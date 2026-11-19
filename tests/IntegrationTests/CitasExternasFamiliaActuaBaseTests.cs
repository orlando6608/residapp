using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Citas externas, PR 6, la familia actúa y correos: el esquema y las reglas de la base (script 0105; docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 11). Aquí se prueba la
/// base sola, con SQL: las cuatro tablas son de solo añadir y con secuencia obligada; la asignación de «Me encargo yo» es atómica (de dos pulsaciones a la vez gana una); quien lo tiene ahora se calcula al
/// leer y se «suelta sola» al reprogramar la cita, cambiar su modo o perder el acceso; y la regla de los avisos por correo decide a quién se escribe y por qué no. Los datos de las pruebas son inventados.</summary>
[Collection("AvisoCambioCita")]
public class CitasExternasFamiliaActuaBaseTests
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");

    private static SqlProfileScopeDirectoryProvider Scopes => new(TestDatabase.ConnectionFactory);

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Madrid).DateTime);

    private static DateOnly InDays(int days) => Today.AddDays(days);

    private static DateTime LocalUtc(DateOnly day, int hour) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Madrid);

    private static ExternalAppointmentsApplicationService Staff(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static ExternalAppointmentFamilySettingsApplicationService Settings(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    /// <summary>Una cita con transporte y acompañante a cargo de la familia, pendientes, en el nivel que se pida.</summary>
    private static async Task<Guid> RegisterAsync(
        Setup setup, int days, ExternalFamilyVisibility visibility = ExternalFamilyVisibility.Visible,
        ExternalTransportMode transport = ExternalTransportMode.Familia, ExternalCompanionMode companion = ExternalCompanionMode.Familiar)
    {
        var registered = await Staff(setup.Admin).RegisterAsync(new RegisterExternalAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId,
            new ExternalAppointment(InDays(days), new TimeOnly(10, 30), ExternalAppointmentType.Consulta, ExternalAppointmentSpecialty.Cardiologia, "Hospital Ficticio Norte",
                transport, ExternalArrangementState.Pendiente, companion, ExternalArrangementState.Pendiente, null, visibility)));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value;
    }

    private static async Task OpenFamilyAsync(Setup setup)
    {
        var result = await Settings(setup.Admin).SetCenterAsync(new SetExternalFamilyCenterCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true, true, true));
        Assert.True(result.Ok, result.Error?.Message);
    }

    private static async Task ExecuteAsync(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private static async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // Los procedimientos de avisos recorren todos los centros: con una base de pruebas grande y la máquina cargada tardan más de los 30 s por defecto.
        return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, commandTimeout: 180)))!;
    }

    private static async Task<SqlException> FailAsync(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
    }

    /// <summary>Una fila de encargo, tal como la inserta la aplicación (el actor lo fija el CHECK).</summary>
    private static Task<int> ArrangeAsync(Guid appointmentId, Setup setup, Guid linkId, string scope, int number, string action, DateOnly? date = null) =>
        InsertArrangementAsync(appointmentId, setup.Admin.CenterId.Value, linkId, scope, number, action, date ?? DateOnly.MinValue, TestDatabase.ConnectionFactory);

    private static async Task<int> InsertArrangementAsync(
        Guid appointmentId, Guid centerId, Guid linkId, string scope, int number, string action, DateOnly date, SqlConnectionFactory connections)
    {
        using var connection = await connections.OpenAsync();
        var actualDate = date == DateOnly.MinValue
            ? await connection.ExecuteScalarAsync<DateTime>("SELECT fecha FROM dbo.citas_externas WHERE id = @appointmentId", new { appointmentId })
            : date.ToDateTime(TimeOnly.MinValue);
        return await connection.ExecuteAsync("""
            INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en)
            VALUES (NEWID(), @centerId, @appointmentId, @scope, @number, @action, @linkId, CASE WHEN @action = 'LIBERADO_AUTOMATICO' THEN 'SISTEMA' ELSE 'FAMILIAR' END, @date, SYSUTCDATETIME())
            """, new { centerId, appointmentId, scope, number, action, linkId, date = actualDate });
    }

    /// <summary>Otra persona de la familia del mismo residente, con acceso al portal, correo y correo confirmado.</summary>
    private static async Task<(Guid LinkId, string Subject, Guid FamilyId)> AddSecondFamilyAsync(Setup setup, string name = "Mateo Pérez")
    {
        var linkId = await AddFamilyAsync(setup.Admin, setup.ResidentId, name);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, setup.ResidentId, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, setup.ResidentId, linkId, FamilyAuthorizationChange.Activar, 1);
        await ConfirmAddressAsync(linkId);
        return (linkId, subject, await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId", new { linkId }));
    }

    private static Task ConfirmAddressAsync(Guid linkId) => ExecuteAsync("""
        UPDATE dbo.familiares SET correo = CONCAT('familia-', CONVERT(NVARCHAR(36), id), '@ejemplo.invalid'), correo_confirmado_en = SYSUTCDATETIME(), correo_confirmado_por_cuenta_id = creado_por_cuenta_id
         WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId)
        """, new { linkId });

    private static Task MutateAppointmentAsync(Guid appointmentId, string assignments) => ExecuteAsync(
        $"UPDATE dbo.citas_externas SET {assignments}, version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @appointmentId", new { appointmentId });

    private static async Task<List<(Guid Link, string? Reason)>> NoticesAsync(Guid appointmentId, string step, DateOnly today)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return [.. (await connection.QueryAsync<(Guid Link, string? Reason)>(
            """
            CREATE TABLE #n (centro_id UNIQUEIDENTIFIER, cita_id UNIQUEIDENTIFIER, fecha DATE, tramo NVARCHAR(2) COLLATE Latin1_General_100_BIN2, vinculo_id UNIQUEIDENTIFIER, familiar_id UNIQUEIDENTIFIER,
                              correo NVARCHAR(254), conf DATETIME2(3), motivo NVARCHAR(24));
            INSERT INTO #n EXEC dbo.calcular_avisos_citas_externas @hoy = @today, @cita = @appointmentId;
            SELECT vinculo_id, motivo FROM #n WHERE tramo = @step;
            """,
            new { appointmentId, step, today = today.ToDateTime(TimeOnly.MinValue) }))];
    }

    private static async Task<List<Guid>> HoldersAsync(Guid appointmentId, DateOnly today)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return [.. await connection.QueryAsync<Guid>(
            "SELECT vinculo_id FROM dbo.fn_citas_externas_encargos_vigentes(@today) WHERE cita_id = @appointmentId ORDER BY ambito_codigo",
            new { appointmentId, today = today.ToDateTime(TimeOnly.MinValue) })];
    }

    // ---- Solo se añade y la secuencia ----

    [Fact]
    public async Task LasCuatroTablasSonDeSoloAnadir_ConsultanLaSecuenciaYNoSePuedenCambiar()
    {
        var setup = await SetupAsync("Residente Actua Inmutable");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup, 5);
        Assert.Equal(1, await ArrangeAsync(id, setup, setup.LinkId, "TRANSPORTE", 1, "ME_ENCARGO"));

        foreach (var table in new[] { "citas_externas_encargos", "citas_externas_no_puedo", "citas_externas_correo_centro", "citas_externas_correo_familiar" })
        {
            Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE", (await FailAsync($"UPDATE dbo.{table} SET cambiado_en = SYSUTCDATETIME()")).Message);
            Assert.Contains("EXTERNAL_APPOINTMENT_FAMILY_ACTION_IMMUTABLE", (await FailAsync($"DELETE FROM dbo.{table}")).Message);
        }
    }

    [Fact]
    public async Task UnEncargoSigueLaSecuencia_PrimeroTomar_NoTomarSobreOtro_SoloSueltaQuienLoTiene()
    {
        var setup = await SetupAsync("Residente Actua Secuencia");
        var second = await AddSecondFamilyAsync(setup);
        var id = await RegisterAsync(setup, 5);

        // La primera fila de un ámbito es un ME_ENCARGO.
        Assert.Contains("EXTERNAL_APPOINTMENT_ARRANGEMENT_SEQUENCE_INVALID", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'TRANSPORTE', 1, 'SOLTAR', (SELECT TOP 1 id FROM dbo.residentes_familiares WHERE residente_id = citas_externas.residente_id), 'FAMILIAR', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id",
            new { id })).Message);
        Assert.Equal(1, await ArrangeAsync(id, setup, setup.LinkId, "TRANSPORTE", 1, "ME_ENCARGO"));

        // Otra persona no puede tomar lo que ya tiene alguien (y un número repetido tampoco existe).
        Assert.Contains("EXTERNAL_APPOINTMENT_ARRANGEMENT_SEQUENCE_INVALID", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'TRANSPORTE', 2, 'ME_ENCARGO', @link, 'FAMILIAR', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id", new { id, link = second.LinkId })).Message);
        Assert.Contains("UX_cexta_numero", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'TRANSPORTE', 1, 'ME_ENCARGO', @link, 'FAMILIAR', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id", new { id, link = second.LinkId })).Message);

        // Solo suelta quien la tiene.
        Assert.Contains("EXTERNAL_APPOINTMENT_ARRANGEMENT_SEQUENCE_INVALID", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'TRANSPORTE', 2, 'SOLTAR', @link, 'FAMILIAR', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id", new { id, link = second.LinkId })).Message);
        Assert.Equal(1, await ArrangeAsync(id, setup, setup.LinkId, "TRANSPORTE", 2, "SOLTAR"));

        // Soltada, otra persona la toma; el acompañante es otro ámbito y va aparte.
        Assert.Equal(1, await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 3, "ME_ENCARGO"));
        Assert.Equal(1, await ArrangeAsync(id, setup, setup.LinkId, "ACOMPANANTE", 1, "ME_ENCARGO"));
    }

    [Fact]
    public async Task ElVinculoTieneQueSerDelResidenteDeLaCita_YElPersonalNoPuedeFirmarUnEncargo()
    {
        var setup = await SetupAsync("Residente Actua Vinculo");
        var other = await SetupAsync("Residente Actua Vinculo Otro");
        var id = await RegisterAsync(setup, 5);

        // El vínculo de otro residente (aunque sea de otro centro la FK ya lo impide; en el mismo centro lo impide el disparador).
        var sameCenterOther = await CreateResidentAsync(setup.Admin, "Residente Actua Vinculo Mismo Centro");
        var foreignLink = await AddFamilyAsync(setup.Admin, sameCenterOther, "Persona de Otro Residente");
        Assert.Contains("EXTERNAL_APPOINTMENT_LINK_INVALID", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'TRANSPORTE', 1, 'ME_ENCARGO', @link, 'FAMILIAR', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id", new { id, link = foreignLink })).Message);
        Assert.NotNull(other);

        // El perfil de quien actúa lo fija el CHECK: ni Administración ni Enfermería firman un encargo.
        Assert.Contains("CK_cexta_actor", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'TRANSPORTE', 1, 'ME_ENCARGO', @link, 'ADMINISTRACION', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id", new { id, link = setup.LinkId })).Message);
        Assert.Contains("CK_cexta_ambito", (await FailAsync(
            "INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en) " +
            "SELECT NEWID(), centro_id, id, 'OTRO', 1, 'ME_ENCARGO', @link, 'FAMILIAR', fecha, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id", new { id, link = setup.LinkId })).Message);
    }

    [Fact]
    public async Task LaAsignacionEsAtomica_DeDosPulsacionesALaVezGanaUna_YLaOtraRecibeLaViolacionUnica()
    {
        var setup = await SetupAsync("Residente Actua Concurrencia");
        var second = await AddSecondFamilyAsync(setup);

        for (var round = 0; round < 8; round++)
        {
            var id = await RegisterAsync(setup, 5 + round);
            var attempts = new[] { setup.LinkId, second.LinkId }.Select(link => Task.Run(async () =>
            {
                try
                {
                    return await ArrangeAsync(id, setup, link, "TRANSPORTE", 1, "ME_ENCARGO");
                }
                catch (SqlException ex) when (ex.Number is 2601 or 2627)
                {
                    return 0;
                }
            })).ToArray();
            var results = await Task.WhenAll(attempts);
            Assert.Equal(1, results.Sum());
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_encargos WHERE cita_id = @id", new { id }));
        }
    }

    [Fact]
    public async Task NoPuedo_SeGuardaPorFamiliarYCita_YSigueSuSecuencia()
    {
        var setup = await SetupAsync("Residente Actua NoPuedo");
        var id = await RegisterAsync(setup, 5);
        const string insert = "INSERT INTO dbo.citas_externas_no_puedo (id, centro_id, cita_id, vinculo_id, numero, no_puede, cambiado_en) " +
                              "SELECT NEWID(), centro_id, id, @link, @number, @value, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id";

        Assert.Contains("EXTERNAL_APPOINTMENT_REFUSAL_SEQUENCE_INVALID", (await FailAsync(insert, new { id, link = setup.LinkId, number = 1, value = false })).Message);
        await ExecuteAsync(insert, new { id, link = setup.LinkId, number = 1, value = true });
        Assert.Contains("EXTERNAL_APPOINTMENT_REFUSAL_SEQUENCE_INVALID", (await FailAsync(insert, new { id, link = setup.LinkId, number = 2, value = true })).Message);
        await ExecuteAsync(insert, new { id, link = setup.LinkId, number = 2, value = false });
        await ExecuteAsync(insert, new { id, link = setup.LinkId, number = 3, value = true });

        var foreignLink = await AddFamilyAsync(setup.Admin, await CreateResidentAsync(setup.Admin, "Residente Actua NoPuedo Otro"), "Otra Persona");
        Assert.Contains("EXTERNAL_APPOINTMENT_LINK_INVALID", (await FailAsync(insert, new { id, link = foreignLink, number = 1, value = true })).Message);
    }

    [Fact]
    public async Task ElCorreoDelCentro_SoloSeEnciendeConLasFamiliasEncendidas_ConConfirmacion_YSigueLaSecuencia()
    {
        var setup = await SetupAsync("Residente Actua Correo Centro");
        var center = setup.Admin.CenterId.Value;
        var account = setup.Admin.AccountId.Value;
        const string insert = "INSERT INTO dbo.citas_externas_correo_centro (id, centro_id, numero, correo_activo, confirma_informada, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en) " +
                              "VALUES (NEWID(), @center, @number, @on, @informed, @account, 'ADMINISTRACION', SYSUTCDATETIME())";

        // Con las familias apagadas no se encienden los correos; y sin la confirmación tampoco.
        Assert.Contains("EXTERNAL_APPOINTMENT_MAIL_NEEDS_FAMILY", (await FailAsync(insert, new { center, number = 1, on = true, informed = true, account })).Message);
        await OpenFamilyAsync(setup);
        Assert.Contains("CK_cextcc_confirmacion", (await FailAsync(insert, new { center, number = 1, on = true, informed = false, account })).Message);
        Assert.Contains("EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID", (await FailAsync(insert, new { center, number = 1, on = false, informed = false, account })).Message);
        await ExecuteAsync(insert, new { center, number = 1, on = true, informed = true, account });
        Assert.Contains("EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID", (await FailAsync(insert, new { center, number = 2, on = true, informed = true, account })).Message);
        await ExecuteAsync(insert, new { center, number = 2, on = false, informed = false, account });

        // El centro reservado de Plataforma no tiene correos.
        var platform = await ScalarAsync<Guid?>("SELECT id FROM dbo.centros WHERE codigo = 'PLATAFORMA'");
        if (platform is { } platformId)
        {
            Assert.Contains("EXTERNAL_APPOINTMENT_MAIL_CENTER_INVALID", (await FailAsync(insert, new { center = platformId, number = 1, on = true, informed = true, account })).Message);
        }
    }

    [Fact]
    public async Task LaBajaDeAvisosDeUnFamiliar_SigueLaSecuencia()
    {
        var setup = await SetupAsync("Residente Actua Baja Avisos");
        var familyId = await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link", new { link = setup.LinkId });
        const string insert = "INSERT INTO dbo.citas_externas_correo_familiar (id, centro_id, familiar_id, numero, sin_avisos, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en) " +
                              "VALUES (NEWID(), @center, @family, @number, @value, @account, 'ADMINISTRACION', SYSUTCDATETIME())";
        var p = (int number, bool value) => new { center = setup.Admin.CenterId.Value, family = familyId, number, value, account = setup.Admin.AccountId.Value };

        Assert.Contains("EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID", (await FailAsync(insert, p(1, false))).Message);
        await ExecuteAsync(insert, p(1, true));
        Assert.Contains("EXTERNAL_APPOINTMENT_MAIL_SEQUENCE_INVALID", (await FailAsync(insert, p(2, true))).Message);
        await ExecuteAsync(insert, p(2, false));
    }

    // ---- Quién tiene qué hoy ----

    [Fact]
    public async Task QuienLoTieneAhora_SeCalculaAlLeer_YSeSueltaSoloAlReprogramar_CambiarElModo_PerderElAccesoOCancelar()
    {
        var setup = await SetupAsync("Residente Actua Vigente");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup, 5);
        Assert.Empty(await HoldersAsync(id, Today));

        await ArrangeAsync(id, setup, setup.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        Assert.Equal([setup.LinkId], await HoldersAsync(id, Today));

        // Reprogramar la cita (otra fecha): el encargo ya no cuenta, sin escribir nada.
        await MutateAppointmentAsync(id, "fecha = DATEADD(DAY, 1, fecha)");
        Assert.Empty(await HoldersAsync(id, Today));
        await MutateAppointmentAsync(id, "fecha = DATEADD(DAY, -1, fecha)");
        Assert.Equal([setup.LinkId], await HoldersAsync(id, Today));

        // El modo deja de ser de la familia.
        await MutateAppointmentAsync(id, "transporte_modo_codigo = 'CENTRO'");
        Assert.Empty(await HoldersAsync(id, Today));
        await MutateAppointmentAsync(id, "transporte_modo_codigo = 'FAMILIA'");
        Assert.Equal([setup.LinkId], await HoldersAsync(id, Today));

        // Cancelada (y una cancelada no vuelve atrás, así que es otra cita): no hay quien se encargue.
        var cancelled = await RegisterAsync(setup, 6);
        await ArrangeAsync(cancelled, setup, setup.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        Assert.Equal([setup.LinkId], await HoldersAsync(cancelled, Today));
        await MutateAppointmentAsync(cancelled, "estado_codigo = 'CANCELADA'");
        Assert.Empty(await HoldersAsync(cancelled, Today));

        // Pierde el acceso: autorización revocada.
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Revocar, 2, "Prueba");
        Assert.Empty(await HoldersAsync(id, Today));
    }

    [Fact]
    public async Task QuienLoTieneAhora_NoCuentaConLaCuentaSuspendida_ElPerfilRevocado_NiLaInvitacionSinUsar()
    {
        var setup = await SetupAsync("Residente Actua Cuenta");
        var id = await RegisterAsync(setup, 5);
        await ArrangeAsync(id, setup, setup.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        Assert.Equal([setup.LinkId], await HoldersAsync(id, Today));

        await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        Assert.Empty(await HoldersAsync(id, Today));
        await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'ACTIVE' WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        Assert.Equal([setup.LinkId], await HoldersAsync(id, Today));

        // El perfil de familia del centro, revocado con la cuenta aún activa: la consulta del portal no lo mira, esta sí.
        await ExecuteAsync(
            "UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = concedido_por_cuenta_id WHERE perfil_codigo = 'FAMILIAR' AND cuenta_id = (SELECT id FROM dbo.cuentas WHERE sujeto_externo = @subject)",
            new { subject = setup.Subject });
        Assert.Empty(await HoldersAsync(id, Today));
    }

    [Fact]
    public async Task LaFuncionDeAcceso_CoincideConLaConsultaDelPortal_MasLoQueEstaNoMira()
    {
        var setup = await SetupAsync("Residente Actua Coincide");
        var accountId = await ScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        var today = Today.ToDateTime(TimeOnly.MinValue);

        async Task<(bool Portal, bool Function)> BothAsync()
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            var portal = await connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) {SqlFamilyPortalReader.AccessibleLinks} AND link.id = @link", new { AccountId = accountId, CenterId = setup.Admin.CenterId.Value, Today = today, link = setup.LinkId });
            var function = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.fn_familiares_con_acceso(@link, @today)", new { today, link = setup.LinkId });
            return (portal == 1, function == 1);
        }

        Assert.Equal((true, true), await BothAsync());
        await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Revocar, 2, "Prueba");
        Assert.Equal((false, false), await BothAsync());
    }

    [Fact]
    public async Task LaFuncionDeAcceso_NoCuentaLaCuentaSuspendida_QueLaConsultaDelPortalNoMira()
    {
        var setup = await SetupAsync("Residente Actua Cuenta Suspendida");
        var accountId = await ScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject = setup.Subject });
        var today = Today.ToDateTime(TimeOnly.MinValue);
        Task<int> FunctionCountAsync() => ScalarAsync<int>("SELECT COUNT(*) FROM dbo.fn_familiares_con_acceso(@link, @today)", new { today, link = setup.LinkId });

        Assert.Equal(1, await FunctionCountAsync());
        await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @accountId", new { accountId });
        Assert.Equal(0, await FunctionCountAsync());
        Assert.Equal(1, await ScalarAsync<int>(
            $"SELECT COUNT(*) {SqlFamilyPortalReader.AccessibleLinks} AND link.id = @link", new { AccountId = accountId, CenterId = setup.Admin.CenterId.Value, Today = today, link = setup.LinkId }));
    }

    // ---- A quién se escribe ----

    [Fact]
    public async Task LosAvisosDe7DiasYLaVispera_VanATodos_SiNadieTieneEncargo_YSoloAQuienLoTiene_SiAlguien()
    {
        var setup = await SetupAsync("Residente Actua Destinatarios");
        await OpenFamilyAsync(setup);
        var second = await AddSecondFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 7);

        var all = await NoticesAsync(id, "7D", Today);
        Assert.Equal(2, all.Count);
        Assert.All(all, n => Assert.Null(n.Reason));

        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        var onlyHolder = await NoticesAsync(id, "7D", Today);
        Assert.Null(onlyHolder.Single(n => n.Link == second.LinkId).Reason);
        Assert.Equal("YA_CUBIERTA", onlyHolder.Single(n => n.Link == setup.LinkId).Reason);

        // La víspera, igual, con la misma cita movida un día.
        await MutateAppointmentAsync(id, "fecha = DATEADD(DAY, -6, fecha)");
        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 2, "SOLTAR");
        Assert.All(await NoticesAsync(id, "1D", Today), n => Assert.Null(n.Reason));
    }

    [Fact]
    public async Task ElAvisoDe2Dias_SoloVaSiAunQuedaAlgoQueLaFamiliaPuedaCubrir_YNoAQuienSeEncargoODijoNoPuedo()
    {
        var setup = await SetupAsync("Residente Actua DosDias");
        await OpenFamilyAsync(setup);
        var second = await AddSecondFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 2);

        Assert.All(await NoticesAsync(id, "2D", Today), n => Assert.Null(n.Reason));

        // Dice «No puedo»: no se le escribe, a la otra persona sí.
        await ExecuteAsync(
            "INSERT INTO dbo.citas_externas_no_puedo (id, centro_id, cita_id, vinculo_id, numero, no_puede, cambiado_en) SELECT NEWID(), centro_id, id, @link, 1, 1, SYSUTCDATETIME() FROM dbo.citas_externas WHERE id = @id",
            new { id, link = setup.LinkId });
        var afterRefusal = await NoticesAsync(id, "2D", Today);
        Assert.Equal("NO_PUEDE", afterRefusal.Single(n => n.Link == setup.LinkId).Reason);
        Assert.Null(afterRefusal.Single(n => n.Link == second.LinkId).Reason);

        // Se encarga del transporte, pero queda el acompañante: sigue sin cubrir, y a quien se encargó no se le escribe.
        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        var afterTransport = await NoticesAsync(id, "2D", Today);
        Assert.Equal("YA_CUBIERTA", afterTransport.Single(n => n.Link == second.LinkId).Reason);

        // Con los dos ámbitos cubiertos por encargos, nadie recibe «sigue sin cubrir».
        await ArrangeAsync(id, setup, second.LinkId, "ACOMPANANTE", 1, "ME_ENCARGO");
        Assert.All(await NoticesAsync(id, "2D", Today), n => Assert.Equal("YA_CUBIERTA", n.Reason));
    }

    [Fact]
    public async Task LosAvisos_NoSalen_ConElCentroOLaFamiliaApagados_ElResidenteRestringido_LaCitaOcultaOLaBajaDelFamiliar()
    {
        var setup = await SetupAsync("Residente Actua Apagados");
        await ConfirmAddressAsync(setup.LinkId);
        var id = await RegisterAsync(setup, 7);
        var hidden = await RegisterAsync(setup, 7, ExternalFamilyVisibility.Oculta);

        Assert.Equal("FAMILIA_NO_VE", (await NoticesAsync(id, "7D", Today)).Single().Reason);
        await OpenFamilyAsync(setup);
        Assert.Equal("AVISOS_DESACTIVADOS", (await NoticesAsync(id, "7D", Today)).Single().Reason);
        await EnableMailAsync(setup);
        Assert.Null((await NoticesAsync(id, "7D", Today)).Single().Reason);
        Assert.Empty(await NoticesAsync(hidden, "7D", Today));

        var restrict = await Settings(setup.Admin).SetResidentAsync(new SetExternalFamilyResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, 0, true));
        Assert.True(restrict.Ok, restrict.Error?.Message);
        Assert.Equal("FAMILIA_NO_VE", (await NoticesAsync(id, "7D", Today)).Single().Reason);
        var release = await Settings(setup.Admin).SetResidentAsync(new SetExternalFamilyResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, 1, false));
        Assert.True(release.Ok, release.Error?.Message);

        var familyId = await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link", new { link = setup.LinkId });
        await ExecuteAsync(
            "INSERT INTO dbo.citas_externas_correo_familiar (id, centro_id, familiar_id, numero, sin_avisos, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en) VALUES (NEWID(), @center, @familyId, 1, 1, @account, 'ADMINISTRACION', SYSUTCDATETIME())",
            new { center = setup.Admin.CenterId.Value, familyId, account = setup.Admin.AccountId.Value });
        Assert.Equal("BAJA_DE_AVISOS", (await NoticesAsync(id, "7D", Today)).Single().Reason);
    }

    private static Task EnableMailAsync(Setup setup) => ExecuteAsync(
        "INSERT INTO dbo.citas_externas_correo_centro (id, centro_id, numero, correo_activo, confirma_informada, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en) VALUES (NEWID(), @center, 1, 1, 1, @account, 'ADMINISTRACION', SYSUTCDATETIME())",
        new { center = setup.Admin.CenterId.Value, account = setup.Admin.AccountId.Value });


    [Theory]
    [InlineData("de baja")]
    [InlineData("sin dirección confirmada")]
    public async Task SiQuienTieneElEncargoNoPuedeRecibirElCorreo_ElRestoNoQuedaSinAviso(string why)
    {
        var setup = await SetupAsync("Residente Actua Encargo Mudo " + why[..4]);
        await OpenFamilyAsync(setup);
        var second = await AddSecondFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 7);
        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");

        // Mientras quien se encargó puede recibir el correo, solo él lo recibe.
        Assert.Equal("YA_CUBIERTA", (await NoticesAsync(id, "7D", Today)).Single(n => n.Link == setup.LinkId).Reason);

        if (why == "de baja")
        {
            await ExecuteAsync(
                "INSERT INTO dbo.citas_externas_correo_familiar (id, centro_id, familiar_id, numero, sin_avisos, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en) VALUES (NEWID(), @center, @family, 1, 1, @account, 'ADMINISTRACION', SYSUTCDATETIME())",
                new { center = setup.Admin.CenterId.Value, family = second.FamilyId, account = setup.Admin.AccountId.Value });
        }
        else
        {
            await ExecuteAsync("UPDATE dbo.familiares SET correo_confirmado_en = NULL, correo_confirmado_por_cuenta_id = NULL WHERE id = @family", new { family = second.FamilyId });
        }

        // Si no puede, el otro no queda sin aviso (el portal dice «lo hará un familiar» pero nadie lo sabría).
        Assert.Null((await NoticesAsync(id, "7D", Today)).Single(n => n.Link == setup.LinkId).Reason);
    }

    [Fact]
    public async Task UnEncargoYaConfirmadoPorElPersonal_NoCuentaComoOfrecido_NiSilenciaALosDemas()
    {
        var setup = await SetupAsync("Residente Actua Confirmado");
        await OpenFamilyAsync(setup);
        var second = await AddSecondFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 7);
        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        Assert.Equal([second.LinkId], await HoldersAsync(id, Today));

        await MutateAppointmentAsync(id, "transporte_estado_codigo = 'CONFIRMADO'");

        Assert.Empty(await HoldersAsync(id, Today));
        Assert.All(await NoticesAsync(id, "7D", Today), n => Assert.Null(n.Reason));
    }

    [Theory]
    [InlineData(ExternalTransportMode.Centro, ExternalCompanionMode.PersonalCentro)]
    [InlineData(ExternalTransportMode.Ambulancia, ExternalCompanionMode.LoLlevaTransporte)]
    [InlineData(ExternalTransportMode.SinDeterminar, ExternalCompanionMode.SinDeterminar)]
    public async Task ElAvisoDe2Dias_NoSale_SiLoQueFaltaNoEsDeLaFamilia(ExternalTransportMode transport, ExternalCompanionMode companion)
    {
        var setup = await SetupAsync("Residente Actua DosDias Ajeno " + transport);
        await OpenFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 2, transport: transport, companion: companion);

        Assert.All(await NoticesAsync(id, "2D", Today), n => Assert.Equal("YA_CUBIERTA", n.Reason));

        // En cambio, el de la víspera sale en toda cita visible, también sin nada que cubra la familia.
        var eve = await RegisterAsync(setup, 1, transport: transport, companion: companion);
        Assert.All(await NoticesAsync(eve, "1D", Today), n => Assert.Null(n.Reason));
    }

    [Fact]
    public async Task SiLaCitaSeMueveDespuesDeReservar_ElAvisoViejoNoSale_YLaFechaNuevaTieneElSuyo()
    {
        var setup = await SetupAsync("Residente Actua Mover");
        await OpenFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 7);
        await ReserveAsync(LocalUtc(Today, 11));
        var old = await ScalarAsync<Guid>("SELECT id FROM dbo.envios_correo WHERE referencia_id = @id", new { id });

        await MutateAppointmentAsync(id, "fecha = DATEADD(DAY, -6, fecha)");

        Assert.Equal("CITA_NO_VIGENTE", await ScalarAsync<string?>("EXEC dbo.comprobar_aviso_cita_externa_en @envio_id = @old, @ahora_utc = @at", new { old, at = LocalUtc(Today, 11) }));
        await ReserveAsync(LocalUtc(Today, 12));
        var keys = (await KeysAsync(id)).ToList();
        Assert.Equal(2, keys.Count);
        Assert.Contains($"1D|{Today.AddDays(1):yyyy-MM-dd}", keys);
        Assert.Contains($"7D|{Today.AddDays(7):yyyy-MM-dd}", keys);
    }

    private static async Task<IEnumerable<string>> KeysAsync(Guid id)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QueryAsync<string>("SELECT referencia_clave FROM dbo.envios_correo WHERE referencia_id = @id", new { id });
    }

    // ---- Reservar y comprobar ----

    private static async Task<int> ReserveAsync(DateTime utc) =>
        await ScalarAsync<int>("EXEC dbo.reservar_avisos_citas_externas_en @ahora_utc = @utc, @idioma = 'es', @version = 'v1'", new { utc });

    [Fact]
    public async Task ReservarAvisos_EscribeUnaFilaPorPersona_SoloEnLaFranja_YNoDuplicaAlRepetir()
    {
        var setup = await SetupAsync("Residente Actua Reserva");
        await OpenFamilyAsync(setup);
        var second = await AddSecondFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 7);

        // Fuera de la franja (a las 09:00 y a las 19:00) no se reserva nada.
        Assert.Equal(0, await ReserveAsync(LocalUtc(Today, 9)));
        Assert.Equal(0, await ReserveAsync(LocalUtc(Today, 19)));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.envios_correo WHERE referencia_id = @id", new { id }));

        // Una fila por persona (el índice incluye al familiar: sin eso, solo se reservaría la primera).
        Assert.True(await ReserveAsync(LocalUtc(Today, 11)) >= 2);
        var rows = (await ScalarRows("SELECT plantilla_codigo, referencia_clave, estado_codigo, familiar_id FROM dbo.envios_correo WHERE referencia_id = @id", id)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("AVISO_CITA_EXTERNA", r.Template));
        Assert.All(rows, r => Assert.Equal($"7D|{InDays(7):yyyy-MM-dd}", r.Key));
        Assert.All(rows, r => Assert.Equal("PENDIENTE", r.State));
        Assert.Equal(2, rows.Select(r => r.FamilyId).Distinct().Count());
        Assert.NotEqual(Guid.Empty, second.FamilyId);

        // Repetir la vuelta no duplica.
        await ReserveAsync(LocalUtc(Today, 12));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.envios_correo WHERE referencia_id = @id", new { id }));
    }

    private static async Task<IEnumerable<(string Template, string Key, string State, Guid FamilyId)>> ScalarRows(string sql, Guid id)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QueryAsync<(string Template, string Key, string State, Guid FamilyId)>(sql, new { id });
    }

    [Fact]
    public async Task ReservarAvisos_NoReservaPara_LaCitaOculta_SinCorreoConfirmado_NiLaQueNoEsDeHoy7_1_2()
    {
        var setup = await SetupAsync("Residente Actua Reserva Filtros");
        await OpenFamilyAsync(setup);
        await EnableMailAsync(setup);
        var hidden = await RegisterAsync(setup, 7, ExternalFamilyVisibility.Oculta);
        var far = await RegisterAsync(setup, 8);
        var visible = await RegisterAsync(setup, 1);

        // Sin correo confirmado no hay a quién reservar.
        await ReserveAsync(LocalUtc(Today, 11));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.envios_correo WHERE referencia_id IN (@hidden, @far, @visible)", new { hidden, far, visible }));

        await ConfirmAddressAsync(setup.LinkId);
        await ReserveAsync(LocalUtc(Today, 11));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.envios_correo WHERE referencia_id IN (@hidden, @far)", new { hidden, far }));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.envios_correo WHERE referencia_id = @visible", new { visible }));
    }

    [Fact]
    public async Task ComprobarAviso_DaNuloSiSale_YElMotivoSiAlgoCambioEntreReservarYEnviar()
    {
        var setup = await SetupAsync("Residente Actua Comprobar");
        await OpenFamilyAsync(setup);
        await ConfirmAddressAsync(setup.LinkId);
        await EnableMailAsync(setup);
        var id = await RegisterAsync(setup, 7);
        await ReserveAsync(LocalUtc(Today, 11));
        var notice = await ScalarAsync<Guid>("SELECT id FROM dbo.envios_correo WHERE referencia_id = @id", new { id });
        var at = LocalUtc(Today, 11);

        async Task<string?> CheckAsync() => await ScalarAsync<string?>("EXEC dbo.comprobar_aviso_cita_externa_en @envio_id = @notice, @ahora_utc = @at", new { notice, at });

        Assert.Null(await CheckAsync());

        // Alguien más se encarga: ya no hace falta decírselo a esta persona.
        var second = await AddSecondFamilyAsync(setup);
        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 1, "ME_ENCARGO");
        Assert.Equal("YA_CUBIERTA", await CheckAsync());
        await ArrangeAsync(id, setup, second.LinkId, "TRANSPORTE", 2, "SOLTAR");
        Assert.Null(await CheckAsync());

        // La cita se mueve, se oculta o se cancela; y a otro día ya no corresponde el tramo.
        await MutateAppointmentAsync(id, "visibilidad_familia_codigo = 'OCULTA'");
        Assert.Equal("FAMILIA_NO_VE", await CheckAsync());
        await MutateAppointmentAsync(id, "visibilidad_familia_codigo = 'VISIBLE'");
        Assert.Null(await CheckAsync());
        Assert.Equal("FUERA_DE_PLAZO", await ScalarAsync<string?>("EXEC dbo.comprobar_aviso_cita_externa_en @envio_id = @notice, @ahora_utc = @at", new { notice, at = LocalUtc(Today.AddDays(1), 11) }));

        // El residente se restringe.
        var restrict = await Settings(setup.Admin).SetResidentAsync(new SetExternalFamilyResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, 0, true));
        Assert.True(restrict.Ok, restrict.Error?.Message);
        Assert.Equal("FAMILIA_NO_VE", await CheckAsync());
        var release = await Settings(setup.Admin).SetResidentAsync(new SetExternalFamilyResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, 1, false));
        Assert.True(release.Ok, release.Error?.Message);
        Assert.Null(await CheckAsync());

        // La cita se cancela (y no vuelve atrás).
        await MutateAppointmentAsync(id, "estado_codigo = 'CANCELADA'");
        Assert.Equal("CITA_NO_VIGENTE", await CheckAsync());
    }

    [Fact]
    public async Task LosEnviosNuevos_AdmitenLasDosPlantillasYLosMotivosNuevos_YElIndiceIncluyeAlFamiliar()
    {
        var setup = await SetupAsync("Residente Actua Envios");
        await OpenFamilyAsync(setup);
        var second = await AddSecondFamilyAsync(setup);
        var id = await RegisterAsync(setup, 7);
        var familyId = await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link", new { link = setup.LinkId });

        foreach (var (template, family) in new[] { ("AVISO_CITA_EXTERNA", familyId), ("AVISO_CITA_EXTERNA", second.FamilyId), ("AVISO_CITA_EXTERNA_SIN_CUBRIR", familyId) })
        {
            await ExecuteAsync("""
                INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en)
                VALUES (NEWID(), @center, @template, 'v1', 'es', @family, @id, '7D|2030-01-01', 'PENDIENTE', 0, SYSUTCDATETIME())
                """, new { center = setup.Admin.CenterId.Value, template, family, id });
        }

        // El mismo aviso a la misma persona sí es un duplicado.
        var duplicate = await FailAsync("""
            INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en)
            VALUES (NEWID(), @center, 'AVISO_CITA_EXTERNA', 'v1', 'es', @family, @id, '7D|2030-01-01', 'PENDIENTE', 0, SYSUTCDATETIME())
            """, new { center = setup.Admin.CenterId.Value, family = familyId, id });
        Assert.Contains("UX_envios_correo_referencia", duplicate.Message);

        // Cada motivo nuevo lo admite el CHECK: una fila pendiente por motivo.
        var reasons = new[] { "FAMILIA_NO_VE", "AVISOS_DESACTIVADOS", "BAJA_DE_AVISOS", "YA_CUBIERTA", "NO_PUEDE" };
        for (var i = 0; i < reasons.Length; i++)
        {
            var row = Guid.NewGuid();
            await ExecuteAsync("""
                INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en)
                VALUES (@row, @center, 'AVISO_CITA_EXTERNA', 'v1', 'es', @family, @id, @key, 'PENDIENTE', 0, SYSUTCDATETIME())
                """, new { row, center = setup.Admin.CenterId.Value, family = familyId, id, key = $"1D|2030-02-0{i + 1}" });
            await ExecuteAsync("UPDATE dbo.envios_correo SET estado_codigo = 'OMITIDO', motivo_codigo = @reason, resuelto_en = SYSUTCDATETIME() WHERE id = @row", new { reason = reasons[i], row });
            Assert.Equal(reasons[i], await ScalarAsync<string>("SELECT motivo_codigo FROM dbo.envios_correo WHERE id = @row", new { row }));
        }
    }

    [Fact]
    public async Task CerrarAvisosSinResolver_IncluyeLasPlantillasNuevas()
    {
        var setup = await SetupAsync("Residente Actua Cierre Envios");
        var id = await RegisterAsync(setup, 7);
        var familyId = await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link", new { link = setup.LinkId });
        const string insert = """
            INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en)
            VALUES (@notice, @center, @template, 'v1', 'es', @family, @id, @key, 'PENDIENTE', @attempts, DATEADD(HOUR, -26, SYSUTCDATETIME()))
            """;
        var unclaimed = Guid.NewGuid();
        var claimed = Guid.NewGuid();
        await ExecuteAsync(insert, new { notice = unclaimed, center = setup.Admin.CenterId.Value, template = "AVISO_CITA_EXTERNA", family = familyId, id, key = "7D|2030-01-01", attempts = 0 });
        await ExecuteAsync(insert, new { notice = claimed, center = setup.Admin.CenterId.Value, template = "AVISO_CITA_EXTERNA_SIN_CUBRIR", family = familyId, id, key = "2D|2030-01-01", attempts = 1 });

        await ExecuteAsync("EXEC dbo.cerrar_envios_correo_sin_resolver");

        Assert.Equal(("OMITIDO", "FUERA_DE_PLAZO"), await ScalarTupleAsync(unclaimed));
        Assert.Equal(("FALLIDO", "RESULTADO_DESCONOCIDO"), await ScalarTupleAsync(claimed));
    }

    private static async Task<(string State, string? Reason)> ScalarTupleAsync(Guid notice)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QuerySingleAsync<(string, string?)>("SELECT estado_codigo, motivo_codigo FROM dbo.envios_correo WHERE id = @notice", new { notice });
    }
}

file sealed class FixedFamilySessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
