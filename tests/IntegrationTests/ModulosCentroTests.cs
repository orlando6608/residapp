using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Modules;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Módulos opcionales por centro (ADR 0015, script 0098): activar, renovar y apagar solo lo hace Plataforma, añadiendo filas y dejando su auditoría sin datos en la misma transacción;
/// el estado (activo, solo lectura, no disponible) se calcula al consultar con la vigencia, el interruptor global del proveedor y el ajuste de desarrollo. Cada prueba crea su operador y su centro.</summary>
public class ModulosCentroTests
{
    private sealed record Operator(string ExternalSubject, AccountId AccountId, Guid ProfileScopeId);

    private sealed class Settings(bool activateAll = false, bool thirdPartyConfigured = true) : IModuleSettings
    {
        public bool ActivateAll => activateAll;

        public bool IsThirdPartyConfigured(string moduleCode) => thirdPartyConfigured;
    }

    private static readonly DateOnly Today = CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow);

    private static async Task<Operator> CreateOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var subject = $"test-plat-{Guid.NewGuid():N}"[..28];
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de módulos', 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
            """, new { accountId, subject, profileScopeId, centerId = PlatformCenter.Id });
        return new Operator(subject, AccountId.From(accountId), profileScopeId);
    }

    private static PlatformApplicationService BuildPlatform(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedSessionIdentityProvider(subject),
        new SqlPlatformCenterDirectory(TestDatabase.ConnectionFactory), new SqlPlatformCenterRepository(TestDatabase.ConnectionFactory),
        new SqlAccessInvitationRepository(TestDatabase.ConnectionFactory), new SqlCenterModuleRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static CenterModuleAccess BuildAccess(string subject, IModuleSettings? settings = null) => new(
        new SqlCenterModuleRepository(TestDatabase.ConnectionFactory), settings ?? new Settings(), new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory),
        new FixedSessionIdentityProvider(subject), TimeProvider.System);

    private static ChangeCenterModuleCommand Enable(
        Operator op, CenterId center, DateOnly? from = null, DateOnly? until = null, string module = CenterModules.DictadoVoz, string reference = "SOL-2026-001") =>
        new(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), center, module, true, from ?? Today.AddDays(-1), until ?? Today.AddDays(30), "ANX-2026-A", "1", true, null,
            DateTimeOffset.UtcNow.AddHours(-2), ModuleRequestChannel.Correo, reference);

    private static ChangeCenterModuleCommand Disable(Operator op, CenterId center, DateTimeOffset? receivedAt = null) =>
        new(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), center, CenterModules.DictadoVoz, false, null, null, null, null, false, ModuleDisableReason.PeticionCentro,
            receivedAt ?? DateTimeOffset.UtcNow.AddHours(-1), ModuleRequestChannel.Telefono, "SOL-2026-002");

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task Activar_y_apagar_dejan_una_fila_nueva_cada_vez_con_la_solicitud_y_la_auditoria_sin_datos_y_el_estado_cambia_en_la_peticion_siguiente()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        var access = BuildAccess(admin.ExternalSubject);
        Assert.Equal(ModuleState.NotAvailable, await access.StateAsync(admin.CenterId, CenterModules.DictadoVoz));

        var enabled = await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId));
        Assert.True(enabled.Ok, enabled.Error?.Message);
        Assert.Equal(ModuleState.Active, await access.StateAsync(admin.CenterId, CenterModules.DictadoVoz));

        var disabled = await service.ChangeCenterModuleAsync(Disable(op, admin.CenterId));
        Assert.True(disabled.Ok, disabled.Error?.Message);
        // Sin caché: el apagado se nota en la consulta siguiente de la misma instancia. El dictado no guarda datos propios, así que apagado es «no disponible», no «solo lectura».
        Assert.Equal(ModuleState.NotAvailable, await access.StateAsync(admin.CenterId, CenterModules.DictadoVoz));

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var rows = (await connection.QueryAsync<(int Number, string State, string? Sheet, string? Conditions, string? Reason, string Channel, string Reference, Guid Actor, string Profile)>("""
            SELECT numero, estado_codigo, version_ficha, condiciones_codigos, motivo_baja_codigo, canal_codigo, referencia_solicitud, cambiado_por_cuenta_id, cambiado_por_perfil
              FROM dbo.modulos_centro WHERE centro_id = @center ORDER BY numero
            """, new { center = admin.CenterId.Value })).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal((1, "ACTIVE", "1", string.Join(',', CenterModules.All[0].PrivacyConditions), null, "CORREO", "SOL-2026-001", op.AccountId.Value, "PLATAFORMA"), rows[0]);
        Assert.Equal((2, "DISABLED", null, null, "PETICION_CENTRO", "TELEFONO", "SOL-2026-002", op.AccountId.Value, "PLATAFORMA"), rows[1]);

        foreach (var action in new[] { "CENTER_MODULE_ENABLE", "CENTER_MODULE_DISABLE" })
        {
            Assert.Equal(1, await CountAsync(
                "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo = @action AND perfil_activo = 'PLATAFORMA' AND cuenta_id = @actor AND tipo_recurso = 'CENTER_MODULE'",
                new { center = admin.CenterId.Value, action, actor = op.AccountId.Value }));
        }

        // La Administración del centro ve sus cambios en su auditoría, con el nombre del módulo y sin datos de residentes.
        var page = (await BuildEstructura(admin.ExternalSubject).ListAuditAsync(new ListAdministrativeAuditQuery(
            admin.ProfileScopeId, admin.CenterId, Today.AddDays(-1), Today.AddDays(1), null, null))).Value!;
        var enabledEntry = Assert.Single(page.Entries, e => e.Action == "CENTER_MODULE_ENABLE");
        Assert.Equal("Dictado por voz", enabledEntry.AffectedName);
        Assert.Equal(SystemProfile.Plataforma, enabledEntry.ActorProfile);
        Assert.Single(page.Entries, e => e.Action == "CENTER_MODULE_DISABLE");
    }

    [Fact]
    public async Task Renovar_es_una_fila_nueva_y_el_vencimiento_apaga_sin_tarea_programada()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        var access = BuildAccess(admin.ExternalSubject);

        // La aplicación ya no admite una alta que nace vencida (la tabla es inmutable); un módulo vencido es uno cuya vigencia acabó con el tiempo, así que se inserta como db_owner.
        var born = await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId, Today.AddDays(-30), Today.AddDays(-1), reference: "SOL-OLD"));
        Assert.Equal(ApplicationFailureCode.InvalidInput, born.Error?.Code);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.modulos_centro
                    (id, centro_id, modulo_codigo, numero, estado_codigo, vigente_desde, vigente_hasta, referencia_anexo, version_anexo, version_ficha, condiciones_codigos,
                     solicitud_recibida_en, canal_codigo, referencia_solicitud, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (NEWID(), @center, 'DICTADO_VOZ', 1, 'ACTIVE', @from, @until, 'ANX-OLD', '1', '1', 'A', SYSUTCDATETIME(), 'CORREO', 'SOL-OLD', @actor, 'PLATAFORMA', SYSUTCDATETIME())
                """, new { center = admin.CenterId.Value, from = Today.AddDays(-30).ToDateTime(TimeOnly.MinValue), until = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), actor = op.AccountId.Value });
        }

        Assert.Equal(ModuleState.NotAvailable, await access.StateAsync(admin.CenterId, CenterModules.DictadoVoz));

        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId, Today, Today, reference: "SOL-RENEW"))).Ok);
        Assert.Equal(ModuleState.Active, await access.StateAsync(admin.CenterId, CenterModules.DictadoVoz));
        Assert.Equal(2, await CountAsync("SELECT MAX(numero) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));

        // Una renovación no puede empezar en el futuro: como manda el último cambio, apagaría hoy un módulo que aún está vigente.
        var future = await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId, Today.AddDays(5), Today.AddDays(40), reference: "SOL-FUTURE"));
        Assert.Equal(ApplicationFailureCode.InvalidInput, future.Error?.Code);
        Assert.Equal(ModuleState.Active, await access.StateAsync(admin.CenterId, CenterModules.DictadoVoz));
    }

    [Fact]
    public async Task Se_guarda_todo_lo_que_prueba_la_instruccion_del_centro_y_el_plazo_de_apagado()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        var received = DateTimeOffset.UtcNow.AddHours(-5);
        var from = Today.AddDays(-2);
        var until = Today.AddDays(90);

        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId, from, until) with { SolicitudRecibidaEn = received })).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(DateTime Received, DateTime From, DateTime Until, string Annex, string AnnexVersion, DateTime ChangedAt)>("""
            SELECT solicitud_recibida_en, vigente_desde, vigente_hasta, referencia_anexo, version_anexo, cambiado_en FROM dbo.modulos_centro WHERE centro_id = @center
            """, new { center = admin.CenterId.Value });
        Assert.InRange((row.Received - received.UtcDateTime).TotalMilliseconds, -2, 2);
        Assert.Equal((from, until), (DateOnly.FromDateTime(row.From), DateOnly.FromDateTime(row.Until)));
        Assert.Equal(("ANX-2026-A", "1"), (row.Annex, row.AnnexVersion));
        Assert.True(row.ChangedAt > row.Received);
    }

    [Fact]
    public async Task Apagar_se_puede_aunque_el_centro_este_inactivo_pero_activar_no_y_cada_centro_cambia_el_suyo()
    {
        var op = await CreateOperatorAsync();
        var a = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var b = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, a.CenterId))).Ok);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, b.CenterId))).Ok);

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.centros SET estado = 'INACTIVE' WHERE id = @center", new { center = a.CenterId.Value });
        }

        // Cada hora de retraso en apagar es tratar contra una instrucción del centro: un centro inactivo también se apaga. Activar, no.
        Assert.True((await service.ChangeCenterModuleAsync(Disable(op, a.CenterId))).Ok);
        var enableInactive = await service.ChangeCenterModuleAsync(Enable(op, a.CenterId, reference: "SOL-OTRA"));
        Assert.Equal(ApplicationFailureCode.InvalidInput, enableInactive.Error?.Code);

        // El otro centro sigue activo y sin auditoría nueva.
        Assert.Equal(ModuleState.Active, await BuildAccess(b.ExternalSubject).StateAsync(b.CenterId, CenterModules.DictadoVoz));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND tipo_recurso = 'CENTER_MODULE'", new { center = b.CenterId.Value }));
    }

    [Fact]
    public async Task Dos_apagados_a_la_vez_dejan_una_baja_y_un_conflicto_y_dos_altas_a_la_vez_no_dejan_huecos()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Ok);

        var disables = await Task.WhenAll(
            service.ChangeCenterModuleAsync(Disable(op, admin.CenterId)), BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Disable(op, admin.CenterId)));
        Assert.Equal(1, disables.Count(r => r.Ok));
        Assert.Equal(ApplicationFailureCode.Conflict, disables.Single(r => !r.Ok).Error!.Code);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo = 'CENTER_MODULE_DISABLE'", new { center = admin.CenterId.Value }));

        var enables = await Task.WhenAll(
            service.ChangeCenterModuleAsync(Enable(op, admin.CenterId, reference: "SOL-A")), BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId, reference: "SOL-B")));
        Assert.All(enables, r => Assert.True(r.Ok, r.Error?.Message));
        Assert.Equal(4, await CountAsync("SELECT MAX(numero) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
        Assert.Equal(4, await CountAsync("SELECT COUNT(DISTINCT numero) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Un_operador_sin_ambito_vigente_cuenta_activa_o_sesion_no_cambia_nada()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        // Sin sesión y con la sesión de otra persona.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await BuildPlatform("nadie-" + Guid.NewGuid().ToString("N")).ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Error?.Code);

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = cuenta_id WHERE id = @id", new { id = op.ProfileScopeId });
        }

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Error?.Code);
        var suspended = await CreateOperatorAsync();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @id", new { id = suspended.AccountId.Value });
        }

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await BuildPlatform(suspended.ExternalSubject).ChangeCenterModuleAsync(Enable(suspended, admin.CenterId))).Error?.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task El_ajuste_de_desarrollo_manda_sobre_una_baja_y_ModuleSettings_usa_la_clave_real_de_Mistral()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Ok);
        Assert.True((await service.ChangeCenterModuleAsync(Disable(op, admin.CenterId))).Ok);
        Assert.Equal(ModuleState.Active, await BuildAccess(admin.ExternalSubject, new Settings(activateAll: true)).StateAsync(admin.CenterId, CenterModules.DictadoVoz));

        var withKey = new ResidApp.Infrastructure.IA.IaOptions { Mistral = new ResidApp.Infrastructure.IA.MistralOptions { ApiKey = "clave-de-prueba" } };
        var withoutKey = new ResidApp.Infrastructure.IA.IaOptions();
        var options = Microsoft.Extensions.Options.Options.Create(new ResidApp.Infrastructure.Modules.ModuleOptions { ActivarTodos = true });
        var enabled = new ResidApp.Infrastructure.Modules.ModuleSettings(options, Microsoft.Extensions.Options.Options.Create(withKey));
        var disabled = new ResidApp.Infrastructure.Modules.ModuleSettings(options, Microsoft.Extensions.Options.Options.Create(withoutKey));
        Assert.True(enabled.ActivateAll);
        Assert.True(enabled.IsThirdPartyConfigured(CenterModules.DictadoVoz));
        Assert.False(disabled.IsThirdPartyConfigured(CenterModules.DictadoVoz));
        Assert.False(enabled.IsThirdPartyConfigured("NO_EXISTE"));
    }

    [Fact]
    public async Task Sin_la_clave_global_del_proveedor_un_modulo_con_tercero_no_funciona_aunque_la_fila_diga_activo_y_el_ajuste_de_desarrollo_no_la_salta()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Ok);

        Assert.Equal(ModuleState.NotAvailable, await BuildAccess(admin.ExternalSubject, new Settings(thirdPartyConfigured: false)).StateAsync(admin.CenterId, CenterModules.DictadoVoz));
        Assert.Equal(ModuleState.NotAvailable, await BuildAccess(admin.ExternalSubject, new Settings(activateAll: true, thirdPartyConfigured: false)).StateAsync(admin.CenterId, CenterModules.DictadoVoz));

        // Con todo activo (desarrollo) basta la clave, sin filas.
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.Equal(ModuleState.Active, await BuildAccess(other.ExternalSubject, new Settings(activateAll: true)).StateAsync(other.CenterId, CenterModules.DictadoVoz));
        Assert.Equal(ModuleState.NotAvailable, await BuildAccess(other.ExternalSubject).StateAsync(other.CenterId, CenterModules.DictadoVoz));
    }

    [Fact]
    public async Task El_estado_para_un_ambito_sale_del_centro_de_la_cuenta_y_un_ambito_ajeno_no_tiene_nada()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Ok);

        Assert.Equal(ModuleState.Active, await BuildAccess(admin.ExternalSubject).StateForScopeAsync(admin.ProfileScopeId, CenterModules.DictadoVoz));
        // Un ámbito de otro centro (aunque la cookie diga lo que quiera) no ve el módulo del primero, y un ámbito que no es de la cuenta de la sesión, nada.
        Assert.Equal(ModuleState.NotAvailable, await BuildAccess(other.ExternalSubject).StateForScopeAsync(other.ProfileScopeId, CenterModules.DictadoVoz));
        Assert.Equal(ModuleState.NotAvailable, await BuildAccess(other.ExternalSubject).StateForScopeAsync(admin.ProfileScopeId, CenterModules.DictadoVoz));
    }

    [Fact]
    public async Task Los_cambios_de_modulo_se_rechazan_si_no_son_de_Plataforma_el_centro_es_el_reservado_o_no_existe_los_datos_no_valen_o_la_secuencia_no_cuadra()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);

        // Quien no es Plataforma, nada.
        var notPlatform = await BuildPlatform(admin.ExternalSubject).ChangeCenterModuleAsync(
            Enable(op, admin.CenterId) with { AmbitoPerfilId = admin.ProfileScopeId, CentroId = admin.CenterId });
        Assert.Equal(ApplicationFailureCode.AccessDenied, notPlatform.Error!.Code);

        // Centro reservado, centro inexistente, módulo fuera del catálogo, referencias con nombres, solicitud en el futuro, sin condiciones o sin motivo de baja.
        var invalid = new[]
        {
            Enable(op, CenterId.From(PlatformCenter.Id)),
            Enable(op, CenterId.From(Guid.NewGuid())),
            Enable(op, admin.CenterId, module: "OTRO_MODULO"),
            Enable(op, admin.CenterId, reference: "Ana Pérez"),
            Enable(op, admin.CenterId) with { SolicitudRecibidaEn = DateTimeOffset.UtcNow.AddHours(1) },
            Enable(op, admin.CenterId) with { CondicionesComprobadas = false },
            Enable(op, admin.CenterId, Today, Today.AddDays(-1)),
            Enable(op, admin.CenterId) with { VigenteHasta = null },
            Disable(op, admin.CenterId) with { MotivoBaja = null },
        };
        foreach (var command in invalid)
        {
            var result = await service.ChangeCenterModuleAsync(command);
            Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error?.Code);
        }

        // Apagar lo que nunca se activó, o ya está apagado, es un conflicto; y no queda ninguna fila ni auditoría.
        var neverEnabled = await service.ChangeCenterModuleAsync(Disable(op, admin.CenterId));
        Assert.Equal(ApplicationFailureCode.Conflict, neverEnabled.Error?.Code);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Ok);
        Assert.True((await service.ChangeCenterModuleAsync(Disable(op, admin.CenterId))).Ok);
        var twice = await service.ChangeCenterModuleAsync(Disable(op, admin.CenterId));
        Assert.Equal(ApplicationFailureCode.Conflict, twice.Error?.Code);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND tipo_recurso = 'CENTER_MODULE'", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Una_referencia_de_solicitud_no_se_repite_para_el_mismo_centro_y_modulo_pero_si_en_otro_centro()
    {
        var op = await CreateOperatorAsync();
        var a = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var b = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);

        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, a.CenterId, reference: "SOL-DUP"))).Ok);
        var again = await service.ChangeCenterModuleAsync(Enable(op, a.CenterId, reference: "SOL-DUP"));
        var asDisable = await service.ChangeCenterModuleAsync(Disable(op, a.CenterId) with { ReferenciaSolicitud = "SOL-DUP" });

        Assert.Equal(ApplicationFailureCode.Conflict, again.Error?.Code);
        Assert.Contains("referencia de solicitud ya se usó", again.Error!.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, asDisable.Error?.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = a.CenterId.Value }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND tipo_recurso = 'CENTER_MODULE'", new { center = a.CenterId.Value }));
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, b.CenterId, reference: "SOL-DUP"))).Ok);
    }

    [Fact]
    public async Task Plataforma_lee_el_historial_sin_la_cuenta_y_los_vencimientos_de_todos_los_centros_y_otro_perfil_no()
    {
        var op = await CreateOperatorAsync();
        var a = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildPlatform(op.ExternalSubject);
        var platformCenter = CenterId.From(PlatformCenter.Id);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, a.CenterId, until: Today.AddDays(5), reference: "SOL-H-1"))).Ok);
        Assert.True((await service.ChangeCenterModuleAsync(Disable(op, a.CenterId))).Ok);
        Assert.True((await service.ChangeCenterModuleAsync(Enable(op, a.CenterId, until: Today.AddDays(8), reference: "SOL-H-2"))).Ok);

        var modules = (await service.ListCenterModulesAsync(new PlatformCenterQuery(op.ProfileScopeId, platformCenter, a.CenterId))).Value!;
        var expiring = (await service.ListExpiringModulesAsync(new PlatformQuery(op.ProfileScopeId, platformCenter))).Value!;
        var denied = await BuildPlatform(a.ExternalSubject).ListCenterModulesAsync(new PlatformCenterQuery(a.ProfileScopeId, a.CenterId, a.CenterId));

        var dictado = Assert.Single(modules);
        Assert.Equal(ModuleState.Active, dictado.State);
        Assert.Equal([3, 2, 1], dictado.History.Select(h => h.Number));
        Assert.Equal(["SOL-H-2", "SOL-2026-002", "SOL-H-1"], dictado.History.Select(h => h.RequestReference));
        Assert.Equal(Today.AddDays(8), dictado.Last!.ValidUntil);
        Assert.Contains(expiring, e => e.CenterId == a.CenterId && e.ValidUntil == Today.AddDays(8));
        Assert.DoesNotContain(expiring, e => e.CenterId == a.CenterId && e.ValidUntil == Today.AddDays(5));
        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error?.Code);
    }

    private static async Task InsertActiveRowAsync(CenterId center, Guid actor, int number, DateOnly until, string reference)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.modulos_centro
                (id, centro_id, modulo_codigo, numero, estado_codigo, vigente_desde, vigente_hasta, referencia_anexo, version_anexo, version_ficha, condiciones_codigos,
                 solicitud_recibida_en, canal_codigo, referencia_solicitud, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, 'DICTADO_VOZ', @number, 'ACTIVE', DATEADD(DAY, -200, @until), @until, 'ANX-DIRECTO', '1', '1', 'A', SYSUTCDATETIME(), 'CORREO', @reference, @actor, 'PLATAFORMA', SYSUTCDATETIME())
            """, new { center = center.Value, number, until = until.ToDateTime(TimeOnly.MinValue), reference, actor });
    }

    [Fact]
    public async Task Los_vencimientos_incluyen_los_limites_de_30_y_90_dias_y_excluyen_lo_de_fuera_las_bajas_y_los_centros_inactivos()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op.ExternalSubject);
        var platformCenter = CenterId.From(PlatformCenter.Id);
        var cases = new Dictionary<string, (CenterId Center, int Offset)>();
        foreach (var offset in new[] { 30, 31, -90, -91, 0 })
        {
            var profile = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
            await InsertActiveRowAsync(profile.CenterId, op.AccountId.Value, 1, Today.AddDays(offset), "SOL-LIM");
            cases[$"{offset}"] = (profile.CenterId, offset);
        }

        // Un módulo cuyo último cambio es una baja no aparece aunque una alta anterior esté en la ventana; ni uno de un centro inactivo.
        var disabled = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await InsertActiveRowAsync(disabled.CenterId, op.AccountId.Value, 1, Today.AddDays(5), "SOL-BAJA-1");
        Assert.True((await service.ChangeCenterModuleAsync(Disable(op, disabled.CenterId))).Ok);
        var inactive = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await InsertActiveRowAsync(inactive.CenterId, op.AccountId.Value, 1, Today.AddDays(5), "SOL-INACT");
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.centros SET estado = 'INACTIVE' WHERE id = @id", new { id = inactive.CenterId.Value });
        }

        var listed = (await service.ListExpiringModulesAsync(new PlatformQuery(op.ProfileScopeId, platformCenter))).Value!.Select(e => e.CenterId).ToHashSet();

        foreach (var offset in new[] { 30, -90, 0 })
        {
            Assert.Contains(cases[$"{offset}"].Center, listed);
        }

        foreach (var offset in new[] { 31, -91 })
        {
            Assert.DoesNotContain(cases[$"{offset}"].Center, listed);
        }

        Assert.DoesNotContain(disabled.CenterId, listed);
        Assert.DoesNotContain(inactive.CenterId, listed);
    }

    [Fact]
    public async Task El_indice_unico_de_la_referencia_rechaza_un_duplicado_insertado_sin_pasar_por_la_aplicacion()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await InsertActiveRowAsync(admin.CenterId, op.AccountId.Value, 1, Today.AddDays(30), "SOL-IDX");

        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => InsertActiveRowAsync(admin.CenterId, op.AccountId.Value, 2, Today.AddDays(60), "SOL-IDX"));

        Assert.Contains("UX_mc_referencia", error.Message);
    }

    [Fact]
    public async Task Dos_altas_simultaneas_con_la_misma_referencia_dejan_una_fila_y_un_conflicto()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var results = await Task.WhenAll(
            BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId, reference: "SOL-RACE")),
            BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId, reference: "SOL-RACE")));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(ApplicationFailureCode.Conflict, results.Single(r => !r.Ok).Error!.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.modulos_centro WHERE centro_id = @center", new { center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Activar_en_un_centro_inactivo_o_inexistente_da_un_mensaje_que_lo_dice()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.centros SET estado = 'INACTIVE' WHERE id = @id", new { id = admin.CenterId.Value });
        }

        var inactive = await BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId));
        var missing = await BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, CenterId.From(Guid.NewGuid())));

        Assert.Equal(ApplicationFailureCode.InvalidInput, inactive.Error?.Code);
        Assert.Contains("no existe o no está activo", inactive.Error!.Message);
        Assert.Contains("no existe o no está activo", missing.Error!.Message);
    }

    [Fact]
    public async Task Exigir_el_modulo_en_el_caso_de_uso_distingue_no_disponible_activo_y_solo_lectura()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var access = BuildAccess(admin.ExternalSubject);

        var notAvailable = await Assert.ThrowsAsync<DomainStateException>(() => access.RequireWritableAsync(admin.CenterId, CenterModules.DictadoVoz));
        Assert.Equal(CenterModuleAccess.NotAvailable, notAvailable.Message);
        Assert.True((await BuildPlatform(op.ExternalSubject).ChangeCenterModuleAsync(Enable(op, admin.CenterId))).Ok);
        await access.RequireWritableAsync(admin.CenterId, CenterModules.DictadoVoz);
        await access.RequireReadableAsync(admin.CenterId, CenterModules.DictadoVoz);
        Assert.Throws<ArgumentException>(() => access.StateAsync(admin.CenterId, "NO_EXISTE").GetAwaiter().GetResult());
    }
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
