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

/// <summary>Comunicados a la familia en Administración (script 0048; CJ, 2026-10-07): los de las unidades del ámbito con su hora de
/// publicación, y la publicación anticipada, que solo hace Administración y solo una vez.</summary>
public class AdministracionComunicadosTests
{
    private static AdministrationFamilyCommunicationApplicationService Build(string externalSubject)
    {
        var publication = new SqlFamilyCommunicationPublication(TestDatabase.ConnectionFactory);
        return new AdministrationFamilyCommunicationApplicationService(
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedComunicadosSessionIdentityProvider(externalSubject)),
            publication, publication);
    }

    /// <summary>Un evento cerrado por Enfermería con un comunicado preparado, y una Administración de su unidad.</summary>
    private static async Task<(SeededProfile Enfermera, SeededProfile Admin, Guid EventId)> SeedCommunicationAsync()
    {
        var (enfermera, _, eventId) = await EnfermeriaApplicationServiceTests.SeedOwnEventAsync();
        var revision = await EnfermeriaApplicationServiceTests.StartAndSaveAsync(enfermera, eventId);
        var closed = await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(
            EnfermeriaApplicationServiceTests.CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
                FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, "Hoy ha tenido tos; la estamos vigilando."));
        Assert.True(closed.Ok, closed.Error?.Message);
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, enfermera.CenterId, enfermera.UnitId);
        return (enfermera, admin, eventId);
    }

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task AdministracionVeLosComunicadosDeSusUnidades_ConSuHora_YLosPublicaAntesUnaSolaVez_ConAuditoria()
    {
        var (enfermera, admin, eventId) = await SeedCommunicationAsync();
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);

        var before = await service.ListAsync(admin.ProfileScopeId, admin.CenterId);
        var item = Assert.Single(before.Value!);
        var outside = await Build(outsider.ExternalSubject).ListAsync(outsider.ProfileScopeId, outsider.CenterId);
        var byOutsider = await Build(outsider.ExternalSubject).PublishNowAsync(
            new PublishFamilyCommunicationCommand(outsider.ProfileScopeId, outsider.CenterId, item.Communication.Id));
        var byNurse = await Build(enfermera.ExternalSubject).ListAsync(enfermera.ProfileScopeId, enfermera.CenterId);
        var published = await service.PublishNowAsync(new PublishFamilyCommunicationCommand(admin.ProfileScopeId, admin.CenterId, item.Communication.Id));
        var again = await service.PublishNowAsync(new PublishFamilyCommunicationCommand(admin.ProfileScopeId, admin.CenterId, item.Communication.Id));
        var after = (await service.ListAsync(admin.ProfileScopeId, admin.CenterId)).Value!.Single();
        var nursing = (await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(enfermera.ProfileScopeId, enfermera.CenterId))).Value!.Single();

        Assert.Equal(eventId, item.Communication.EventId);
        Assert.Equal("Hoy ha tenido tos; la estamos vigilando.", item.Communication.Text);
        Assert.False(item.Published);
        Assert.True(item.ScheduledAt >= item.Communication.PreparedAt + FamilyCommunicationSchedule.Margin);
        Assert.Empty(outside.Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.True(published.Ok, published.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.True(after.Published);
        Assert.NotNull(after.Communication.PublishedEarlyAt);
        Assert.Equal(after.Communication.PublishedEarlyAt, nursing.PublishedEarlyAt);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'FAMILY_COMMUNICATION_PUBLISH_NOW' AND recurso_id = @c",
            new { a = admin.AccountId.Value, c = item.Communication.Id }));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.comunicaciones_familiares_publicacion_anticipada WHERE comunicacion_id = @c AND publicada_por_cuenta_id = @a",
            new { c = item.Communication.Id, a = admin.AccountId.Value }));
    }

    [Fact]
    public async Task UnComunicadoQueYaLlegoASuHora_YaEstaPublicado_YNoSePuedePublicarAntes()
    {
        var (enfermera, admin, _) = await SeedCommunicationAsync();
        var secondEventId = await RegisterSecondEventAsync(enfermera);
        // La tabla es de solo inserción: un comunicado preparado hace tres días entra ya con su hora pasada.
        var communicationId = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.comunicaciones_familiares (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en)
                SELECT @Id, ea.id, ea.residente_id, ea.centro_id, 'RELEVANTE', 'Comunicado de hace tres días.', @AccountId, DATEADD(DAY, -3, SYSUTCDATETIME())
                  FROM dbo.eventos_asistenciales ea WHERE ea.id = @EventId
                """, new { Id = communicationId, AccountId = enfermera.AccountId.Value, EventId = secondEventId });
        }

        var listed = (await Build(admin.ExternalSubject).ListAsync(admin.ProfileScopeId, admin.CenterId)).Value!;
        var old = listed.Single(c => c.Communication.Id == communicationId);
        var result = await Build(admin.ExternalSubject).PublishNowAsync(new PublishFamilyCommunicationCommand(admin.ProfileScopeId, admin.CenterId, communicationId));

        Assert.True(old.Published);
        Assert.Null(old.Communication.PublishedEarlyAt);
        Assert.Equal(ApplicationFailureCode.Conflict, result.Error!.Code);
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM dbo.comunicaciones_familiares_publicacion_anticipada WHERE comunicacion_id = @c", new { c = communicationId }));
    }

    [Fact]
    public async Task LaPublicacionAnticipadaNoSePuedeCambiarNiBorrar()
    {
        var (_, admin, _) = await SeedCommunicationAsync();
        var service = Build(admin.ExternalSubject);
        var id = (await service.ListAsync(admin.ProfileScopeId, admin.CenterId)).Value!.Single().Communication.Id;
        Assert.True((await service.PublishNowAsync(new PublishFamilyCommunicationCommand(admin.ProfileScopeId, admin.CenterId, id))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var update = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.comunicaciones_familiares_publicacion_anticipada SET publicada_en = SYSUTCDATETIME() WHERE comunicacion_id = @id", new { id }));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.comunicaciones_familiares_publicacion_anticipada WHERE comunicacion_id = @id", new { id }));

        Assert.Contains("FAMILY_COMMUNICATION_PUBLICATION_IMMUTABLE", update.Message);
        Assert.Contains("FAMILY_COMMUNICATION_PUBLICATION_IMMUTABLE", delete.Message);
    }

    private static async Task<Guid> RegisterSecondEventAsync(SeededProfile enfermera)
    {
        Guid residentId;
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            residentId = await connection.ExecuteScalarAsync<Guid>(
                "SELECT TOP (1) residente_id FROM dbo.eventos_asistenciales WHERE centro_id = @c", new { c = enfermera.CenterId.Value });
        }

        var registered = await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(
            new RegisterClinicalEventCommand(enfermera.ProfileScopeId, enfermera.CenterId, ResidentId.From(residentId),
                "Segundo evento de la prueba.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        Assert.True(registered.Ok, registered.Error?.Message);
        return registered.Value!.EventId;
    }
}

file sealed class FixedComunicadosSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
