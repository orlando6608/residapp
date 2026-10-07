using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Baja, reactivación y suspensión por ingreso hospitalario (script 0041, CJ 2026-10-07). Cada prueba crea su propio centro.</summary>
public class EstadoResidenteTests
{
    private static ResidentStatusApplicationService Build(string externalSubject)
    {
        var session = new FixedStatusSessionIdentityProvider(externalSubject);
        return new ResidentStatusApplicationService(
            new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), session,
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), session),
            new SqlResidentStatusRepository(TestDatabase.ConnectionFactory), new SqlResidentStatusDirectory(TestDatabase.ConnectionFactory));
    }

    private static async Task<T> QueryAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Sin resultado.");
    }

    private static DischargeResidentCommand Discharge(
        SeededProfile admin, ResidentId resident, ResidentDischargeReason reason = ResidentDischargeReason.Fallecimiento,
        string? text = null, Guid? operationId = null) =>
        new(admin.ProfileScopeId, admin.CenterId, resident, operationId ?? Guid.NewGuid(), reason, text);

    private static SuspendResidentCommand Suspend(SeededProfile admin, ResidentId resident, Guid? operationId = null) =>
        new(admin.ProfileScopeId, admin.CenterId, resident, operationId ?? Guid.NewGuid(), "Hospital de referencia.");

    private static async Task<int> ResidentsInListAsync(SeededProfile admin) =>
        (await AdministracionResidentesTests.Build(admin.ExternalSubject).ListResidentsAsync(
            new AdministracionQuery(admin.ProfileScopeId, admin.CenterId))).Value!.Count;

    [Fact]
    public async Task Baja_CierraLaEstancia_ConservaLosDatosCincoAnios_YSaleDeLaListaActiva_YUnReenvioYaNoLoEncuentra()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Baja Integración");
        var service = Build(admin.ExternalSubject);
        var operationId = Guid.NewGuid();
        Assert.Equal(1, await ResidentsInListAsync(admin));

        var result = await service.DischargeAsync(Discharge(admin, resident, ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia.", operationId));
        var resent = await service.DischargeAsync(Discharge(admin, resident, ResidentDischargeReason.AltaVoluntaria, "Vuelve con su familia.", operationId));

        Assert.True(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, resent.Error!.Code);
        Assert.Equal(0, await ResidentsInListAsync(admin));
        Assert.Equal("INACTIVE", await QueryAsync<string>("SELECT estado FROM dbo.residentes WHERE id = @id", new { id = resident.Value }));
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @id AND vigente_hasta IS NULL", new { id = resident.Value }));
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.episodios_residente_centro WHERE residente_id = @id AND vigente_hasta IS NULL", new { id = resident.Value }));
        Assert.Equal(1, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.bajas_residente WHERE residente_id = @id", new { id = resident.Value }));
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @id AND accion_codigo = 'RESIDENT_DISCHARGE'", new { id = resident.Value }));

        var discharged = Assert.Single((await service.ListDischargedAsync(admin.ProfileScopeId, admin.CenterId)).Value!);
        Assert.Equal(resident, discharged.ResidentId);
        Assert.Equal(ResidentDischargeReason.AltaVoluntaria, discharged.Reason);
        Assert.Equal("Vuelve con su familia.", discharged.ReasonText);
        Assert.Equal(DateOnly.FromDateTime(discharged.DischargedAt.UtcDateTime).AddYears(5), discharged.RetainUntil);
    }

    [Fact]
    public async Task Baja_OtroMotivoExigeTexto_YUnResidenteAjenoOUnaNoAdministracionSonAccesoDenegado()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Baja Validación");
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var withoutText = await Build(admin.ExternalSubject).DischargeAsync(Discharge(admin, resident, ResidentDischargeReason.Otro, "  "));
        var byNurse = await Build(nurse.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            nurse.ProfileScopeId, nurse.CenterId, resident, Guid.NewGuid(), ResidentDischargeReason.Fallecimiento, null));
        var foreign = await Build(otherAdmin.ExternalSubject).DischargeAsync(Discharge(otherAdmin, resident));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutText.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        Assert.Equal("ACTIVE", await QueryAsync<string>("SELECT estado FROM dbo.residentes WHERE id = @id", new { id = resident.Value }));
    }

    [Fact]
    public async Task Reactivar_AbreUnaEstanciaNueva_VuelveALaLista_YUnReenvioNoLaRepite()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Reactivación Integración");
        var service = Build(admin.ExternalSubject);
        Assert.True((await service.DischargeAsync(Discharge(admin, resident))).Ok);
        var operationId = Guid.NewGuid();
        var command = new ReactivateResidentCommand(admin.ProfileScopeId, admin.CenterId, resident, operationId, admin.UnitId, null, null);

        var result = await service.ReactivateAsync(command);
        var resent = await service.ReactivateAsync(command);
        var again = await service.ReactivateAsync(command with { OperacionId = Guid.NewGuid() });

        Assert.True(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, resent.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, again.Error!.Code);
        Assert.Equal(1, await ResidentsInListAsync(admin));
        Assert.Equal("ACTIVE", await QueryAsync<string>("SELECT estado FROM dbo.residentes WHERE id = @id", new { id = resident.Value }));
        Assert.Equal(2, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.episodios_residente_centro WHERE residente_id = @id", new { id = resident.Value }));
        Assert.Empty((await service.ListDischargedAsync(admin.ProfileScopeId, admin.CenterId)).Value!);
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.bajas_residente WHERE residente_id = @id AND reactivada_en IS NOT NULL", new { id = resident.Value }));
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @id AND accion_codigo = 'RESIDENT_REACTIVATE'", new { id = resident.Value }));
    }

    [Fact]
    public async Task Reactivar_EnUnaUnidadFueraDelAmbito_EsAccesoDenegado_YElResidenteSigueDeBaja()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Reactivación Ámbito");
        var foreign = await AdministracionResidentesTests.AddUnitAsync(admin.CenterId);
        var service = Build(admin.ExternalSubject);
        Assert.True((await service.DischargeAsync(Discharge(admin, resident))).Ok);

        var result = await service.ReactivateAsync(new ReactivateResidentCommand(
            admin.ProfileScopeId, admin.CenterId, resident, Guid.NewGuid(), foreign, null, null));

        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Equal("INACTIVE", await QueryAsync<string>("SELECT estado FROM dbo.residentes WHERE id = @id", new { id = resident.Value }));
    }

    [Fact]
    public async Task Suspension_ImpideEscribirSobreElResidente_YReanudarLoDevuelve()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Suspensión Integración");
        var nursing = EnfermeriaApplicationServiceTests.BuildService(nurse.ExternalSubject);
        var openEvent = (await nursing.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, resident, "Tos desde la mañana.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()))).Value!;
        var service = Build(admin.ExternalSubject);

        var suspended = await service.SuspendAsync(Suspend(admin, resident));
        var twice = await service.SuspendAsync(Suspend(admin, resident));
        var newEvent = await nursing.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, resident, "Otro episodio.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        var start = await nursing.StartNursingAssessmentAsync(new StartNursingAssessmentCommand(
            nurse.ProfileScopeId, nurse.CenterId, openEvent.EventId, 1));

        Assert.True(suspended.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, twice.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, newEvent.Error!.Code);
        Assert.Contains("suspendido", newEvent.Error.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, start.Error!.Code);
        Assert.NotNull((await service.FindSuspensionAsync(admin.ProfileScopeId, admin.CenterId, resident)).Value);
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @id AND accion_codigo = 'RESIDENT_SUSPEND'", new { id = resident.Value }));

        Assert.True((await service.ResumeAsync(admin.ProfileScopeId, admin.CenterId, resident)).Ok);
        var resumed = await nursing.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, resident, "Episodio tras reanudar.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        var notSuspended = await service.ResumeAsync(admin.ProfileScopeId, admin.CenterId, resident);

        Assert.True(resumed.Ok);
        Assert.Null((await service.FindSuspensionAsync(admin.ProfileScopeId, admin.CenterId, resident)).Value);
        Assert.Equal(ApplicationFailureCode.Conflict, notSuspended.Error!.Code);
    }

    [Fact]
    public async Task Baja_PorFallecimiento_CierraSolaLosEventosAbiertos_ConUnaAnotacionDeSistema_YOtroMotivoNo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var deceased = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Fallecimiento Integración");
        var leaving = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Alta Voluntaria Integración");
        var nursing = EnfermeriaApplicationServiceTests.BuildService(nurse.ExternalSubject);
        async Task<Guid> OpenEventAsync(ResidentId resident) => (await nursing.RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, resident, "Disnea progresiva.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()))).Value!.EventId;
        var deceasedEvent = await OpenEventAsync(deceased);
        var leavingEvent = await OpenEventAsync(leaving);
        var service = Build(admin.ExternalSubject);

        var closed = await service.DischargeAsync(Discharge(admin, deceased, ResidentDischargeReason.Fallecimiento));
        var notClosed = await service.DischargeAsync(Discharge(admin, leaving, ResidentDischargeReason.AltaVoluntaria));

        Assert.Equal(1, closed.Value);
        Assert.Equal(0, notClosed.Value);
        Assert.Equal("CERRADO", await QueryAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @id", new { id = deceasedEvent }));
        Assert.Equal("FALLECIMIENTO", await QueryAsync<string>("SELECT cierre_sistema_codigo FROM dbo.eventos_asistenciales WHERE id = @id", new { id = deceasedEvent }));
        Assert.Equal(admin.AccountId.Value, await QueryAsync<Guid>("SELECT cerrado_por_cuenta_id FROM dbo.eventos_asistenciales WHERE id = @id", new { id = deceasedEvent }));
        Assert.Equal("PENDIENTE", await QueryAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @id", new { id = leavingEvent }));
        var detail = (await nursing.FindPendingChangeDetailAsync(new FindPendingChangeDetailCommand(nurse.ProfileScopeId, nurse.CenterId, deceasedEvent))).Value;
        Assert.True(detail!.Closure!.ClosedBySystemForDeath);
    }

    [Fact]
    public async Task LaBaseDeDatos_NoDejaCerrarUnEventoComoSistemaSinUnaBajaPorFallecimientoDeEsaTransaccion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Cierre Ilegal");
        var eventId = (await EnfermeriaApplicationServiceTests.BuildService(nurse.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, resident, "Tos.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()))).Value!.EventId;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var ex = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.eventos_asistenciales
               SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @accountId, cerrado_en = SYSUTCDATETIME(),
                   comunicacion_familiar_codigo = 'NO_COMUNICAR', cierre_sistema_codigo = 'FALLECIMIENTO'
             WHERE id = @eventId
            """, new { accountId = admin.AccountId.Value, eventId }));

        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", ex.Message);
    }

    [Fact]
    public async Task Baja_DeUnResidenteSuspendido_TerminaLaSuspension()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Baja Suspendido");
        var service = Build(admin.ExternalSubject);
        Assert.True((await service.SuspendAsync(Suspend(admin, resident))).Ok);

        Assert.True((await service.DischargeAsync(Discharge(admin, resident))).Ok);

        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.suspensiones_residente WHERE residente_id = @id AND finalizada_en IS NULL", new { id = resident.Value }));
    }
}

file sealed class FixedStatusSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
