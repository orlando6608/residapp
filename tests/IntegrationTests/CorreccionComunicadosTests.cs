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

/// <summary>Corrección del texto de un comunicado a la familia durante su margen de 1 hora (script 0049; CJ, 2026-10-07): la hace Enfermería
/// del ámbito, deja historial, y deja de ser posible pasado el margen o publicado antes por Administración.</summary>
public class CorreccionComunicadosTests
{
    private const string Original = "Hoy ha tenido tos; la estamos vigilando.";

    private static CorrectFamilyCommunication Build(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedCorreccionSessionIdentityProvider(externalSubject),
        new SqlFamilyCommunicationCorrection(TestDatabase.ConnectionFactory));

    private static CorrectFamilyCommunicationCommand Command(
        SeededProfile nurse, Guid communicationId, int version, string? text, FamilyCommunicationType? type = FamilyCommunicationType.Ordinaria) =>
        new(nurse.ProfileScopeId, nurse.CenterId, communicationId, version, type, text);

    private static async Task<(SeededProfile Enfermera, Guid EventId, Guid CommunicationId)> SeedAsync()
    {
        var (enfermera, _, eventId) = await EnfermeriaApplicationServiceTests.SeedOwnEventAsync();
        var revision = await EnfermeriaApplicationServiceTests.StartAndSaveAsync(enfermera, eventId);
        var closed = await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(
            EnfermeriaApplicationServiceTests.CloseCommand(enfermera, eventId, revision, Guid.NewGuid(),
                FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, Original));
        Assert.True(closed.Ok, closed.Error?.Message);
        return (enfermera, eventId, await CommunicationIdAsync(eventId));
    }

    private static async Task<Guid> CommunicationIdAsync(Guid eventId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.comunicaciones_familiares WHERE evento_id = @eventId", new { eventId });
    }

