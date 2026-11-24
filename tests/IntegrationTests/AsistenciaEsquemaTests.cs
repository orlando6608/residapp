using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Platform;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// El esquema de la asistencia del Centro de Día (script 0115, ADR 0019): cuatro tablas de solo añadir con sus reglas en la propia base de datos. Se prueban con SQL directo, como dueño de la base
/// (los disparadores y las restricciones rigen para cualquiera): solo en un Centro de Día, formas válidas e inválidas, versiones, solapes y que nada se actualiza ni se borra.
/// </summary>
public class AsistenciaEsquemaTests
{
    private sealed record Day(Guid CenterId, Guid AccountId, Guid ResidentId, Guid UnitId);

    private sealed class FixedTenant(TenantScope? scope) : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult(scope);
    }

    private sealed class FixedIdentity(string subject) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) => Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(subject));
    }

    private static async Task<Day> CreateCenterWithResidentAsync(CenterServiceType type)
    {
        var accountId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var subject = $"test-asist-{Guid.NewGuid():N}"[..28];
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de asistencia', 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject, scopeId, centerId = PlatformCenter.Id });
        }

        var factory = new SqlConnectionFactory(TestDatabase.ConnectionString, new FixedTenant(new TenantScope(subject, scopeId)));
        var service = new PlatformApplicationService(
            new SqlProfileScopeDirectoryProvider(factory), new FixedIdentity(subject), new SqlPlatformCenterDirectory(factory), new SqlPlatformCenterRepository(factory),
            new SqlAccessInvitationRepository(factory), new SqlCenterModuleRepository(factory), TimeProvider.System, new SqlCenterTimeZones(factory), new SqlCenterTimeZones(factory),
            new SqlCenterServiceTypes(factory));
        var centerId = Guid.NewGuid();
        var adminSubject = $"test-adm-{Guid.NewGuid():N}"[..28];
        string Code() => $"asi-{Guid.NewGuid():N}"[..20];
        var created = await service.CreateCenterAsync(new CreateCenterCommand(
            scopeId, CenterId.From(PlatformCenter.Id), centerId, Code(), "Centro de asistencia (ficticio)", Code(), "Sala 1", adminSubject, "Admin Asistencia", TipoDeServicio: type));
        Assert.True(created.Ok, created.Error?.Message);

        using var db = await TestDatabase.ConnectionFactory.OpenAsync();
        var admin = await db.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @adminSubject", new { adminSubject });
        var unit = await db.ExecuteScalarAsync<Guid>("SELECT TOP (1) id FROM dbo.unidades WHERE centro_id = @centerId", new { centerId });
        var resident = (await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            AccountId.From(admin), SystemProfile.Administracion, CenterId.From(centerId), UnitId.From(unit), "Persona de asistencia (ficticia)",
            new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid(), LocationPending: true))).ResidentId;
        return new Day(centerId, admin, resident.Value, unit);
    }

    private static async Task<string?> TryAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        try
        {
            await connection.ExecuteAsync(sql, parameters);
            return null;
        }
        catch (SqlException error)
        {
            return error.Message;
        }
    }

    private static DateTime Day0(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static Task<string?> AttendanceAsync(Day d, string state, string? cause, string? note, int number = 1, DateOnly? date = null, string profile = "AUXILIAR") => TryAsync("""
        INSERT INTO dbo.asistencia_dia (id, centro_id, residente_id, fecha, numero, estado_codigo, causa_codigo, nota, anotado_por_cuenta_id, anotado_por_perfil, anotado_en)
        VALUES (NEWID(), @CenterId, @ResidentId, @date, @number, @state, @cause, @note, @AccountId, @profile, SYSUTCDATETIME())
        """, new { d.CenterId, d.ResidentId, d.AccountId, date = Day0(date ?? Today), number, state, cause, note, profile });

    private static Task<string?> AbsenceAsync(Day d, Guid id, string type, Guid reference, DateOnly? from, DateOnly? to, string? reason, string? cause) => TryAsync("""
        INSERT INTO dbo.ausencias_programadas (id, centro_id, residente_id, tipo_codigo, ausencia_id, desde, hasta, motivo_codigo, causa_codigo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
        VALUES (@id, @CenterId, @ResidentId, @type, @reference, @from, @to, @reason, @cause, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
        """, new
    {
        d.CenterId, d.ResidentId, d.AccountId, id, type, reference, from = from is null ? (DateTime?)null : Day0(from.Value), to = to is null ? (DateTime?)null : Day0(to.Value), reason, cause,
    });

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Theory]
    [InlineData("asistencia_centro")]
    [InlineData("asistencia_pauta")]
    [InlineData("asistencia_dia")]
    [InlineData("ausencias_programadas")]
    public async Task Una_residencia_no_admite_ninguna_fila_de_asistencia(string table)
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.Residencia);
        var absence = Guid.NewGuid();

        var message = table switch
        {
            "asistencia_centro" => await TryAsync("""
                INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (NEWID(), @CenterId, 1, 1, 1, 1, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { d.CenterId, d.AccountId }),
            "asistencia_pauta" => await TryAsync("""
                INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (NEWID(), @CenterId, @ResidentId, 1, 31, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { d.CenterId, d.ResidentId, d.AccountId }),
            "asistencia_dia" => await AttendanceAsync(d, "ASISTIO", null, null),
            _ => await AbsenceAsync(d, absence, "REGISTRADA", absence, Today, Today, "VACACIONES", null),
        };

        Assert.Contains("ASSISTANCE_CENTER_INVALID", message ?? "sin error");
    }

    [Fact]
    public async Task El_interruptor_empieza_apagado_encender_exige_las_dos_confirmaciones_y_cada_fila_cambia_el_valor()
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);
        Task<string?> Switch(int number, bool on, bool informed = true, bool eipd = true) => TryAsync("""
            INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, @number, @on, @informed, @eipd, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { d.CenterId, d.AccountId, number, on, informed, eipd });

        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Switch(1, on: false) ?? "");
        Assert.Contains("CK_asc_confirmaciones", await Switch(1, on: true, informed: false) ?? "");
        Assert.Contains("CK_asc_confirmaciones", await Switch(1, on: true, eipd: false) ?? "");
        Assert.Null(await Switch(1, on: true));
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Switch(2, on: true) ?? "");
        Assert.Null(await Switch(2, on: false, informed: false, eipd: false));
        Assert.Null(await Switch(3, on: true));
    }

    [Fact]
    public async Task La_pauta_admite_de_1_a_127_y_cada_version_es_distinta_de_la_anterior()
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);
        Task<string?> Pattern(int number, int days) => TryAsync("""
            INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, @ResidentId, @number, @days, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { d.CenterId, d.ResidentId, d.AccountId, number, days });

        Assert.Contains("CK_asp_dias", await Pattern(1, 0) ?? "");
        Assert.Contains("CK_asp_dias", await Pattern(1, 128) ?? "");
        Assert.Null(await Pattern(1, 21));
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Pattern(2, 21) ?? "");
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await Pattern(3, 31) ?? "");
        Assert.Null(await Pattern(2, 31));
    }

    [Fact]
    public async Task La_asistencia_exige_causa_con_NO_ASISTIO_nota_solo_con_OTRO_de_una_linea_y_80_caracteres()
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);

        Assert.Contains("CK_asd_causa", await AttendanceAsync(d, "NO_ASISTIO", null, null) ?? "");
        Assert.Contains("CK_asd_causa", await AttendanceAsync(d, "ASISTIO", "SIN_AVISO", null) ?? "");
        Assert.Contains("CK_asd_causa", await AttendanceAsync(d, "NO_ASISTIO", "DIAGNOSTICO", null) ?? "");
        Assert.Contains("CK_asd_nota", await AttendanceAsync(d, "NO_ASISTIO", "SIN_AVISO", "una nota") ?? "");
        Assert.Contains("CK_asd_nota", await AttendanceAsync(d, "NO_ASISTIO", "OTRO", "dos\nlíneas") ?? "");
        Assert.Contains("CK_asd_nota", await AttendanceAsync(d, "NO_ASISTIO", "OTRO", "   ") ?? "");
        Assert.NotNull(await AttendanceAsync(d, "NO_ASISTIO", "OTRO", new string('x', 81)));
        Assert.Contains("CK_asd_perfil", await AttendanceAsync(d, "ASISTIO", null, null, profile: "ADMINISTRACION") ?? "");
        Assert.Null(await AttendanceAsync(d, "NO_ASISTIO", "OTRO", new string('x', 80)));
    }

    [Fact]
    public async Task La_asistencia_no_es_futura_ni_anterior_al_alta_y_cada_correccion_cambia_algo_y_sigue_a_la_anterior()
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);

        Assert.Contains("ASSISTANCE_DATE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, date: Today.AddDays(2)) ?? "");
        Assert.Contains("ASSISTANCE_DATE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, date: Today.AddDays(-2)) ?? "");
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, number: 2) ?? "");
        Assert.Null(await AttendanceAsync(d, "ASISTIO", null, null));
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await AttendanceAsync(d, "ASISTIO", null, null, number: 2) ?? "");
        Assert.Contains("ASSISTANCE_SEQUENCE_INVALID", await AttendanceAsync(d, "NO_ASISTIO", "SIN_AVISO", null, number: 3) ?? "");
        Assert.Null(await AttendanceAsync(d, "NO_ASISTIO", "SIN_AVISO", null, number: 2));
        Assert.Contains("UX_asd_numero", await AttendanceAsync(d, "NO_ASISTIO", "AVISO_FAMILIA", null, number: 2) ?? "");
    }

    [Fact]
    public async Task Un_residente_suspendido_no_admite_asistencia()
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);
        Assert.Null(await TryAsync("""
            INSERT INTO dbo.suspensiones_residente (id, residente_id, centro_id, unidad_id, iniciada_por_cuenta_id, iniciada_en)
            VALUES (NEWID(), @ResidentId, @CenterId, @UnitId, @AccountId, SYSUTCDATETIME())
            """, new { d.ResidentId, d.CenterId, d.UnitId, d.AccountId }));

        Assert.Contains("ASSISTANCE_RESIDENT_INVALID", await AttendanceAsync(d, "ASISTIO", null, null) ?? "sin error");
    }

    [Fact]
    public async Task Las_ausencias_tienen_hasta_90_dias_como_maximo_sin_solapes_y_anular_o_acortar_siguen_reglas()
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);
        var first = Guid.NewGuid();
        var start = Today.AddDays(1);

        Assert.Contains("CK_aup_forma", await AbsenceAsync(d, Guid.NewGuid(), "REGISTRADA", Guid.Empty, start, null, "VACACIONES", null) ?? "");
        var tooLong = Guid.NewGuid();
        Assert.Contains("CK_aup_forma", await AbsenceAsync(d, tooLong, "REGISTRADA", tooLong, start, start.AddDays(90), "VACACIONES", null) ?? "");
        var badReason = Guid.NewGuid();
        Assert.Contains("CK_aup_motivo", await AbsenceAsync(d, badReason, "REGISTRADA", badReason, start, start.AddDays(3), "GRIPE", null) ?? "");
        Assert.Null(await AbsenceAsync(d, first, "REGISTRADA", first, start, start.AddDays(89), "INGRESO_HOSPITALARIO", null));

        var overlapping = Guid.NewGuid();
        Assert.Contains("ASSISTANCE_ABSENCE_OVERLAP", await AbsenceAsync(d, overlapping, "REGISTRADA", overlapping, start.AddDays(10), start.AddDays(12), "OTRO", null) ?? "");

        // Acortar: debe terminar antes del último día vigente y no antes del primero.
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", first, null, start.AddDays(89), null, "REGRESO_ANTES") ?? "");
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", first, null, start.AddDays(-1), null, "REGRESO_ANTES") ?? "");
        Assert.Null(await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", first, null, start.AddDays(5), null, "REGRESO_ANTES"));
        // Ya acortada: ahora sí cabe otra que empiece después del nuevo último día.
        Assert.Null(await AbsenceAsync(d, overlapping, "REGISTRADA", overlapping, start.AddDays(10), start.AddDays(12), "OTRO", null));

        // Anular: una vez; después no admite más cambios y deja de contar para los solapes.
        Assert.Null(await AbsenceAsync(d, Guid.NewGuid(), "ANULADA", overlapping, null, null, null, "ERROR"));
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ANULADA", overlapping, null, null, null, "ERROR") ?? "");
        Assert.Contains("ASSISTANCE_ABSENCE_INVALID", await AbsenceAsync(d, Guid.NewGuid(), "ACORTADA", overlapping, null, start.AddDays(11), null, "REGRESO_ANTES") ?? "");
        var again = Guid.NewGuid();
        Assert.Null(await AbsenceAsync(d, again, "REGISTRADA", again, start.AddDays(10), start.AddDays(12), "OTRO", null));
    }

    [Theory]
    [InlineData("asistencia_centro")]
    [InlineData("asistencia_pauta")]
    [InlineData("asistencia_dia")]
    [InlineData("ausencias_programadas")]
    public async Task Ninguna_tabla_de_asistencia_se_actualiza_ni_se_borra(string table)
    {
        var d = await CreateCenterWithResidentAsync(CenterServiceType.CentroDeDia);
        var id = Guid.NewGuid();
        var inserted = table switch
        {
            "asistencia_centro" => await TryAsync("""
                INSERT INTO dbo.asistencia_centro (id, centro_id, numero, activa, confirma_informada, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@id, @CenterId, 1, 1, 1, 1, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { id, d.CenterId, d.AccountId }),
            "asistencia_pauta" => await TryAsync("""
                INSERT INTO dbo.asistencia_pauta (id, centro_id, residente_id, numero, dias_esperados, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@id, @CenterId, @ResidentId, 1, 31, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { id, d.CenterId, d.ResidentId, d.AccountId }),
            "asistencia_dia" => await TryAsync("""
                INSERT INTO dbo.asistencia_dia (id, centro_id, residente_id, fecha, numero, estado_codigo, anotado_por_cuenta_id, anotado_por_perfil, anotado_en)
                VALUES (@id, @CenterId, @ResidentId, CAST(SYSUTCDATETIME() AS DATE), 1, 'ASISTIO', @AccountId, 'AUXILIAR', SYSUTCDATETIME())
                """, new { id, d.CenterId, d.ResidentId, d.AccountId }),
            _ => await AbsenceAsync(d, id, "REGISTRADA", id, Today, Today, "VACACIONES", null),
        };
        Assert.Null(inserted);

        Assert.Contains("ASSISTANCE_IMMUTABLE", await TryAsync($"UPDATE dbo.{table} SET centro_id = centro_id WHERE id = @id", new { id }) ?? "");
        Assert.Contains("ASSISTANCE_IMMUTABLE", await TryAsync($"DELETE FROM dbo.{table} WHERE id = @id", new { id }) ?? "");
    }
}
