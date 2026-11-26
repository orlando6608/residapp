using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// Zona horaria por centro (script 0109, F1 del estudio de zonas horarias): el catálogo, la fila de alta al crear el centro, la corrección solo por Plataforma con motivo, recuento de
/// lo afectado y auditoría, y la historia de solo añadir. Canarias está en el catálogo pero inactiva hasta F3: estas pruebas la activan mientras corren y la dejan como estaba.
/// </summary>
public class ZonaHorariaCentroTests
{
    private const string Canarias = "Atlantic/Canary";
    private const string Madrid = "Europe/Madrid";
    private const string Inactiva = "Test/Inactiva";

    private sealed record Operator(string ExternalSubject, AccountId AccountId, Guid ProfileScopeId);

    /// <summary>Una zona del catÃ¡logo que no estÃ¡ activa (no se ofrece ni se puede elegir). Se crea una vez y no se toca el estado de las zonas reales.</summary>
    private static async Task EnsureInactiveZoneAsync()
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.zonas_horarias WHERE codigo = N'Test/Inactiva')
                INSERT INTO dbo.zonas_horarias (codigo, nombre_windows, etiqueta, orden, activa) VALUES (N'Test/Inactiva', N'GMT Standard Time', N'Zona inactiva de pruebas', 99, 0)
            """);
    }

    private static async Task<Operator> CreateOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var subject = $"test-zona-{Guid.NewGuid():N}"[..28];
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de zonas', 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
            """, new { accountId, subject, profileScopeId, centerId = PlatformCenter.Id });
        return new Operator(subject, AccountId.From(accountId), profileScopeId);
    }

    private static PlatformApplicationService BuildPlatform(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedIdentity(subject),
        new SqlPlatformCenterDirectory(TestDatabase.ConnectionFactory), new SqlPlatformCenterRepository(TestDatabase.PlatformFactoryFor(subject)),
        new SqlAccessInvitationRepository(TestDatabase.ConnectionFactory), new SqlCenterModuleRepository(TestDatabase.ConnectionFactory), TimeProvider.System,
        new SqlCenterTimeZones(TestDatabase.ConnectionFactory), new SqlCenterTimeZones(TestDatabase.ConnectionFactory),
        new SqlCenterServiceTypes(TestDatabase.ConnectionFactory));

    private static ChangeCenterTimeZoneCommand Change(
        Operator op, CenterId center, string? zone, CenterTimeZoneChangeReason? reason, int slots = 0, int appointments = 0) =>
        new(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), center, zone, reason, new CenterTimeZoneImpact(slots, appointments));

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    private static async Task AddSlotsAsync(SeededProfile admin, int active, int withdrawn)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        for (var i = 0; i < active + withdrawn; i++)
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.citas_franjas (id, centro_id, equipo_codigo, dia_semana, hora_inicio, hora_fin, duracion_minutos, creada_por_cuenta_id, creada_en, retirada_en)
                VALUES (NEWID(), @center, 'MEDICO', @day, '10:00', '12:00', 30, @account, SYSUTCDATETIME(), CASE WHEN @withdrawn = 1 THEN SYSUTCDATETIME() END)
                """, new { center = admin.CenterId.Value, day = (i % 7) + 1, account = admin.AccountId.Value, withdrawn = i >= active });
        }
    }

    private const int HoursIn2026And2027 = 24 * (365 + 365);

    [Fact]
    public async Task Cada_zona_del_catalogo_la_conoce_el_servidor_y_la_base_de_datos_y_las_dos_dan_el_mismo_desfase_durante_2026_y_2027()
    {
        // La aplicaciÃ³n calcula con el nombre estÃ¡ndar (TimeZoneInfo: tzdata en Linux) y los avisos de la base con el de Windows (AT TIME ZONE): si discreparan, un recordatorio saldrÃ­a en otra
        // hora que las pantallas. Se comparan TODAS las horas de 2026 y 2027, en los dos sentidos, incluidas las zonas inactivas: una zona cuyos datos no coincidan no se puede activar.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var zones = (await connection.QueryAsync<(string Code, string WindowsName)>(
            "SELECT codigo, nombre_windows FROM dbo.zonas_horarias WHERE codigo NOT LIKE N'Test/%' ORDER BY orden")).ToList();
        Assert.Contains(zones, zone => zone.Code == Madrid);
        Assert.Contains(zones, zone => zone.Code == Canarias);
        Assert.Contains(zones, zone => zone.Code == "America/New_York");
        Assert.Contains(zones, zone => zone.Code == "America/Mexico_City");

        var origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var mismatches = new List<string>();
        foreach (var (code, windowsName) in zones)
        {
            var net = TimeZoneInfo.FindSystemTimeZoneById(code);

            // UTC -> local: el desfase que SQL aplica a cada instante frente al de .NET.
            var utcToLocal = await connection.QueryAsync<(int Hour, int Minutes)>(
                "SELECT value, DATEPART(TZOFFSET, DATEADD(HOUR, CAST(value AS INT), CAST(@origin AS DATETIME2(0))) AT TIME ZONE 'UTC' AT TIME ZONE @windowsName) FROM GENERATE_SERIES(0, @last)",
                new { origin, windowsName, last = HoursIn2026And2027 - 1 });
            foreach (var (hour, minutes) in utcToLocal)
            {
                var instant = new DateTimeOffset(origin.AddHours(hour), TimeSpan.Zero);
                if ((int)net.GetUtcOffset(instant).TotalMinutes != minutes)
                {
                    mismatches.Add($"{code} UTCâ†’local {instant:yyyy-MM-dd HH}Z: .NET {(int)net.GetUtcOffset(instant).TotalMinutes} min, SQL {minutes} min");
                    break;
                }
            }

            // local -> UTC: la hora de reloj (sin zona) que SQL entiende y .NET entiende igual; las horas que no existen o se repiten solo se comprueba que SQL no falla.
            var localToUtc = await connection.QueryAsync<(int Hour, int Minutes)>(
                "SELECT value, DATEPART(TZOFFSET, CAST(DATEADD(HOUR, CAST(value AS INT), CAST(@origin AS DATETIME2(0))) AS DATETIME2(0)) AT TIME ZONE @windowsName) FROM GENERATE_SERIES(0, @last)",
                new { origin = DateTime.SpecifyKind(origin, DateTimeKind.Unspecified), windowsName, last = HoursIn2026And2027 - 1 });
            foreach (var (hour, minutes) in localToUtc)
            {
                var local = DateTime.SpecifyKind(origin.AddHours(hour), DateTimeKind.Unspecified);
                if (!net.IsInvalidTime(local) && !net.IsAmbiguousTime(local) && (int)net.GetUtcOffset(local).TotalMinutes != minutes)
                {
                    mismatches.Add($"{code} localâ†’UTC {local:yyyy-MM-dd HH}: .NET {(int)net.GetUtcOffset(local).TotalMinutes} min, SQL {minutes} min");
                    break;
                }
            }
        }

        Assert.True(mismatches.Count == 0, "El servidor (tzdata) y SQL Server no dan el mismo desfase; la zona no se puede activar hasta contrastar su norma oficial:" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }
    [Fact]
    public async Task El_catalogo_ofrece_Madrid_y_Canarias_pero_no_una_zona_inactiva()
    {
        var directory = new SqlCenterTimeZones(TestDatabase.ConnectionFactory);

        var withCanarias = await directory.ListCatalogAsync();
        Assert.Equal([Madrid, Canarias], withCanarias.Select(z => z.Code).Where(c => c is Madrid or Canarias));
        Assert.Equal("Península y Baleares (Madrid)", withCanarias.Single(z => z.Code == Madrid).Label);

        await EnsureInactiveZoneAsync();
        Assert.DoesNotContain(await directory.ListCatalogAsync(), z => z.Code == Inactiva);

        // Miami y MÃ©xico estÃ¡n dadas de alta (0112) pero inactivas hasta que cada una tenga su estudio de cumplimiento: Plataforma no las ofrece.
        Assert.DoesNotContain(withCanarias, z => z.Code is "America/New_York" or "America/Mexico_City");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.zonas_horarias WHERE codigo IN (N'America/New_York', N'America/Mexico_City') AND activa = 1"));
    }

    [Fact]
    public async Task Un_centro_nuevo_guarda_su_zona_como_fila_1_de_alta_y_un_reenvio_con_otra_zona_se_rechaza()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op.ExternalSubject);
        var centerId = Guid.NewGuid();
        string Code() => $"zon-{Guid.NewGuid():N}"[..20];
        string Admin() => $"test-adm-{Guid.NewGuid():N}"[..28];
        var adminSubject = Admin();
        var unitCode = Code();
        var centerCode = Code();
        CreateCenterCommand Create(string zone) => new(
            op.ProfileScopeId, CenterId.From(PlatformCenter.Id), centerId, centerCode, "Residencia Zona (ficticia)", unitCode, "Primera unidad", adminSubject, "Admin Nuevo", null, zone, TipoDeServicio: CenterServiceType.Residencia);

        var created = await service.CreateCenterAsync(Create(Canarias));
        Assert.True(created.Ok, created.Error?.Message);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(int Number, string Zone, string Reason, int Slots, int Appointments, string Profile)>(
            "SELECT numero, zona_codigo, motivo_codigo, franjas_afectadas, citas_afectadas, cambiado_por_perfil FROM dbo.zonas_centro WHERE centro_id = @centerId", new { centerId });
        Assert.Equal((1, Canarias, "ALTA", 0, 0, "PLATAFORMA"), row);

        // El mismo envío repetido no lo duplica; con otra zona es otra petición con la misma clave.
        Assert.True((await service.CreateCenterAsync(Create(Canarias))).Ok);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @centerId", new { centerId }));
        var reused = await service.CreateCenterAsync(Create(Madrid));
        Assert.False(reused.Ok);
    }

    [Fact]
    public async Task Un_centro_sin_zona_elegida_usa_Madrid_y_una_zona_fuera_del_catalogo_no_lo_crea()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op.ExternalSubject);
        string Code() => $"zon-{Guid.NewGuid():N}"[..20];
        CreateCenterCommand Create(Guid id, string? zone) => new(
            op.ProfileScopeId, CenterId.From(PlatformCenter.Id), id, Code(), "Residencia Zona (ficticia)", Code(), "Primera unidad", $"test-adm-{Guid.NewGuid():N}"[..28], "Admin Nuevo", null, zone, TipoDeServicio: CenterServiceType.Residencia);
        var withoutZone = Guid.NewGuid();
        var invalid = Guid.NewGuid();

        Assert.True((await service.CreateCenterAsync(Create(withoutZone, null))).Ok);
        var rejected = await service.CreateCenterAsync(Create(invalid, "Mars/Olympus"));

        Assert.Equal(Madrid, await ScalarAsync<string>("SELECT zona_codigo FROM dbo.zonas_centro WHERE centro_id = @center", new { center = withoutZone }));
        Assert.False(rejected.Ok);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.centros WHERE id = @invalid", new { invalid }));
    }

    private static async Task<T?> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    [Fact]
    public async Task La_base_de_datos_da_a_un_centro_sin_fila_la_misma_zona_por_defecto_que_la_aplicacion()
    {
        // La vista de los avisos por correo (0110) tiene la zona por defecto escrita aparte de CenterTime.DefaultZoneId: si una cambia sin la otra, avisos y pantallas usarían horas distintas.
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var fromView = await connection.QuerySingleAsync<(string Code, string WindowsName)>(
            "SELECT zona_codigo, nombre_windows FROM dbo.zonas_vigentes_centros WHERE centro_id = @center", new { center = admin.CenterId.Value });
        var windowsNameInCatalog = await connection.ExecuteScalarAsync<string>(
            "SELECT nombre_windows FROM dbo.zonas_horarias WHERE codigo = @zone", new { zone = CenterTime.DefaultZoneId });

        Assert.Equal(CenterTime.DefaultZoneId, fromView.Code);
        Assert.Equal(windowsNameInCatalog, fromView.WindowsName);
    }

    [Fact]
    public async Task Un_centro_que_ya_existia_no_tiene_fila_usa_Madrid_y_Plataforma_ve_lo_que_se_veria_afectado()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await AddSlotsAsync(admin, active: 3, withdrawn: 1);
        var service = BuildPlatform(op.ExternalSubject);

        var state = (await service.GetCenterTimeZoneAsync(new PlatformCenterQuery(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), admin.CenterId))).Value!;

        Assert.Equal(Madrid, state.Current.Code);
        Assert.Empty(state.History);
        // Las franjas retiradas no cuentan; sin citas reservadas.
        Assert.Equal(new CenterTimeZoneImpact(3, 0), state.Impact);
        Assert.Equal(Madrid, await new SqlCenterTimeZones(TestDatabase.ConnectionFactory).FindZoneCodeForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId));
    }

    [Fact]
    public async Task Corregir_la_zona_anota_el_alta_y_el_cambio_con_lo_confirmado_deja_la_auditoria_y_la_zona_nueva_se_lee_en_el_acto()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await AddSlotsAsync(admin, active: 2, withdrawn: 0);
        var service = BuildPlatform(op.ExternalSubject);

        var result = await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Canarias, CenterTimeZoneChangeReason.ErrorDeAlta, slots: 2));

        Assert.True(result.Ok, result.Error?.Message);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var rows = (await connection.QueryAsync<(int Number, string Zone, string Reason, int Slots, Guid Actor, string Profile)>(
            "SELECT numero, zona_codigo, motivo_codigo, franjas_afectadas, cambiado_por_cuenta_id, cambiado_por_perfil FROM dbo.zonas_centro WHERE centro_id = @center ORDER BY numero",
            new { center = admin.CenterId.Value })).ToList();
        Assert.Equal([(1, Madrid, "ALTA", 0, op.AccountId.Value, "PLATAFORMA"), (2, Canarias, "ERROR_DE_ALTA", 2, op.AccountId.Value, "PLATAFORMA")], rows);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo = 'CENTER_TIMEZONE_CHANGE' AND perfil_activo = 'PLATAFORMA' AND cuenta_id = @actor AND tipo_recurso = 'CENTER_TIMEZONE'",
            new { center = admin.CenterId.Value, actor = op.AccountId.Value }));
        Assert.Equal(Canarias, await new SqlCenterTimeZones(TestDatabase.ConnectionFactory).FindZoneCodeForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId));

        var state = (await service.GetCenterTimeZoneAsync(new PlatformCenterQuery(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), admin.CenterId))).Value!;
        Assert.Equal(Canarias, state.Current.Code);
        Assert.Equal([2, 1], state.History.Select(h => h.Number));

        // Y se puede volver atrás: otra fila, no una modificación.
        Assert.True((await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Madrid, CenterTimeZoneChangeReason.CambioDeUbicacion, slots: 2))).Ok);
        Assert.Equal(3, await CountAsync("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Si_lo_afectado_ha_cambiado_desde_que_se_vio_no_se_cambia_nada_y_se_pide_confirmar_otra_vez()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await AddSlotsAsync(admin, active: 2, withdrawn: 0);
        var service = BuildPlatform(op.ExternalSubject);

        var stale = await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Canarias, CenterTimeZoneChangeReason.ErrorDeAlta, slots: 1));

        Assert.False(stale.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Contains("revisa los números", stale.Error.Message);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task La_misma_zona_una_zona_fuera_del_catalogo_o_sin_motivo_se_rechazan_sin_dejar_filas()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);

        var same = await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Madrid, CenterTimeZoneChangeReason.ErrorDeAlta));
        var outside = await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, "Mars/Olympus", CenterTimeZoneChangeReason.ErrorDeAlta));
        var withoutReason = await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Canarias, null));
        var withoutZone = await service.ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, null, CenterTimeZoneChangeReason.ErrorDeAlta));

        Assert.Equal(ApplicationFailureCode.Conflict, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, outside.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutZone.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Una_zona_inactiva_no_se_puede_elegir()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await EnsureInactiveZoneAsync();

        var result = await BuildPlatform(op.ExternalSubject).ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Inactiva, CenterTimeZoneChangeReason.ErrorDeAlta));

        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Fact]
    public async Task El_centro_de_Plataforma_y_uno_que_no_existe_no_cambian_de_zona_y_la_base_de_datos_tampoco_deja_darle_fila()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op.ExternalSubject);

        var platform = await service.ChangeCenterTimeZoneAsync(Change(op, CenterId.From(PlatformCenter.Id), Canarias, CenterTimeZoneChangeReason.ErrorDeAlta));
        var missing = await service.ChangeCenterTimeZoneAsync(Change(op, CenterId.From(Guid.NewGuid()), Canarias, CenterTimeZoneChangeReason.ErrorDeAlta));

        Assert.Equal(ApplicationFailureCode.InvalidInput, platform.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, missing.Error!.Code);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var trigger = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.zonas_centro (id, centro_id, numero, zona_codigo, motivo_codigo, franjas_afectadas, citas_afectadas, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, 1, 'Europe/Madrid', 'ALTA', 0, 0, @actor, 'PLATAFORMA', SYSUTCDATETIME())
            """, new { center = PlatformCenter.Id, actor = op.AccountId.Value }));
        Assert.Contains("CENTER_TIMEZONE_CENTER_INVALID", trigger.Message);
    }

    [Fact]
    public async Task Solo_Plataforma_corrige_la_zona_la_Administracion_del_centro_no_puede()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var asAdmin = new PlatformApplicationService(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedIdentity(admin.ExternalSubject),
            new SqlPlatformCenterDirectory(TestDatabase.ConnectionFactory), new SqlPlatformCenterRepository(TestDatabase.ConnectionFactory),
            new SqlAccessInvitationRepository(TestDatabase.ConnectionFactory), new SqlCenterModuleRepository(TestDatabase.ConnectionFactory), TimeProvider.System,
            new SqlCenterTimeZones(TestDatabase.ConnectionFactory), new SqlCenterTimeZones(TestDatabase.ConnectionFactory),
        new SqlCenterServiceTypes(TestDatabase.ConnectionFactory));

        var result = await asAdmin.ChangeCenterTimeZoneAsync(new ChangeCenterTimeZoneCommand(
            admin.ProfileScopeId, admin.CenterId, admin.CenterId, Canarias, CenterTimeZoneChangeReason.ErrorDeAlta, new CenterTimeZoneImpact(0, 0)));

        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.zonas_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task La_historia_es_de_solo_anadir_ni_se_actualiza_ni_se_borra()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await BuildPlatform(op.ExternalSubject).ChangeCenterTimeZoneAsync(Change(op, admin.CenterId, Canarias, CenterTimeZoneChangeReason.ErrorDeAlta))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.zonas_centro SET zona_codigo = 'Europe/Madrid' WHERE centro_id = @center", new { center = admin.CenterId.Value }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.zonas_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));

        Assert.Contains("CENTER_TIMEZONE_IMMUTABLE", update.Message);
        Assert.Contains("CENTER_TIMEZONE_IMMUTABLE", delete.Message);
    }

    [Fact]
    public async Task La_zona_del_ambito_solo_se_da_a_la_cuenta_que_lo_tiene_y_Plataforma_usa_Madrid()
    {
        var op = await CreateOperatorAsync();
        var a = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var b = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await BuildPlatform(op.ExternalSubject).ChangeCenterTimeZoneAsync(Change(op, a.CenterId, Canarias, CenterTimeZoneChangeReason.ErrorDeAlta))).Ok);
        var directory = new SqlCenterTimeZones(TestDatabase.ConnectionFactory);

        Assert.Equal(Canarias, await directory.FindZoneCodeForScopeAsync(a.ExternalSubject, a.ProfileScopeId));
        // El ámbito de otra cuenta, o uno que no existe, no da la zona de ese centro.
        Assert.Null(await directory.FindZoneCodeForScopeAsync(b.ExternalSubject, a.ProfileScopeId));
        Assert.Null(await directory.FindZoneCodeForScopeAsync(a.ExternalSubject, Guid.NewGuid()));
        Assert.Equal(Madrid, await directory.FindZoneCodeForScopeAsync(op.ExternalSubject, op.ProfileScopeId));
        Assert.Equal(Madrid, await directory.FindZoneCodeForScopeAsync(b.ExternalSubject, b.ProfileScopeId));
    }

    [Fact]
    public void La_zona_elegida_tiene_que_ser_del_catalogo_y_el_motivo_uno_de_la_lista()
    {
        CenterTimeZone[] catalog = [new(Madrid, "Madrid"), new(Canarias, "Canarias")];

        Assert.Equal(Canarias, CenterTimeZoneRules.ValidateZone(Canarias, catalog));
        foreach (var bad in new string?[] { null, "", "  ", "Mars/Olympus", "europe/madrid" })
        {
            Assert.Equal(CenterTimeZoneRules.InvalidZone, Assert.Throws<DomainValidationException>(() => CenterTimeZoneRules.ValidateZone(bad, catalog)).Message);
        }

        Assert.Equal(CenterTimeZoneChangeReason.ErrorDeAlta, CenterTimeZoneRules.ValidateReason(CenterTimeZoneChangeReason.ErrorDeAlta));
        Assert.Equal(CenterTimeZoneRules.InvalidReason, Assert.Throws<DomainValidationException>(() => CenterTimeZoneRules.ValidateReason(null)).Message);
        Assert.Equal(CenterTimeZoneRules.InvalidReason, Assert.Throws<DomainValidationException>(() => CenterTimeZoneRules.ValidateReason((CenterTimeZoneChangeReason)99)).Message);
    }

    private sealed class FixedIdentity(string externalSubject) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
            Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
    }
}
