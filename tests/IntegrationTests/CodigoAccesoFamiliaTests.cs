using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>dbo.codigos_acceso_correo (script 0121) contra SQL real: límites de envío y de fallos, un solo uso (también con peticiones simultáneas), la dirección que se lee
/// con el ámbito de la familia, la purga y que un código cerrado no revive.</summary>
public class CodigoAccesoFamiliaTests
{
    private static readonly DateTime Start = new(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc);

    private static SqlFamilyAccessCodeStore Store() => new(TestDatabase.ConnectionFactory, TestDatabase.ConnectionString);

    private static async Task<AccountId> SeedAccountAsync()
    {
        var accountId = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.credenciales (cuenta_id, actualizada_en) VALUES (@accountId, SYSUTCDATETIME());
            """, new { accountId, subject = $"codigo-{accountId:N}" });
        return AccountId.From(accountId);
    }

    private static NewAccessCode Code(AccountId account, DateTime at, byte[]? digest = null) =>
        new(Guid.NewGuid(), account, digest ?? RandomNumberGenerator.GetBytes(32), at, at.AddMinutes(10), "es", "1");

    private static async Task<NewAccessCode> ReservedAsync(AccountId account, DateTime at)
    {
        var code = Code(account, at);
        Assert.Equal(AccessCodeReservation.Reserved, await Store().ReserveAsync(code));
        return code;
    }

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static Task<string?> ClosedAsAsync(Guid codeId) =>
        ScalarAsync<string?>("SELECT motivo_cierre FROM dbo.codigos_acceso_correo WHERE id = @codeId", new { codeId });

    [Fact]
    public async Task Envios_EsperaUnMinuto_CincoPorHora_YDiezPorDia()
    {
        var account = await SeedAccountAsync();
        var first = await ReservedAsync(account, Start);

        Assert.Equal(AccessCodeReservation.TooSoon, await Store().ReserveAsync(Code(account, Start.AddSeconds(59))));
        for (var i = 1; i < 5; i++)
        {
            await ReservedAsync(account, Start.AddMinutes(i * 2));
        }
        Assert.Equal("ANULADO", await ClosedAsAsync(first.Id));
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.codigos_acceso_correo WHERE cuenta_id = @id AND resumen IS NOT NULL", new { id = account.Value }));
        Assert.Equal(AccessCodeReservation.HourLimit, await Store().ReserveAsync(Code(account, Start.AddMinutes(30))));

        for (var i = 0; i < 5; i++)
        {
            await ReservedAsync(account, Start.AddHours(3).AddMinutes(i * 2));
        }
        Assert.Equal(AccessCodeReservation.DayLimit, await Store().ReserveAsync(Code(account, Start.AddHours(6))));
        await ReservedAsync(account, Start.AddHours(25));
    }

    [Fact]
    public async Task UnCodigo_ValeUnaSolaVez_SoloParaSuCuenta_YNoCaducado()
    {
        var account = await SeedAccountAsync();
        var other = await SeedAccountAsync();
        var code = await ReservedAsync(account, Start);

        Assert.Equal(AccessCodeCheck.NotLive, await Store().RedeemAsync(code.Id, other, code.Digest, Start.AddMinutes(1)));
        Assert.Equal(AccessCodeCheck.NotLive, await Store().RedeemAsync(code.Id, account, code.Digest, Start.AddMinutes(10)));
        Assert.Equal(AccessCodeCheck.Wrong, await Store().RedeemAsync(code.Id, account, new byte[32], Start.AddMinutes(1)));
        Assert.Equal(AccessCodeCheck.Valid, await Store().RedeemAsync(code.Id, account, code.Digest, Start.AddMinutes(2)));
        Assert.Equal(AccessCodeCheck.NotLive, await Store().RedeemAsync(code.Id, account, code.Digest, Start.AddMinutes(2)));
        Assert.Equal("USADO", await ClosedAsAsync(code.Id));

        var second = await ReservedAsync(account, Start.AddMinutes(3));
        var third = await ReservedAsync(account, Start.AddMinutes(5));
        Assert.Equal(AccessCodeCheck.NotLive, await Store().RedeemAsync(second.Id, account, second.Digest, Start.AddMinutes(5)));
        Assert.Equal(AccessCodeCheck.Valid, await Store().RedeemAsync(third.Id, account, third.Digest, Start.AddMinutes(5)));
    }

    [Fact]
    public async Task PeticionesSimultaneas_UnSoloEnvio_YUnaSolaEntrada()
    {
        var account = await SeedAccountAsync();
        var codes = Enumerable.Range(0, 6).Select(_ => Code(account, Start)).ToList();

        var reservations = await Task.WhenAll(codes.Select(code => Store().ReserveAsync(code)));
        Assert.Single(reservations, result => result == AccessCodeReservation.Reserved);

        var live = codes[Array.IndexOf(reservations, AccessCodeReservation.Reserved)];
        var redeemed = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Store().RedeemAsync(live.Id, account, live.Digest, Start.AddMinutes(1))));
        Assert.Single(redeemed, result => result == AccessCodeCheck.Valid);
        Assert.All(redeemed.Where(result => result != AccessCodeCheck.Valid), result => Assert.Equal(AccessCodeCheck.NotLive, result));
    }

    [Fact]
    public async Task CincoFallosAgotanElCodigo_QuinceEnUnDiaBloquean_YLaInvitacionDesbloquea()
    {
        var account = await SeedAccountAsync();
        var results = new List<AccessCodeCheck>();
        NewAccessCode code = null!;
        for (var round = 0; round < 3; round++)
        {
            code = await ReservedAsync(account, Start.AddMinutes(round * 2));
            for (var attempt = 0; attempt < 5; attempt++)
            {
                results.Add(await Store().RedeemAsync(code.Id, account, new byte[32], Start.AddMinutes(round * 2)));
            }
        }

        Assert.Equal(4, results.Take(4).Count(result => result == AccessCodeCheck.Wrong));
        Assert.Equal(AccessCodeCheck.Exhausted, results[4]);
        Assert.Equal(AccessCodeCheck.Exhausted, results[9]);
        Assert.Equal(AccessCodeCheck.Blocked, results[14]);
        Assert.DoesNotContain(AccessCodeCheck.Blocked, results.Take(14));
        // Bloqueada: ni el último código, ni uno nuevo, ni al día siguiente.
        Assert.Equal(AccessCodeCheck.Blocked, await Store().RedeemAsync(code.Id, account, code.Digest, Start.AddMinutes(5)));
        Assert.Equal(AccessCodeReservation.Blocked, await Store().ReserveAsync(Code(account, Start.AddDays(3))));

        await Store().ResetAsync(account, Start.AddMinutes(6));

        var after = await ReservedAsync(account, Start.AddMinutes(6));
        Assert.Equal(AccessCodeCheck.Wrong, await Store().RedeemAsync(after.Id, account, new byte[32], Start.AddMinutes(6)));
        Assert.Equal(AccessCodeCheck.Valid, await Store().RedeemAsync(after.Id, account, after.Digest, Start.AddMinutes(6)));
    }

    [Fact]
    public async Task UnEnvioFallido_DejaElCodigoSinValor_ConSuMotivo()
    {
        var account = await SeedAccountAsync();
        var code = await ReservedAsync(account, Start);

        await Store().RecordSendAsync(code.Id, null, EmailLog.ProviderRejected, Start);

        Assert.Equal("NO_ENVIADO", await ClosedAsAsync(code.Id));
        Assert.Equal("FALLIDO/ACS_RECHAZADO", await ScalarAsync<string>(
            "SELECT envio_estado_codigo + '/' + envio_motivo_codigo FROM dbo.codigos_acceso_correo WHERE id = @Id", new { code.Id }));
        Assert.Equal(AccessCodeCheck.NotLive, await Store().RedeemAsync(code.Id, account, code.Digest, Start));
    }

    [Fact]
    public async Task UnCodigoCerrado_NoRevive_NiBajaSusFallos()
    {
        var account = await SeedAccountAsync();
        var code = await ReservedAsync(account, Start);
        await Store().RedeemAsync(code.Id, account, new byte[32], Start);
        await Store().RedeemAsync(code.Id, account, code.Digest, Start);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        foreach (var change in new[]
        {
            "resumen = 0x01, cerrado_en = NULL, motivo_cierre = NULL", "intentos_fallidos = 0", "caduca_en = DATEADD(DAY, 1, caduca_en)",
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(
                () => connection.ExecuteAsync($"UPDATE dbo.codigos_acceso_correo SET {change} WHERE id = @Id", new { code.Id }));
            Assert.Equal(51180, error.Number);
        }
    }

    [Fact]
    public async Task LaPurga_CierraLosCaducados_BorraLosDeMasDeTreintaDias_YSoloLaEjecutaLaTarea()
    {
        var old = await ReservedAsync(await SeedAccountAsync(), DateTime.UtcNow.AddDays(-31));
        var recent = await ReservedAsync(await SeedAccountAsync(), DateTime.UtcNow.AddDays(-29));
        var live = await ReservedAsync(await SeedAccountAsync(), DateTime.UtcNow);

        await new SqlAccessCodePurge(TestDatabase.ConnectionFactory).PurgeAsync();

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.codigos_acceso_correo WHERE id = @Id", new { old.Id }));
        Assert.Equal("CADUCADO", await ClosedAsAsync(recent.Id));
        Assert.Null(await ClosedAsAsync(live.Id));
        Assert.Equal(["residapp_tareas"], await GranteesAsync("purgar_codigos_acceso"));
    }

    private static async Task<List<string>> GranteesAsync(string procedure)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<string>("""
            SELECT USER_NAME(grantee_principal_id) FROM sys.database_permissions
             WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.' + @procedure) AND permission_name = N'EXECUTE' AND state IN ('G', 'W')
            """, new { procedure })).ToList();
    }

    [Fact]
    public async Task LaDireccion_EsSoloLaPropia_YSiFaltaONoCoincide_NoHayNinguna()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var firstCenter = await SeedCenterAsync(connection);
        var secondCenter = await SeedCenterAsync(connection);

        var one = await SeedFamilyAsync(connection, (firstCenter, " una@example.test "));
        var neighbour = await SeedFamilyAsync(connection, (firstCenter, "vecina@example.test"));
        var without = await SeedFamilyAsync(connection, (firstCenter, null));
        var twice = await SeedFamilyAsync(connection, (firstCenter, "misma@example.test"), (secondCenter, "Misma@example.test"));
        var different = await SeedFamilyAsync(connection, (firstCenter, "a@example.test"), (secondCenter, "b@example.test"));

        Task<string?> AddressAsync(Family family) => Store().FindAddressAsync(family.Subject, family.Account, family.Scopes);

        Assert.Equal("una@example.test", await AddressAsync(one));
        Assert.Equal("vecina@example.test", await AddressAsync(neighbour));
        Assert.Null(await AddressAsync(without));
        Assert.Equal("misma@example.test", await AddressAsync(twice), ignoreCase: true);
        Assert.Null(await AddressAsync(different));
        // Un ámbito de un centro donde la cuenta no tiene ficha no trae la de nadie.
        Assert.Null(await Store().FindAddressAsync(one.Subject, one.Account, [one.Scopes[0] with { CenterId = secondCenter }]));
    }

    private sealed record Family(string Subject, AccountId Account, List<ActiveProfileScope> Scopes);

    private static async Task<CenterId> SeedCenterAsync(SqlConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@id, @code, @code, 'ACTIVE', SYSUTCDATETIME())",
            new { id, code = $"CODIGO-{id:N}"[..30] });
        return CenterId.From(id);
    }

    /// <summary>Una cuenta de familia con un ámbito y una ficha (con el correo indicado, o sin él) en cada centro.</summary>
    private static async Task<Family> SeedFamilyAsync(SqlConnection connection, params (CenterId Center, string? Email)[] records)
    {
        var account = await SeedAccountAsync();
        var subject = $"codigo-{account.Value:N}";
        var scopes = new List<ActiveProfileScope>();
        foreach (var (center, email) in records)
        {
            var scopeId = Guid.NewGuid();
            await connection.ExecuteAsync("""
                DECLARE @now DATETIME2(3) = SYSUTCDATETIME();
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @accountId, @centerId, 'FAMILIAR', 'ACTIVE', @now, @accountId);
                INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, correo, creado_por_cuenta_id, creado_en, cuenta_id)
                VALUES (NEWID(), @centerId, N'Familiar de prueba', N'600000000', @email, @accountId, @now, @accountId);
                """, new { scopeId, accountId = account.Value, centerId = center.Value, email });
            scopes.Add(new ActiveProfileScope(scopeId, account, center, "Centro de prueba", SystemProfile.Familiar));
        }
        return new Family(subject, account, scopes);
    }
}
