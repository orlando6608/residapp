using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using ResidApp.Application.Ports;
using ResidApp.Infrastructure.Identity;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>dbo.credenciales y dbo.eventos_acceso (0062) contra SQL real, a través del UserManager de ASP.NET Core Identity configurado
/// como en la aplicación (ADR 0007, incremento I1): contraseña, bloqueo, intentos simultáneos, códigos de recuperación guardados como
/// resúmenes y de un solo uso, y el registro de accesos inmutable.</summary>
public class SqlCredentialStoreTests
{
    private static UserManager<CredentialAccount> Users()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TestDatabase.ConnectionFactory)
            .AddSingleton(TimeProvider.System);
        services.AddIdentityCore<CredentialAccount>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.AllowedUserNameCharacters = string.Empty;
            })
            .AddUserStore<SqlCredentialStore>()
            .AddTokenProvider<AuthenticatorTokenProvider<CredentialAccount>>(TokenOptions.DefaultAuthenticatorProvider);
        services.AddScoped<ILookupNormalizer, SubjectLookupNormalizer>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<UserManager<CredentialAccount>>();
    }

    private static async Task<(Guid AccountId, string Subject)> SeedAccountAsync()
    {
        var accountId = Guid.NewGuid();
        var subject = $"credencial-{Guid.NewGuid():N}";
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@accountId, @subject, 'ACTIVE', SYSUTCDATETIME())",
            new { accountId, subject });
        return (accountId, subject);
    }

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    [Fact]
    public async Task SinFila_NoTieneContrasena_YAlPonerlaSeGuardaSoloElHash()
    {
        var (accountId, subject) = await SeedAccountAsync();
        var users = Users();

        var before = await users.FindByNameAsync($"  {subject} ");
        var stampBefore = before!.SecurityStamp;
        var hadPassword = await users.HasPasswordAsync(before);
        var added = await users.AddPasswordAsync(before, "una contraseña larga");
        var after = await Users().FindByNameAsync(subject);
        var stored = await ScalarAsync<string>("SELECT hash_contrasena FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId });

        Assert.Equal(accountId, before.Id);
        Assert.Null(stampBefore);
        Assert.False(hadPassword);
        Assert.True(added.Succeeded);
        Assert.True(await Users().CheckPasswordAsync(after!, "una contraseña larga"));
        Assert.False(await Users().CheckPasswordAsync(after!, "otra contraseña larga"));
        Assert.DoesNotContain("contraseña", stored);
        Assert.NotNull(after!.SecurityStamp);
        Assert.Null(await Users().FindByNameAsync($"credencial-{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task CincoFallos_BloqueanLaCuenta_YElContadorVuelveACero()
    {
        var (accountId, subject) = await SeedAccountAsync();
        var users = Users();
        await users.AddPasswordAsync((await users.FindByNameAsync(subject))!, "una contraseña larga");

        var lockedAfter = new List<bool>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var account = (await Users().FindByNameAsync(subject))!;
            await Users().AccessFailedAsync(account);
            lockedAfter.Add(await Users().IsLockedOutAsync((await Users().FindByNameAsync(subject))!));
        }

        Assert.Equal([false, false, false, false, true], lockedAfter);
        Assert.Equal(0, await ScalarAsync<int>("SELECT intentos_fallidos FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId }));
        Assert.True(await ScalarAsync<DateTimeOffset>(
            "SELECT bloqueada_hasta FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId }) > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task DosFallosALaVez_CuentanLosDos()
    {
        var (accountId, subject) = await SeedAccountAsync();
        var users = Users();
        await users.AddPasswordAsync((await users.FindByNameAsync(subject))!, "una contraseña larga");
        // Las dos lecturas antes de fallar: con un contador escrito desde memoria, el segundo pisaría al primero.
        var first = (await Users().FindByNameAsync(subject))!;
        var second = (await Users().FindByNameAsync(subject))!;

        await Task.WhenAll(Users().AccessFailedAsync(first), Users().AccessFailedAsync(second));

        Assert.Equal(2, await ScalarAsync<int>("SELECT intentos_fallidos FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId }));
    }

    [Fact]
    public async Task CodigosDeRecuperacion_GuardadosComoResumen_YDeUnSoloUso()
    {
        var (accountId, subject) = await SeedAccountAsync();
        var users = Users();
        var account = (await users.FindByNameAsync(subject))!;
        await users.AddPasswordAsync(account, "una contraseña larga");
        await users.ResetAuthenticatorKeyAsync(account);
        await users.SetTwoFactorEnabledAsync(account, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 10))!.ToList();
        var stored = await ScalarAsync<string>("SELECT codigos_recuperacion FROM dbo.credenciales WHERE cuenta_id = @accountId", new { accountId });

        var first = await Users().RedeemTwoFactorRecoveryCodeAsync((await Users().FindByNameAsync(subject))!, $" {codes[0].ToLowerInvariant()} ");
        var again = await Users().RedeemTwoFactorRecoveryCodeAsync((await Users().FindByNameAsync(subject))!, codes[0]);
        var unknown = await Users().RedeemTwoFactorRecoveryCodeAsync((await Users().FindByNameAsync(subject))!, "NO-EXISTE");

        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, code => Assert.DoesNotContain(code.ToUpperInvariant(), stored.ToUpperInvariant()));
        Assert.True(first.Succeeded);
        Assert.False(again.Succeeded);
        Assert.False(unknown.Succeeded);
        Assert.Equal(9, await Users().CountRecoveryCodesAsync((await Users().FindByNameAsync(subject))!));
    }

    [Fact]
    public async Task SegundoFactorActivo_SinClave_LoRechazaLaBase()
    {
        var (accountId, _) = await SeedAccountAsync();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.credenciales (cuenta_id, segundo_factor_activo, actualizada_en) VALUES (@accountId, 1, SYSUTCDATETIME())
            """, new { accountId }));
    }

    [Fact]
    public async Task RegistroDeAccesos_SinCuentaSiNoExiste_EInmutable()
    {
        var (accountId, _) = await SeedAccountAsync();
        var log = new SqlAccessEventLog(TestDatabase.ConnectionFactory);
        var now = DateTime.UtcNow;

        await log.RecordAsync(AccessEventType.ContrasenaFallida, null, null, "203.0.113.9", now);
        await log.RecordAsync(AccessEventType.Bloqueo, AccountId.From(accountId), null, null, now);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_acceso SET tipo = 'ENTRADA' WHERE cuenta_id = @accountId", new { accountId }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.eventos_acceso WHERE cuenta_id = @accountId", new { accountId }));
        // Una entrada siempre abre una sesión.
        await Assert.ThrowsAsync<SqlException>(() => log.RecordAsync(AccessEventType.Entrada, AccountId.From(accountId), null, null, now));

        Assert.Equal("BLOQUEO", await ScalarAsync<string>(
            "SELECT tipo FROM dbo.eventos_acceso WITH (NOLOCK) WHERE cuenta_id = @accountId", new { accountId }));
        Assert.Contains("ACCESS_EVENT_UPDATE_FORBIDDEN", update.Message);
        Assert.Contains("ACCESS_EVENT_DELETE_FORBIDDEN", delete.Message);
    }
}
