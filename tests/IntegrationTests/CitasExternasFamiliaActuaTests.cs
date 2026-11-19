using Dapper;
using ResidApp.Application.Errors;
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

/// <summary>Citas externas, PR 6, la familia actúa (script 0105; docs/tareas/alta-prioridad/pendientes-citas-externas.md, apartado 11): «Me encargo yo», «Ya no puedo», «No puedo» y deshacerlo, por los casos
/// de uso del Portal Familiar. La asignación es atómica; quien lo tiene se ve como «tú» u «otra persona» sin nombre; el personal con permiso ve el nombre y solo el recuento de «No puedo»; todo lo que la
/// familia no puede ver es el mismo «acceso denegado»; y la auditoría lleva solo identificadores. Los datos de las pruebas son inventados.</summary>
public class CitasExternasFamiliaActuaTests
{
    private static SqlProfileScopeDirectoryProvider Scopes => new(TestDatabase.ConnectionFactory);

    private static ExternalAppointmentsApplicationService Staff(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static ExternalAppointmentFamilySettingsApplicationService Settings(SeededProfile by) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(by.ExternalSubject), new SqlExternalAppointmentRepository(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static FamilyExternalAppointmentsApplicationService Family(string subject) =>
        new(Scopes, new FixedFamilySessionIdentityProvider(subject), new SqlFamilyExternalAppointments(TestDatabase.ConnectionFactory), TimeProvider.System);

    private static DateOnly InDays(int days) => ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow).AddDays(days);

    private static async Task<Guid> RegisterAsync(
        Setup setup, int days = 5, ExternalFamilyVisibility visibility = ExternalFamilyVisibility.Visible,
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

    private static async Task<(Guid LinkId, string Subject)> AddSecondAsync(Setup setup, string name = "Mateo Pérez")
    {
        var linkId = await AddFamilyAsync(setup.Admin, setup.ResidentId, name);
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        var grant = await Build(setup.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, linkId, subject));
        Assert.True(grant.Ok, grant.Error?.Message);
        await ChangeAsync(setup.Admin, setup.ResidentId, linkId, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(setup.Admin, setup.ResidentId, linkId, FamilyAuthorizationChange.Activar, 1);
        return (linkId, subject);
    }

    private static async Task<FamilyArrangementOutcome> ActAsync(
        string subject, Guid linkId, Guid appointmentId, FamilyArrangementAction action, ExternalArrangementScope scope = ExternalArrangementScope.Transporte)
    {
        var result = await Family(subject).ArrangeAsync(await QueryAsync(subject), linkId, appointmentId, scope, action);
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value;
    }

    private static async Task<ExternalAppointmentForFamily> SeenAsync(string subject, Guid linkId, Guid appointmentId)
    {
        var found = await Family(subject).FindAsync(await QueryAsync(subject), linkId, appointmentId);
        Assert.True(found.Ok, found.Error?.Message);
        return found.Value.Appointment;
    }

    private static async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static Task ExecuteAsync(string sql, object? parameters = null) => ScalarAsync<int?>(sql + "; SELECT 0", parameters);

    // ---- Tomar, soltar, rechazar ----

    [Fact]
    public async Task MeEncargoYo_ElQueLoToma_LoVeComoTuYLaOtraPersonaComoOtra_SinNombre()
    {
        var setup = await SetupAsync("Residente Actua Toma");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup);
        var id = await RegisterAsync(setup);

        var before = await SeenAsync(setup.Subject, setup.LinkId, id);
        Assert.True(before.CanTake(ExternalArrangementScope.Transporte));
        Assert.True(before.CanTake(ExternalArrangementScope.Acompanante));
        Assert.True(before.CanRefuse);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        var mine = await SeenAsync(setup.Subject, setup.LinkId, id);
        var theirs = await SeenAsync(second.Subject, second.LinkId, id);
        Assert.Equal(ExternalFamilyHolder.You, mine.TransportHolder);
        Assert.Equal(ExternalFamilyHolder.Another, theirs.TransportHolder);
        Assert.Equal(ExternalFamilyHolder.Nobody, theirs.CompanionHolder);
        Assert.True(mine.CanRelease(ExternalArrangementScope.Transporte));
        Assert.False(theirs.CanTake(ExternalArrangementScope.Transporte));
        Assert.False(mine.Covered);
        Assert.False(theirs.CoveredByFamily);

        // Repetir la pulsación (doble clic) no cambia nada ni da error.
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_encargos WHERE cita_id = @id", new { id }));

        // La otra persona se queda con el acompañante; la cita queda cubierta para la familia, aunque el personal no haya confirmado nada.
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Take, ExternalArrangementScope.Acompanante));
        Assert.True((await SeenAsync(setup.Subject, setup.LinkId, id)).CoveredByFamily);
        Assert.False((await SeenAsync(setup.Subject, setup.LinkId, id)).Covered);
    }

    [Fact]
    public async Task YaLoTieneOtraPersona_ElSegundoRecibeElAvisoYNoPisaAlPrimero()
    {
        var setup = await SetupAsync("Residente Actua Segundo");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup);
        var id = await RegisterAsync(setup);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.TakenByAnother, await ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(ExternalFamilyHolder.You, (await SeenAsync(setup.Subject, setup.LinkId, id)).TransportHolder);
    }

    [Fact]
    public async Task DosPulsacionesALaVez_GanaUna_YLaOtraRecibeElAviso()
    {
        var setup = await SetupAsync("Residente Actua Carrera");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup);

        for (var round = 0; round < 6; round++)
        {
            var id = await RegisterAsync(setup, 5 + round);
            var results = await Task.WhenAll(
                Task.Run(() => ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take)),
                Task.Run(() => ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Take)));
            Assert.Equal(1, results.Count(r => r == FamilyArrangementOutcome.Done));
            Assert.Equal(1, results.Count(r => r == FamilyArrangementOutcome.TakenByAnother));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_encargos WHERE cita_id = @id AND accion_codigo = 'ME_ENCARGO'", new { id }));
        }
    }

    [Fact]
    public async Task YaNoPuedo_SueltaLoPropio_NoLoAjeno_YLaOtraPersonaYaPuedeTomarlo()
    {
        var setup = await SetupAsync("Residente Actua Soltar");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup);
        var id = await RegisterAsync(setup);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Release));
        Assert.Equal(ExternalFamilyHolder.You, (await SeenAsync(setup.Subject, setup.LinkId, id)).TransportHolder);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release));
        Assert.Equal(ExternalFamilyHolder.Nobody, (await SeenAsync(second.Subject, second.LinkId, id)).TransportHolder);
        // Soltar lo que ya no se tiene no da error.
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(ExternalFamilyHolder.Another, (await SeenAsync(setup.Subject, setup.LinkId, id)).TransportHolder);
    }

    [Fact]
    public async Task NoPuedo_SeGuardaYSeDeshace_NoLoDiceQuienSeHaEncargado_YElTomarLoQuita()
    {
        var setup = await SetupAsync("Residente Actua NoPuedo Servicio");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Refuse));
        Assert.True((await SeenAsync(setup.Subject, setup.LinkId, id)).Refused);
        Assert.False((await SeenAsync(setup.Subject, setup.LinkId, id)).CanRefuse);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Refuse));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_no_puedo WHERE cita_id = @id", new { id }));

        // Tomar algo lo quita de «No puedo».
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.False((await SeenAsync(setup.Subject, setup.LinkId, id)).Refused);

        // Quien se ha encargado de algo no dice «No puedo»: suelta.
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Refuse));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Refuse));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.UndoRefuse));
        Assert.False((await SeenAsync(setup.Subject, setup.LinkId, id)).Refused);
    }

    // ---- Lo que la familia no puede hacer ----

    [Fact]
    public async Task LaFamiliaNoPuedeActuar_ConElCentroApagado_ElResidenteRestringido_LaCitaOculta_OUnaCitaAjena_SiempreElMismoAccesoDenegado()
    {
        var setup = await SetupAsync("Residente Actua Denegado");
        var id = await RegisterAsync(setup);
        var hidden = await RegisterAsync(setup, 6, ExternalFamilyVisibility.Oculta);
        var query = await QueryAsync(setup.Subject);

        // Centro apagado.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).ArrangeAsync(query, setup.LinkId, id, ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Error!.Code);
        await OpenFamilyAsync(setup);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));

        // Oculta, inexistente, y de otra familia.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).ArrangeAsync(query, setup.LinkId, hidden, ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).ArrangeAsync(query, setup.LinkId, Guid.NewGuid(), ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).ArrangeAsync(query, Guid.NewGuid(), id, ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Error!.Code);
        var other = await SetupAsync("Residente Actua Denegado Otro");
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(other.Subject).ArrangeAsync(await QueryAsync(other.Subject), setup.LinkId, id, ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Error!.Code);

        // El residente restringido: ni ve ni actúa.
        var restricted = await Settings(setup.Admin).SetResidentAsync(new SetExternalFamilyResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, 0, true));
        Assert.True(restricted.Ok, restricted.Error?.Message);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).ArrangeAsync(query, setup.LinkId, id, ExternalArrangementScope.Transporte, FamilyArrangementAction.Release)).Error!.Code);
    }

    [Fact]
    public async Task SoloSePuedeActuarSobreLoQueEsDeLaFamiliaYSigueSinResolver_UnaCitaCanceladaNoAdmiteNada()
    {
        var setup = await SetupAsync("Residente Actua Aplicable");
        await OpenFamilyAsync(setup);
        var byCenter = await RegisterAsync(setup, 5, transport: ExternalTransportMode.Centro, companion: ExternalCompanionMode.PersonalCentro);
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, byCenter, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, byCenter, FamilyArrangementAction.Take, ExternalArrangementScope.Acompanante));

        var id = await RegisterAsync(setup, 6);
        // El personal deja confirmado el transporte: ya no hay nada que ofrecer ahí.
        await ExecuteAsync("UPDATE dbo.citas_externas SET transporte_estado_codigo = 'CONFIRMADO', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take, ExternalArrangementScope.Acompanante));

        // Cancelada: se sigue viendo («Cancelada») pero no se puede actuar.
        await ExecuteAsync("UPDATE dbo.citas_externas SET estado_codigo = 'CANCELADA', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release, ExternalArrangementScope.Acompanante));
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Refuse));
    }

    [Fact]
    public async Task UnaCitaReprogramadaOQuienPierdeElAcceso_YaNoCuenta_YOtraPersonaPuedeTomarla()
    {
        var setup = await SetupAsync("Residente Actua Reprogramar");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup);
        var id = await RegisterAsync(setup);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        await ExecuteAsync("UPDATE dbo.citas_externas SET fecha = DATEADD(DAY, 1, fecha), version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });
        Assert.Equal(ExternalFamilyHolder.Nobody, (await SeenAsync(second.Subject, second.LinkId, id)).TransportHolder);
        // A quien se había encargado la pantalla le dice que ya no cuenta (texto fijo), y puede volver a ofrecerse.
        Assert.True((await SeenAsync(setup.Subject, setup.LinkId, id)).LapsedOf(ExternalArrangementScope.Transporte));
        Assert.False((await SeenAsync(second.Subject, second.LinkId, id)).LapsedOf(ExternalArrangementScope.Transporte));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(ExternalFamilyHolder.Another, (await SeenAsync(setup.Subject, setup.LinkId, id)).TransportHolder);

        // La que lo tenía pierde el acceso: la tarea queda libre, y otra persona la puede tomar sin que nadie escriba nada a mano.
        var other = await RegisterAsync(setup, 7);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(second.Subject, second.LinkId, other, FamilyArrangementAction.Take));
        await ChangeAsync(setup.Admin, setup.ResidentId, second.LinkId, FamilyAuthorizationChange.Revocar, 2, "Prueba");
        Assert.Equal(ExternalFamilyHolder.Nobody, (await SeenAsync(setup.Subject, setup.LinkId, other)).TransportHolder);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, other, FamilyArrangementAction.Take));
        Assert.Equal(ExternalFamilyHolder.You, (await SeenAsync(setup.Subject, setup.LinkId, other)).TransportHolder);
    }

    // ---- Lo que ve el personal ----

    [Fact]
    public async Task ElPersonalVeQuienSeEncargo_SoloConPermiso_YDeLosNoPuedoSoloElRecuento()
    {
        var setup = await SetupAsync("Residente Actua Personal");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup, "Mateo Pérez");
        var id = await RegisterAsync(setup);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Refuse));

        var admin = await Settings(setup.Admin).ReadOffersAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, id);
        Assert.True(admin.Ok, admin.Error?.Message);
        Assert.True(admin.Value!.NamesVisible);
        Assert.Equal("Lucía Pérez", admin.Value.Offers.Single().Name);
        Assert.Equal(ExternalArrangementScope.Transporte, admin.Value.Offers.Single().Scope);
        Assert.Equal(1, admin.Value.RefusalCount);

        // Enfermería sin el permiso de citas: ve que hay un ofrecimiento, no quién (decisión 7). Con el permiso, sí.
        var nurse = await Settings(setup.Nurse).ReadOffersAsync(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId, id);
        Assert.True(nurse.Ok, nurse.Error?.Message);
        Assert.False(nurse.Value!.NamesVisible);
        Assert.Null(nurse.Value.Offers.Single().Name);
        Assert.Equal(1, nurse.Value.RefusalCount);
        await ExecuteAsync(
            "INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id) VALUES (NEWID(), @scope, @center, 'EXTERNAL_APPOINTMENT_MANAGE', SYSUTCDATETIME(), @by)",
            new { scope = setup.Nurse.ProfileScopeId, center = setup.Nurse.CenterId.Value, by = setup.Admin.AccountId.Value });
        var withPermission = await Settings(setup.Nurse).ReadOffersAsync(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId, id);
        Assert.Equal("Lucía Pérez", withPermission.Value!.Offers.Single().Name);
    }

    [Fact]
    public async Task LaLista_DeAlgoOfrecidoSinConfirmar_SoloMarcaLasCitasQueLasTienen()
    {
        var setup = await SetupAsync("Residente Actua Lista Ofrecida");
        await OpenFamilyAsync(setup);
        var offered = await RegisterAsync(setup, 5);
        var plain = await RegisterAsync(setup, 6);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, offered, FamilyArrangementAction.Take));

        var marked = await Settings(setup.Admin).ListOfferedAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, [offered, plain]);
        Assert.True(marked.Ok, marked.Error?.Message);
        Assert.Equal([offered], marked.Value!.ToArray());

        // Cuando el personal lo confirma, ya no es «sin confirmar».
        await ExecuteAsync("UPDATE dbo.citas_externas SET transporte_estado_codigo = 'CONFIRMADO', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @offered", new { offered });
        Assert.Empty((await Settings(setup.Admin).ListOfferedAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, [offered, plain])).Value!);
    }

    [Fact]
    public async Task ElPersonalDeOtroCentro_NoVeLoOfrecidoDeUnaCitaAjena()
    {
        var setup = await SetupAsync("Residente Actua Otro Centro");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        var other = await SetupAsync("Residente Actua Otro Centro Otro");

        var seen = await Settings(other.Admin).ReadOffersAsync(other.Admin.ProfileScopeId, other.Admin.CenterId, id);
        Assert.Equal(ApplicationFailureCode.AccessDenied, seen.Error!.Code);
        Assert.Empty((await Settings(other.Admin).ListOfferedAsync(other.Admin.ProfileScopeId, other.Admin.CenterId, [id])).Value!);
    }


    [Fact]
    public async Task ConLaParteConfirmadaPorElPersonal_LaFichaNoLaMuestraComoOfrecida()
    {
        var setup = await SetupAsync("Residente Actua Ficha Confirmada");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Single((await Settings(setup.Admin).ReadOffersAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, id)).Value!.Offers);

        await ExecuteAsync("UPDATE dbo.citas_externas SET transporte_estado_codigo = 'CONFIRMADO', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });

        Assert.Empty((await Settings(setup.Admin).ReadOffersAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, id)).Value!.Offers);
    }

    [Fact]
    public async Task SinAccesoVigente_NoSePuedeActuar_RevocadaLaAutorizacionOSuspendidaLaCuenta()
    {
        var revoked = await SetupAsync("Residente Actua Sin Acceso Revocada");
        await OpenFamilyAsync(revoked);
        var id = await RegisterAsync(revoked);
        await ChangeAsync(revoked.Admin, revoked.ResidentId, revoked.LinkId, FamilyAuthorizationChange.Revocar, 2, "Prueba");
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(revoked.Subject).ArrangeAsync(await QueryAsync(revoked.Subject), revoked.LinkId, id, ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Error!.Code);

        var suspended = await SetupAsync("Residente Actua Sin Acceso Cuenta");
        await OpenFamilyAsync(suspended);
        var other = await RegisterAsync(suspended);
        var query = await QueryAsync(suspended.Subject);
        await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE sujeto_externo = @subject", new { subject = suspended.Subject });
        Assert.False((await Family(suspended.Subject).ArrangeAsync(query, suspended.LinkId, other, ExternalArrangementScope.Transporte, FamilyArrangementAction.Take)).Ok);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_encargos WHERE cita_id IN (@id, @other)", new { id, other }));
    }

    [Fact]
    public async Task ElMismoFamiliarPulsandoDosVecesALaVez_NoDejaMasDeUnaFila()
    {
        var setup = await SetupAsync("Residente Actua Doble Pulsacion");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);

        var results = await Task.WhenAll(
            Task.Run(() => ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take)),
            Task.Run(() => ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take)));

        Assert.All(results, r => Assert.True(r is FamilyArrangementOutcome.Done or FamilyArrangementOutcome.TakenByAnother));
        Assert.Contains(FamilyArrangementOutcome.Done, results);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_encargos WHERE cita_id = @id AND accion_codigo = 'ME_ENCARGO'", new { id }));
        Assert.Equal(ExternalFamilyHolder.You, (await SeenAsync(setup.Subject, setup.LinkId, id)).TransportHolder);
    }

    // ---- Auditoría ----

    [Fact]
    public async Task LaAuditoria_LlevaSoloIdentificadores_YElPerfilFamiliar()
    {
        var setup = await SetupAsync("Residente Actua Auditoria");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);

        await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take);
        await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release);
        await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Refuse);
        await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.UndoRefuse);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var events = (await connection.QueryAsync<(string Action, string Profile, string Resource, Guid ResidentId)>(
            "SELECT accion_codigo, perfil_activo, tipo_recurso, residente_id FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo LIKE 'EXT\\_APPOINTMENT\\_ARRANGE%' ESCAPE '\\' ORDER BY ocurrido_en",
            new { center = setup.Admin.CenterId.Value })).ToList();
        Assert.Equal(
            ["EXT_APPOINTMENT_ARRANGE_TAKE", "EXT_APPOINTMENT_ARRANGE_RELEASE", "EXT_APPOINTMENT_ARRANGE_REFUSE", "EXT_APPOINTMENT_ARRANGE_UNDO"], events.Select(e => e.Action).ToArray());
        Assert.All(events, e =>
        {
            Assert.Equal("FAMILIAR", e.Profile);
            Assert.Equal("EXTERNAL_APPOINTMENT_ARRANGEMENT", e.Resource);
            Assert.Equal(setup.ResidentId.Value, e.ResidentId);
        });

        // Una acción que no cambia nada (repetir lo ya hecho) no deja un evento más.
        await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.UndoRefuse);
        Assert.Equal(4, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo LIKE 'EXT\\_APPOINTMENT\\_ARRANGE%' ESCAPE '\\'", new { center = setup.Admin.CenterId.Value }));
    }

    // ---- Límites: el residente, los 90 días, la cita confirmada por el hospital y las carreras ----

    [Fact]
    public async Task ConElResidenteSuspendidoODeBaja_LaFamiliaNoPuedeActuar_YAlVolverSiPuede()
    {
        var setup = await SetupAsync("Residente Actua Suspendido");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);

        async Task AssertDeniedAsync()
        {
            foreach (var action in new[] { FamilyArrangementAction.Take, FamilyArrangementAction.Release, FamilyArrangementAction.Refuse, FamilyArrangementAction.UndoRefuse })
            {
                var result = await Family(setup.Subject).ArrangeAsync(await QueryAsync(setup.Subject), setup.LinkId, id, ExternalArrangementScope.Transporte, action);
                Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
            }
        }

        // Suspensión (ingreso en un hospital): la cita sigue viva y el encargo se conserva, pero la familia no puede tocar nada mientras dure.
        Assert.True((await status.SuspendAsync(new SuspendResidentCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), null))).Ok);
        await AssertDeniedAsync();
        Assert.True((await status.ResumeAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId)).Ok);
        Assert.Equal(ExternalFamilyHolder.You, (await SeenAsync(setup.Subject, setup.LinkId, id)).TransportHolder);
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release));

        // Baja: tampoco; y al reactivarlo, la cita que se canceló con la baja no admite nada (no se restaura).
        Assert.True((await status.DischargeAsync(new DischargeResidentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), ResidApp.Domain.Residents.ResidentDischargeReason.AltaVoluntaria, null))).Ok);
        await AssertDeniedAsync();
        Assert.True((await status.ReactivateAsync(new ReactivateResidentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), setup.Admin.UnitId, null, null))).Ok);
        Assert.Equal(FamilyArrangementOutcome.NotApplicable, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
    }

    [Fact]
    public async Task LaFamiliaActuaHastaLosNoventaDias_YMasAllaEsElMismoAccesoDenegado()
    {
        var setup = await SetupAsync("Residente Actua Noventa");
        await OpenFamilyAsync(setup);
        var at90 = await RegisterAsync(setup, 90);
        var at91 = await RegisterAsync(setup, 91);

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, at90, FamilyArrangementAction.Take));
        Assert.Equal(ExternalFamilyHolder.You, (await SeenAsync(setup.Subject, setup.LinkId, at90)).TransportHolder);

        foreach (var action in new[] { FamilyArrangementAction.Take, FamilyArrangementAction.Refuse })
        {
            var result = await Family(setup.Subject).ArrangeAsync(await QueryAsync(setup.Subject), setup.LinkId, at91, ExternalArrangementScope.Transporte, action);
            Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        }

        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Family(setup.Subject).FindAsync(await QueryAsync(setup.Subject), setup.LinkId, at91)).Error!.Code);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.citas_externas_encargos WHERE cita_id = @at91", new { at91 }));
    }

    [Fact]
    public async Task UnaCitaConfirmadaPorElHospital_SigueAdmitiendoLoQueHaceLaFamilia()
    {
        var setup = await SetupAsync("Residente Actua Hospital");
        await OpenFamilyAsync(setup);
        var id = await RegisterAsync(setup);
        await ExecuteAsync("UPDATE dbo.citas_externas SET estado_codigo = 'CONFIRMADA_HOSPITAL', version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id", new { id });

        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take, ExternalArrangementScope.Acompanante));
        Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release));
        var seen = await SeenAsync(setup.Subject, setup.LinkId, id);
        Assert.Equal(ExternalFamilyHolder.Nobody, seen.TransportHolder);
        Assert.Equal(ExternalFamilyHolder.You, seen.CompanionHolder);
    }

    [Fact]
    public async Task QuienTomaMientrasOtraPersonaSuelta_NoDejaDosDuenos_YLaSueltaSiempreSale()
    {
        var setup = await SetupAsync("Residente Actua Toma Y Suelta");
        await OpenFamilyAsync(setup);
        var second = await AddSecondAsync(setup);

        for (var round = 0; round < 6; round++)
        {
            var id = await RegisterAsync(setup, 5 + round);
            Assert.Equal(FamilyArrangementOutcome.Done, await ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take));
            var results = await Task.WhenAll(
                Task.Run(() => ActAsync(second.Subject, second.LinkId, id, FamilyArrangementAction.Take)),
                Task.Run(() => ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Release)));

            Assert.Equal(FamilyArrangementOutcome.Done, results[1]);
            Assert.Contains(results[0], new[] { FamilyArrangementOutcome.Done, FamilyArrangementOutcome.TakenByAnother });
            var first = await SeenAsync(setup.Subject, setup.LinkId, id);
            var other = await SeenAsync(second.Subject, second.LinkId, id);
            if (results[0] == FamilyArrangementOutcome.Done)
            {
                Assert.Equal((ExternalFamilyHolder.Another, ExternalFamilyHolder.You), (first.TransportHolder, other.TransportHolder));
            }
            else
            {
                Assert.Equal((ExternalFamilyHolder.Nobody, ExternalFamilyHolder.Nobody), (first.TransportHolder, other.TransportHolder));
            }
        }
    }

    [Fact]
    public async Task QuienTomaMientrasElPersonalConfirma_LaParteQuedaConfirmada_YNadieFiguraComoOfrecido()
    {
        var setup = await SetupAsync("Residente Actua Toma Y Confirma");
        await OpenFamilyAsync(setup);

        for (var round = 0; round < 6; round++)
        {
            var id = await RegisterAsync(setup, 5 + round);
            var results = await Task.WhenAll(
                Task.Run(() => ActAsync(setup.Subject, setup.LinkId, id, FamilyArrangementAction.Take)),
                Task.Run(async () =>
                {
                    await ExecuteAsync(
                        "UPDATE dbo.citas_externas SET transporte_estado_codigo = @confirmed, version = version + 1, modificada_en = SYSUTCDATETIME() WHERE id = @id",
                        new { id, confirmed = ExternalArrangementState.Confirmado.ToCode() });
                    return FamilyArrangementOutcome.Done;
                }));

            Assert.Contains(results[0], new[] { FamilyArrangementOutcome.Done, FamilyArrangementOutcome.NotApplicable });
            var seen = await SeenAsync(setup.Subject, setup.LinkId, id);
            Assert.Equal(ExternalFamilyHolder.Nobody, seen.TransportHolder);
            Assert.False(seen.CanTake(ExternalArrangementScope.Transporte));
            var offers = (await Settings(setup.Admin).ReadOffersAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, id)).Value!;
            Assert.DoesNotContain(offers.Offers, o => o.Scope == ExternalArrangementScope.Transporte);
        }
    }

    // ---- La invitación de alta ----

    [Fact]
    public async Task ConUnaInvitacionDeAltaSinUsarOAnulada_NoHayAcceso_YAlUsarla_SiHay()
    {
        var setup = await SetupAsync("Residente Actua Invitacion");
        var second = await AddSecondAsync(setup);
        var today = InDays(0).ToDateTime(TimeOnly.MinValue);

        async Task<int> AccessAsync(Guid link) => await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.fn_familiares_con_acceso(@link, @today)", new { link, today });
        async Task<Guid> InviteAsync(string subject)
        {
            var account = await ScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject });
            var invitation = Guid.NewGuid();
            await ExecuteAsync("""
                INSERT INTO dbo.invitaciones_acceso (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor, caduca_en)
                VALUES (@invitation, @account, @center, 'ALTA', CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONVERT(VARCHAR(36), NEWID())), 2), SYSUTCDATETIME(), @by, 'ADMINISTRACION', DATEADD(HOUR, 72, SYSUTCDATETIME()))
                """, new { invitation, account, center = setup.Admin.CenterId.Value, by = setup.Admin.AccountId.Value });
            return invitation;
        }

        Assert.Equal((1, 1), (await AccessAsync(setup.LinkId), await AccessAsync(second.LinkId)));

        // Una invitación de alta viva: la persona aún no ha llegado a entrar.
        var open = await InviteAsync(setup.Subject);
        Assert.Equal((0, 1), (await AccessAsync(setup.LinkId), await AccessAsync(second.LinkId)));
        // Anulada sin usar: tampoco entró.
        await ExecuteAsync("UPDATE dbo.invitaciones_acceso SET anulada_en = SYSUTCDATETIME() WHERE id = @open", new { open });
        Assert.Equal(0, await AccessAsync(setup.LinkId));
        // Usada: la activación se completó.
        var used = await InviteAsync(setup.Subject);
        await ExecuteAsync("UPDATE dbo.invitaciones_acceso SET usada_en = SYSUTCDATETIME() WHERE id = @used", new { used });
        Assert.Equal(1, await AccessAsync(setup.LinkId));
    }

    // ---- La baja y el alta de los avisos, en la auditoría ----

    [Fact]
    public async Task LaBajaYElAltaDeLosAvisosDeUnaPersona_QuedanEnLaAuditoria_SinNombreNiDireccion()
    {
        var setup = await SetupAsync("Residente Actua Auditoria Baja");
        var familyId = await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link", new { link = setup.LinkId });
        var admin = Settings(setup.Admin);
        var change = new SetExternalFamilyMemberMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, familyId, 0, true);

        Assert.True((await admin.SetFamilyMemberMailAsync(change)).Ok);
        Assert.True((await admin.SetFamilyMemberMailAsync(change with { VersionVista = 1, DarDeBaja = false })).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var events = (await connection.QueryAsync<(string Action, string Profile, Guid? ResidentId)>(
            "SELECT accion_codigo, perfil_activo, residente_id FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo IN ('EXT_APPOINTMENT_MAIL_OPT_OUT', 'EXT_APPOINTMENT_MAIL_OPT_IN')",
            new { center = setup.Admin.CenterId.Value })).ToList();
        Assert.Equal(["EXT_APPOINTMENT_MAIL_OPT_IN", "EXT_APPOINTMENT_MAIL_OPT_OUT"], events.Select(e => e.Action).Order().ToArray());
        Assert.All(events, e => Assert.Equal(("ADMINISTRACION", setup.ResidentId.Value), (e.Profile, e.ResidentId)));

        var everything = string.Concat(await connection.QueryAsync<string>(
            "SELECT (SELECT * FROM dbo.eventos_auditoria e WHERE e.id = audit.id FOR JSON PATH) FROM dbo.eventos_auditoria audit WHERE audit.centro_id = @center AND audit.accion_codigo LIKE 'EXT\\_APPOINTMENT\\_MAIL\\_OPT%' ESCAPE '\\'",
            new { center = setup.Admin.CenterId.Value }));
        Assert.NotEmpty(everything);
        foreach (var secret in new[] { "Lucía", "Pérez", "@", "600123456" })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    // ---- Los avisos por correo del centro y la baja ----

    [Fact]
    public async Task ElCorreoDelCentro_LoEnciendeSoloAdministracion_ConConfirmacion_ConLasFamiliasEncendidas_YConVersion()
    {
        var setup = await SetupAsync("Residente Actua Correo Servicio");
        var admin = Settings(setup.Admin);

        var state = (await admin.ReadMailAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!;
        Assert.False(state.Enabled);
        Assert.False(state.FamilyEnabled);
        Assert.Equal(0, state.Number);

        // Con las familias apagadas, no se enciende el correo; sin la confirmación, tampoco.
        var needsFamily = (await admin.SetMailAsync(new SetExternalMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true))).Error!;
        Assert.Equal(ApplicationFailureCode.Conflict, needsFamily.Code);
        Assert.Contains("Primero abre las citas externas", needsFamily.Message);
        await OpenFamilyAsync(setup);
        Assert.False((await admin.SetMailAsync(new SetExternalMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, false))).Ok);

        Assert.Equal(1, (await admin.SetMailAsync(new SetExternalMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, true, true))).Value);
        Assert.True((await admin.ReadMailAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!.Enabled);
        // Otra persona cambió antes (número visto viejo): no pisa.
        Assert.False((await admin.SetMailAsync(new SetExternalMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 0, false, false))).Ok);
        Assert.Equal(2, (await admin.SetMailAsync(new SetExternalMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, 1, false, false))).Value);

        // Enfermería y Medicina no lo ven ni lo cambian.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(setup.Nurse).ReadMailAsync(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId)).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(setup.Nurse).SetMailAsync(new SetExternalMailCommand(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId, 2, true, true))).Error!.Code);
        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @center AND accion_codigo IN ('EXT_APPOINTMENT_MAIL_ON', 'EXT_APPOINTMENT_MAIL_OFF')", new { center = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task LaBajaDeAvisosDeUnaPersona_LaHaceSoloAdministracion_SoloDeSuFamilia_YSinVerLaDireccion()
    {
        var setup = await SetupAsync("Residente Actua Baja Servicio");
        var second = await AddSecondAsync(setup);
        var familyId = await ScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @link", new { link = setup.LinkId });
        var admin = Settings(setup.Admin);

        var members = (await admin.ListFamilyMembersAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId)).Value!;
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.False(m.OptedOut));

        var change = new SetExternalFamilyMemberMailCommand(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, familyId, 0, true);
        Assert.Equal(1, (await admin.SetFamilyMemberMailAsync(change)).Value);
        Assert.True((await admin.ListFamilyMembersAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId)).Value!.Single(m => m.FamilyId == familyId).OptedOut);
        Assert.Equal(ApplicationFailureCode.Conflict, (await admin.SetFamilyMemberMailAsync(change)).Error!.Code); // número viejo
        Assert.Equal(2, (await admin.SetFamilyMemberMailAsync(change with { VersionVista = 1, DarDeBaja = false })).Value);

        // Una persona que no es de la familia de ese residente no se da de baja por su identificador suelto.
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await admin.SetFamilyMemberMailAsync(change with { FamiliarId = Guid.NewGuid() })).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Settings(setup.Nurse).ListFamilyMembersAsync(setup.Nurse.ProfileScopeId, setup.Nurse.CenterId, setup.ResidentId)).Error!.Code);
        Assert.NotEqual(Guid.Empty, second.LinkId);
    }
}

file sealed class FixedFamilySessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