    private static async Task<PendingFamilyCommunicationSummary> NursingViewAsync(SeededProfile nurse, Guid communicationId) =>
        (await EnfermeriaApplicationServiceTests.BuildService(nurse.ExternalSubject).ListPendingFamilyCommunicationsAsync(
            new ListPendingFamilyCommunicationsCommand(nurse.ProfileScopeId, nurse.CenterId))).Value!.Single(c => c.CommunicationId == communicationId);

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task EnfermeriaDelAmbitoCorrigeDentroDelMargen_ConHistorial_ElOriginalSeConserva_YElTextoVigenteLoVeTodo()
    {
        var (enfermera, eventId, communicationId) = await SeedAsync();
        var companera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, enfermera.CenterId, enfermera.UnitId);
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, enfermera.CenterId, enfermera.UnitId);

        var first = await Build(companera.ExternalSubject).ExecuteAsync(
            Command(companera, communicationId, 0, "  Hoy ha tenido tos y fiebre; la estamos vigilando.  ", FamilyCommunicationType.Relevante));
        var second = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 1, "Hoy ha tenido tos y fiebre leve; la vigilamos.", FamilyCommunicationType.Relevante));
        var view = await NursingViewAsync(enfermera, communicationId);
        var detail = (await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(enfermera.ProfileScopeId, enfermera.CenterId, eventId))).Value!;
        var asAdmin = (await new AdministrationFamilyCommunicationApplicationService(
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedCorreccionSessionIdentityProvider(admin.ExternalSubject)),
            new SqlFamilyCommunicationPublication(TestDatabase.ConnectionFactory), new SqlFamilyCommunicationPublication(TestDatabase.ConnectionFactory))
            .ListAsync(admin.ProfileScopeId, admin.CenterId)).Value!.Single();

        Assert.True(first.Ok, first.Error?.Message);
        Assert.True(second.Ok, second.Error?.Message);
        Assert.Equal(2, view.Version);
        Assert.Equal(FamilyCommunicationType.Relevante, view.Communication.Type);
        Assert.Equal("Hoy ha tenido tos y fiebre leve; la vigilamos.", view.Communication.Text);
        Assert.Equal(view.Communication, detail.Closure!.Communication);
        Assert.Equal(view.Communication.Text, asAdmin.Communication.Text);
        Assert.Equal(Original, await new Func<Task<string>>(async () =>
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            return await connection.ExecuteScalarAsync<string>("SELECT texto FROM dbo.comunicaciones_familiares WHERE id = @communicationId", new { communicationId });
        })());
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'FAMILY_COMMUNICATION_CORRECT' AND recurso_id = @c",
            new { a = companera.AccountId.Value, c = communicationId }));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones WHERE comunicacion_id = @c AND numero IN (1, 2)", new { c = communicationId }));
    }

    [Fact]
    public async Task LaCorreccionExigeLaVersionVigente_UnTextoValido_YUnCambio()
    {
        var (enfermera, _, communicationId) = await SeedAsync();
        Assert.True((await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 0, "Primera corrección."))).Ok);

        var stale = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 0, "Otra corrección."));
        var unchanged = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 1, "Primera corrección."));
        var empty = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 1, "   "));
        var noType = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 1, "Con tipo.", type: null));
        var tooLong = await Build(enfermera.ExternalSubject).ExecuteAsync(
            Command(enfermera, communicationId, 1, new string('a', FamilyCommunicationChoice.MaxTextLength + 1)));

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unchanged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, empty.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noType.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones WHERE comunicacion_id = @c", new { c = communicationId }));
    }

    [Fact]
    public async Task SoloEnfermeriaDelAmbitoCorrige()
    {
        var (enfermera, _, communicationId) = await SeedAsync();
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, enfermera.CenterId, enfermera.UnitId);

        var byOutsider = await Build(outsider.ExternalSubject).ExecuteAsync(Command(outsider, communicationId, 0, "Intento ajeno."));
        var byAdmin = await Build(admin.ExternalSubject).ExecuteAsync(Command(admin, communicationId, 0, "Intento de Administración."));
        var mismatched = await Build(outsider.ExternalSubject).ExecuteAsync(
            Command(enfermera, communicationId, 0, "Con el ámbito de otra persona."));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byAdmin.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, mismatched.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones WHERE comunicacion_id = @c", new { c = communicationId }));
    }

    [Fact]
    public async Task NoSeCorrigeTrasLaPublicacionAnticipada_NiPasadoElMargen()
    {
        var (enfermera, eventId, communicationId) = await SeedAsync();
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, enfermera.CenterId, enfermera.UnitId);
        var publisher = new SqlFamilyCommunicationPublication(TestDatabase.ConnectionFactory);
        await publisher.PublishNowAsync(new AccountAdministrationAccess(admin.ProfileScopeId, admin.AccountId, admin.CenterId), communicationId);

        // La tabla es de solo inserción: un segundo comunicado preparado hace dos horas entra ya con el margen pasado.
        Guid residentId;
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            residentId = await connection.ExecuteScalarAsync<Guid>("SELECT residente_id FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });
        }
        var registered = await EnfermeriaApplicationServiceTests.BuildService(enfermera.ExternalSubject).RegisterClinicalEventAsync(
            new RegisterClinicalEventCommand(enfermera.ProfileScopeId, enfermera.CenterId, ResidentId.From(residentId),
                "Segundo evento de la prueba.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));
        var oldId = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.comunicaciones_familiares (id, evento_id, residente_id, centro_id, tipo_codigo, texto, preparado_por_cuenta_id, preparado_en)
                SELECT @Id, ea.id, ea.residente_id, ea.centro_id, 'ORDINARIA', 'Comunicado de hace dos horas.', @AccountId, DATEADD(HOUR, -2, SYSUTCDATETIME())
                  FROM dbo.eventos_asistenciales ea WHERE ea.id = @EventId
                """, new { Id = oldId, AccountId = enfermera.AccountId.Value, EventId = registered.Value!.EventId });
        }

        var afterPublication = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 0, "Tarde para corregir."));
        var afterMargin = await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, oldId, 0, "Pasado el margen."));

        Assert.Equal(ApplicationFailureCode.Conflict, afterPublication.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, afterMargin.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correcciones WHERE comunicacion_id IN (@a, @b)", new { a = communicationId, b = oldId }));
    }

    [Fact]
    public async Task LaBaseDeDatosNoDejaCambiarNiBorrarUnaCorreccion_NiSaltarSuNumeracion()
    {
        var (enfermera, _, communicationId) = await SeedAsync();
        Assert.True((await Build(enfermera.ExternalSubject).ExecuteAsync(Command(enfermera, communicationId, 0, "Corregido."))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var update = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.comunicaciones_familiares_correcciones SET texto = 'Otro' WHERE comunicacion_id = @communicationId", new { communicationId }));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.comunicaciones_familiares_correcciones WHERE comunicacion_id = @communicationId", new { communicationId }));
        var skip = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares_correcciones (id, comunicacion_id, centro_id, residente_id, numero, tipo_codigo, texto, corregida_por_cuenta_id, corregida_en)
            SELECT NEWID(), id, centro_id, residente_id, 5, 'ORDINARIA', 'Salto', @AccountId, SYSUTCDATETIME()
              FROM dbo.comunicaciones_familiares WHERE id = @communicationId
            """, new { communicationId, AccountId = enfermera.AccountId.Value }));

        Assert.Contains("FAMILY_COMMUNICATION_CORRECTION_IMMUTABLE", update.Message);
        Assert.Contains("FAMILY_COMMUNICATION_CORRECTION_IMMUTABLE", delete.Message);
        Assert.Contains("FAMILY_COMMUNICATION_CORRECTION_SEQUENCE_INVALID", skip.Message);
    }
}

file sealed class FixedCorreccionSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
