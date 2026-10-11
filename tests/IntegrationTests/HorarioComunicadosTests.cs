using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Hora de publicación y margen de los comunicados a la familia, por centro (script 0051; CJ, 2026-10-10): los fija Administración del
/// centro con historial y versión, y la hora de cada comunicado queda fijada al guardarlo.</summary>
public class HorarioComunicadosTests
{
    private static FamilyCommunicationScheduleApplicationService Build(string externalSubject) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedHorarioSessionIdentityProvider(externalSubject)),
        new SqlFamilyCommunicationScheduleRepository(TestDatabase.ConnectionFactory));

    private static SaveFamilyCommunicationScheduleCommand Save(SeededProfile admin, int version, TimeOnly? time, int? margin) =>
        new(admin.ProfileScopeId, admin.CenterId, version, time, margin);

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task AdministracionVeElHorarioDeCJ_LoCambiaConVersionEHistorial_YVolverAlDeCJBorraLaFilaDelCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);

        var initial = (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!;
        var saved = await service.SaveAsync(Save(admin, 0, new TimeOnly(20, 30, 59), 120));
        var unchanged = await service.SaveAsync(Save(admin, 1, new TimeOnly(20, 30), 120));
        var stale = await service.SaveAsync(Save(admin, 0, new TimeOnly(21, 0), 30));
        var afterSave = (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!;
        var rows = await CountAsync("SELECT COUNT(*) FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = admin.CenterId.Value });
        var back = await service.SaveAsync(Save(admin, 1, FamilyCommunicationScheduleSettings.Default.PublicationTime, FamilyCommunicationScheduleSettings.Default.MarginMinutes));
        var afterReset = (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!;

        Assert.Equal(FamilyCommunicationScheduleSettings.Default, initial.Settings);
        Assert.Equal(0, initial.Version);
        Assert.Equal(1, saved.Value);
        Assert.Equal(1, unchanged.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(new FamilyCommunicationScheduleSettings(new TimeOnly(20, 30), TimeSpan.FromHours(2)), afterSave.Settings);
        Assert.Equal(1, afterSave.Version);
        var change = Assert.Single(afterSave.History);
        Assert.Equal((FamilyCommunicationScheduleSettings.Default, new FamilyCommunicationScheduleSettings(new TimeOnly(20, 30), TimeSpan.FromHours(2)), true),
            (change.Previous, change.New, change.ChangedByCurrentAccount));
        Assert.Equal(1, rows);
        Assert.Equal(2, back.Value);
        Assert.Equal(FamilyCommunicationScheduleSettings.Default, afterReset.Settings);
        Assert.Equal(2, afterReset.History.Count);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = admin.CenterId.Value }));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'FAMILY_SCHEDULE_UPDATE'", new { a = admin.AccountId.Value }));
    }

    [Fact]
    public async Task ElHorarioRechazaValoresInvalidos_OtrosPerfiles_YOtrosCentros()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId);
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);

        var noTime = await service.SaveAsync(Save(admin, 0, null, 60));
        var noMargin = await service.SaveAsync(Save(admin, 0, new TimeOnly(19, 0), null));
        var negative = await service.SaveAsync(Save(admin, 0, new TimeOnly(19, 0), -1));
        var tooLong = await service.SaveAsync(Save(admin, 0, new TimeOnly(19, 0), 1441));
        var byNurse = await Build(nurse.ExternalSubject).SaveAsync(new SaveFamilyCommunicationScheduleCommand(nurse.ProfileScopeId, nurse.CenterId, 0, new TimeOnly(18, 0), 30));
        var readByNurse = await Build(nurse.ExternalSubject).ReadAsync(nurse.ProfileScopeId, nurse.CenterId);
        var byDirection = await Build(direction.ExternalSubject).SaveAsync(new SaveFamilyCommunicationScheduleCommand(direction.ProfileScopeId, direction.CenterId, 0, new TimeOnly(18, 0), 30));
        var crossCenter = await Build(otherAdmin.ExternalSubject).SaveAsync(new SaveFamilyCommunicationScheduleCommand(admin.ProfileScopeId, admin.CenterId, 0, new TimeOnly(18, 0), 30));
        Assert.True((await Build(otherAdmin.ExternalSubject).SaveAsync(Save(otherAdmin, 0, new TimeOnly(18, 0), 30))).Ok);

        Assert.All(new[] { noTime, noMargin, negative, tooLong }, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, readByNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byDirection.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, crossCenter.Error!.Code);
        // El horario de un centro no cambia el de otro.
        Assert.Equal(FamilyCommunicationScheduleSettings.Default, (await service.ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!.Settings);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.comunicados_horario_centro WHERE centro_id = @c", new { c = admin.CenterId.Value }));
    }

    [Fact]
    public async Task LaHoraDeCadaComunicadoQuedaFijadaAlGuardarlo_YCambiarElHorarioNoMueveLosYaGuardados()
    {
        var (enfermera, _, firstEvent) = await EnfermeriaApplicationServiceTests.SeedOwnEventAsync();
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, enfermera.CenterId, enfermera.UnitId);
        var nursing = EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject);
        var service = Build(admin.ExternalSubject);

        var first = await CloseWithCommunicationAsync(enfermera, firstEvent, "Primer comunicado, con el horario de CJ.");
        Assert.True((await service.SaveAsync(Save(admin, 0, new TimeOnly(20, 30), 0))).Ok);
        var secondEvent = await RegisterAnotherEventAsync(enfermera, firstEvent);
        var second = await CloseWithCommunicationAsync(enfermera, secondEvent, "Segundo comunicado, con el horario del centro.");

        var summaries = (await nursing.ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!;
        var firstSummary = summaries.Single(c => c.CommunicationId == first);
        var secondSummary = summaries.Single(c => c.CommunicationId == second);
        Assert.True((await service.SaveAsync(Save(admin, 1, new TimeOnly(8, 0), 600))).Ok);
        var afterChange = (await nursing.ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!;

        Assert.Equal(
            FamilyCommunicationSchedule.ScheduledAt(firstSummary.Communication.PreparedAt, FamilyCommunicationScheduleSettings.Default, TimeZoneInfo.Local),
            firstSummary.ScheduledAt);
        Assert.Equal(
            FamilyCommunicationSchedule.ScheduledAt(
                secondSummary.Communication.PreparedAt, new FamilyCommunicationScheduleSettings(new TimeOnly(20, 30), TimeSpan.Zero), TimeZoneInfo.Local),
            secondSummary.ScheduledAt);
        Assert.Equal(firstSummary.ScheduledAt, afterChange.Single(c => c.CommunicationId == first).ScheduledAt);
        Assert.Equal(secondSummary.ScheduledAt, afterChange.Single(c => c.CommunicationId == second).ScheduledAt);
    }

    [Fact]
    public async Task LaBaseDeDatosNoDejaCambiarNiBorrarElHistorialDelHorario()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await Build(admin.ExternalSubject).SaveAsync(Save(admin, 0, new TimeOnly(20, 0), 90))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var update = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.comunicados_horario_centro_historial SET hora_nueva = '01:00' WHERE centro_id = @c", new { c = admin.CenterId.Value }));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.comunicados_horario_centro_historial WHERE centro_id = @c", new { c = admin.CenterId.Value }));
        var margin = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.comunicados_horario_centro SET margen_minutos = 1441 WHERE centro_id = @c", new { c = admin.CenterId.Value }));

        Assert.Contains("FAMILY_COMMUNICATION_SCHEDULE_HISTORY_IMMUTABLE", update.Message);
        Assert.Contains("FAMILY_COMMUNICATION_SCHEDULE_HISTORY_IMMUTABLE", delete.Message);
        Assert.Contains("CK_chc_margen", margin.Message);
    }

    private static async Task<Guid> CloseWithCommunicationAsync(SeededProfile enfermera, Guid eventId, string text)
    {
        var revision = await EnfermeriaApplicationServiceTests.StartAndSaveAsync(enfermera, eventId);
        var closed = await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(
            EnfermeriaApplicationServiceTests.CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
                FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, text));
        Assert.True(closed.Ok, closed.Error?.Message);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.comunicaciones_familiares WHERE evento_id = @eventId", new { eventId });
    }

    private static async Task<Guid> RegisterAnotherEventAsync(SeededProfile enfermera, Guid sameResidentAs)
    {
        Guid residentId;
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            residentId = await connection.ExecuteScalarAsync<Guid>("SELECT residente_id FROM dbo.eventos_asistenciales WHERE id = @sameResidentAs", new { sameResidentAs });
        }

        var registered = await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(
            new RegisterClinicalEventCommand(enfermera.ProfileScopeId, enfermera.CenterId, ResidentId.From(residentId),
                "Otro evento de la prueba.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value!.EventId;
    }
}

file sealed class FixedHorarioSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
