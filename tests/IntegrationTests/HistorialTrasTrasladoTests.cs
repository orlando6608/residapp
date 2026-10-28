using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>Historial de un residente trasladado (script 0077, CJ tercera tanda 5.2 C): la unidad de destino abre los episodios cerrados de la unidad anterior solo
/// con una petición con motivo, que vale 1 hora, es de una cuenta y un residente, se audita en cada lectura y la revalida SQL. Cada prueba crea su propio centro.</summary>
public class HistorialTrasTrasladoTests
{
    private const string Reason = "Continuidad asistencial tras el traslado.";

    private static readonly SqlChangeInboxDirectory Directory = new(TestDatabase.ConnectionFactory);

    private static TransferHistoryAccess BuildAccess(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedTransferHistorySessionIdentityProvider(externalSubject),
        new SqlTransferHistoryAccess(TestDatabase.ConnectionFactory), Directory, ClinicalAccessSettings.Default);

    private static ResidentTransferApplicationService BuildTransfer(string externalSubject) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedTransferHistorySessionIdentityProvider(externalSubject),
        new SqlResidentTransferRepository(TestDatabase.ConnectionFactory));

    private static async Task<T> QueryAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Sin resultado.");
    }

    private static Task<int> ReadAuditCountAsync(ResidentId residentId) => QueryAsync<int>(
        "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @id AND accion_codigo = 'TRANSFER_HISTORY_READ'", new { id = residentId.Value });

    private static async Task GrantUnitAsync(SeededProfile profile, UnitId unitId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, SYSUTCDATETIME(), @AccountId)
            """, new { profile.ProfileScopeId, CenterId = profile.CenterId.Value, UnitId = unitId.Value, AccountId = profile.AccountId.Value });
    }

    private sealed record Setup(
        SeededProfile Admin, UnitId Origin, UnitId Destination, ResidentId Resident, SeededProfile NurseOrigin, SeededProfile NurseDestination, Guid ClosedEventId);

    /// <summary>Una Administración con dos unidades; un residente en la primera con un episodio cerrado y otro abierto, trasladado a la segunda; una Enfermería en cada unidad.</summary>
    private static async Task<Setup> SeedTransferredAsync(string name = "Residente Historial Traslado")
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var destination = await AdministracionResidentesTests.AddUnitAsync(admin.CenterId);
        await GrantUnitAsync(admin, destination);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, name);
        var nurseOrigin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var nurseDestination = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, destination);

        var closedId = await CloseEventAsync(nurseOrigin, resident, "Caída sin lesiones en el baño.");
        await RegisterAsync(nurseOrigin, resident, "Inapetencia en la cena.");
        await TransferAsync(admin, resident, admin.UnitId, destination);
        return new Setup(admin, admin.UnitId, destination, resident, nurseOrigin, nurseDestination, closedId);
    }

    private static async Task<Guid> CloseEventAsync(SeededProfile nurse, ResidentId residentId, string observation)
    {
        var eventId = await RegisterAsync(nurse, residentId, observation);
        var revision = await StartAndSaveAsync(nurse, eventId);
        Assert.True((await BuildService(nurse.ExternalSubject).CloseClinicalEventAsync(new CloseClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, eventId, revision, Guid.NewGuid(), FamilyCommunicationDecision.NoComunicar, null, null))).Ok);
        return eventId;
    }

    private static async Task TransferAsync(SeededProfile admin, ResidentId residentId, UnitId from, UnitId to) =>
        Assert.True((await BuildTransfer(admin.ExternalSubject).TransferAsync(
            new TransferResidentCommand(admin.ProfileScopeId, admin.CenterId, residentId, from, to, null, null, Guid.NewGuid()))).Ok);

    private static async Task<Guid> RegisterAsync(SeededProfile nurse, ResidentId residentId, string observation) =>
        (await BuildService(nurse.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, residentId, observation, DailyChangeClassification.Ordinario, null, Guid.NewGuid()))).Value!.EventId;

    private static Task<ApplicationResult<Guid>> DeclareAsync(
        SeededProfile who, ResidentId residentId, string? reason = Reason, SystemProfile profile = SystemProfile.Enfermeria) =>
        BuildAccess(who.ExternalSubject).DeclareAsync(new DeclareTransferHistoryCommand(who.ProfileScopeId, who.CenterId, residentId, reason, profile));

    private static Task<IReadOnlyList<ClosedEventSummary>> ClosedAsync(SeededProfile who, ResidentId residentId, Guid? declarationId = null) =>
        Directory.ListClosedEventsAsync(who.ProfileScopeId, who.CenterId, residentId, transferDeclarationId: declarationId);

    [Fact]
    public async Task SinPeticionElDestinoNoVeLosCerradosDeLaUnidadAnterior_ConPeticionSi_YCadaLecturaQuedaAuditada()
    {
        var s = await SeedTransferredAsync();

        Assert.Empty(await ClosedAsync(s.NurseDestination, s.Resident));
        Assert.Null(await Directory.FindAsync(s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, s.ClosedEventId));
        Assert.DoesNotContain(
            await Directory.ListTimelineAsync(s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, s.Resident),
            e => e.EventId == s.ClosedEventId);
        Assert.Equal(1, await Directory.CountPreviousUnitClosedEventsAsync(s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, s.Resident));

        var declared = await DeclareAsync(s.NurseDestination, s.Resident, "  " + Reason + "  ");
        Assert.True(declared.Ok, declared.Error?.Message);
        var declarationId = declared.Value;
        Assert.Equal(1, await ReadAuditCountAsync(s.Resident));
        Assert.Equal(Reason, await QueryAsync<string>(
            "SELECT TOP (1) justificacion FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @id AND accion_codigo = 'TRANSFER_HISTORY_READ'", new { id = s.Resident.Value }));

        var history = Assert.Single(await ClosedAsync(s.NurseDestination, s.Resident, declarationId));
        Assert.Equal(s.ClosedEventId, history.EventId);
        Assert.True(history.FromPreviousUnit);
        Assert.Equal(2, await ReadAuditCountAsync(s.Resident));

        var timeline = await Directory.ListTimelineAsync(
            s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, s.Resident, transferDeclarationId: declarationId);
        Assert.Contains(timeline, e => e.EventId == s.ClosedEventId && e.FromPreviousUnit);
        Assert.Equal(3, await ReadAuditCountAsync(s.Resident));

        var detail = await Directory.FindAsync(
            s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, s.ClosedEventId, transferDeclarationId: declarationId);
        Assert.NotNull(detail);
        Assert.True(detail.FromPreviousUnit);
        Assert.Equal(4, await ReadAuditCountAsync(s.Resident));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            var resources = (await connection.QueryAsync<string>(
                "SELECT tipo_recurso FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @id AND accion_codigo = 'TRANSFER_HISTORY_READ'",
                new { id = s.Resident.Value })).Order().ToList();
            Assert.Equal(["CLINICAL_EVENT", "CLOSED_EVENTS_HISTORY", "RESIDENT_TIMELINE", "RESIDENT_TRANSFER_HISTORY"], resources);
        }

        // Lo abierto sigue su camino y el origen no cambia: el episodio cerrado sigue siendo de la unidad anterior, que lo ve como siempre.
        Assert.Null(await Directory.FindAsync(s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, s.ClosedEventId));
        var origin = Assert.Single(await ClosedAsync(s.NurseOrigin, s.Resident));
        Assert.False(origin.FromPreviousUnit);
    }

    [Fact]
    public async Task LaPeticionNoLaHeredaOtraCuentaNiOtroResidente_NiSirveCaducadaTerminadaOTrasCerrarSesion()
    {
        var s = await SeedTransferredAsync();
        var otherNurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, s.Admin.CenterId, s.Destination);
        var otherResident = await AdministracionResidentesTests.CreateResidentAsync(s.Admin, "Otro Residente Historial Traslado");
        var otherClosed = await CloseEventAsync(s.NurseOrigin, otherResident, "Control de glucemia sin incidencias.");
        await TransferAsync(s.Admin, otherResident, s.Origin, s.Destination);
        var declarationId = (await DeclareAsync(s.NurseDestination, s.Resident)).Value;

        // Otra cuenta del mismo ámbito o el mismo residente con otra cuenta, y otro residente con la petición de este: nada.
        Assert.Empty(await ClosedAsync(otherNurse, s.Resident, declarationId));
        Assert.Null(await Directory.FindAsync(otherNurse.ProfileScopeId, otherNurse.CenterId, s.ClosedEventId, transferDeclarationId: declarationId));
        Assert.Empty(await ClosedAsync(s.NurseDestination, otherResident, declarationId));
        Assert.Null(await Directory.FindAsync(
            s.NurseDestination.ProfileScopeId, s.NurseDestination.CenterId, otherClosed, transferDeclarationId: declarationId));
        // Una petición inventada tampoco.
        Assert.Empty(await ClosedAsync(s.NurseDestination, s.Resident, Guid.NewGuid()));
        Assert.Single(await ClosedAsync(s.NurseDestination, s.Resident, declarationId));

        // Cerrar sesión o cambiar de ámbito la termina.
        await new SqlBaselineRepository(TestDatabase.ConnectionFactory).EndAccessDeclarationsAsync(s.NurseDestination.ExternalSubject);
        Assert.Empty(await ClosedAsync(s.NurseDestination, s.Resident, declarationId));

        // Una petición caducada (insertada a mano, con la hora pasada) no sirve.
        var expiredId = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.declaraciones_historial_traslado (id, centro_id, cuenta_id, ambito_perfil_id, residente_id, motivo, creada_en, caduca_en)
                VALUES (@expiredId, @center, @account, @scope, @resident, 'Caducada', DATEADD(HOUR, -2, SYSUTCDATETIME()), DATEADD(HOUR, -1, SYSUTCDATETIME()))
                """, new
            {
                expiredId, center = s.NurseDestination.CenterId.Value, account = s.NurseDestination.AccountId.Value,
                scope = s.NurseDestination.ProfileScopeId, resident = s.Resident.Value,
            });
        }

        Assert.Empty(await ClosedAsync(s.NurseDestination, s.Resident, expiredId));
        // Pedir otra vez, con un motivo nuevo, vale de nuevo y termina la anterior.
        var renewed = (await DeclareAsync(s.NurseDestination, s.Resident, "Revisión de la pauta de seguimiento.")).Value;
        Assert.Single(await ClosedAsync(s.NurseDestination, s.Resident, renewed));
    }

    [Fact]
    public async Task Pedir_ExigeMotivoDeHastaTrescientosCaracteres_YQueHayaAlgoQuePedirEnSuAmbito()
    {
        var s = await SeedTransferredAsync();
        var medicina = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, s.Admin.CenterId, s.Destination);

        var empty = await DeclareAsync(s.NurseDestination, s.Resident, "   ");
        var missing = await DeclareAsync(s.NurseDestination, s.Resident, null);
        var tooLong = await DeclareAsync(s.NurseDestination, s.Resident, new string('x', TransferHistoryRequest.MaxReasonLength + 1));
        var exact = await DeclareAsync(s.NurseDestination, s.Resident, new string('x', TransferHistoryRequest.MaxReasonLength));
        // La Enfermería de la unidad de origen ya no tiene al residente; Medicina solo ve escalados y propios, y el cerrado no lo fue.
        var fromOrigin = await DeclareAsync(s.NurseOrigin, s.Resident);
        var asMedicine = await DeclareAsync(medicina, s.Resident, Reason, SystemProfile.Medicina);
        // Con el perfil de otro ámbito no se pide.
        var wrongProfile = await DeclareAsync(s.NurseDestination, s.Resident, Reason, SystemProfile.Medicina);

        Assert.Equal(ApplicationFailureCode.InvalidInput, empty.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, missing.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        Assert.True(exact.Ok, exact.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, fromOrigin.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, asMedicine.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, wrongProfile.Error!.Code);
        Assert.Equal(1, await ReadAuditCountAsync(s.Resident));
        Assert.Empty(await ClosedAsync(medicina, s.Resident));
        Assert.Equal(0, await Directory.CountPreviousUnitClosedEventsAsync(medicina.ProfileScopeId, medicina.CenterId, s.Resident));
    }

    [Fact]
    public async Task TrasVariosTraslados_LaUnidadActualPideLosCerradosDeTodasLasAnteriores()
    {
        var s = await SeedTransferredAsync();
        var third = await AdministracionResidentesTests.AddUnitAsync(s.Admin.CenterId);
        await GrantUnitAsync(s.Admin, third);
        var nurseThird = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, s.Admin.CenterId, third);
        var closedInSecond = await CloseEventAsync(s.NurseDestination, s.Resident, "Tos seca sin fiebre.");
        await TransferAsync(s.Admin, s.Resident, s.Destination, third);

        var declarationId = (await DeclareAsync(nurseThird, s.Resident)).Value;
        var history = await ClosedAsync(nurseThird, s.Resident, declarationId);

        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.True(h.FromPreviousUnit));
        Assert.Contains(history, h => h.EventId == s.ClosedEventId);
        Assert.Contains(history, h => h.EventId == closedInSecond);
        // Lo que sigue abierto se movió con el residente y se ve como siempre; la segunda unidad ya no ve nada del residente sin petición propia.
        Assert.Empty(await ClosedAsync(nurseThird, s.Resident));
    }

    [Fact]
    public async Task LaDeclaracionSoloSeCreaYSeTermina_NoSeEditaNiSeBorra()
    {
        var s = await SeedTransferredAsync();
        var declarationId = (await DeclareAsync(s.NurseDestination, s.Resident)).Value;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var edit = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_historial_traslado SET motivo = 'Otro motivo' WHERE id = @id", new { id = declarationId }));
        var extend = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_historial_traslado SET caduca_en = DATEADD(DAY, 1, caduca_en) WHERE id = @id", new { id = declarationId }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.declaraciones_historial_traslado WHERE id = @id", new { id = declarationId }));
        var ended = await connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_historial_traslado SET terminada_en = SYSUTCDATETIME() WHERE id = @id", new { id = declarationId });
        var endedAgain = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.declaraciones_historial_traslado SET terminada_en = SYSUTCDATETIME() WHERE id = @id", new { id = declarationId }));

        Assert.Contains("TRANSFER_HISTORY_DECLARATION_IMMUTABLE", edit.Message);
        Assert.Contains("TRANSFER_HISTORY_DECLARATION_IMMUTABLE", extend.Message);
        Assert.Contains("TRANSFER_HISTORY_DECLARATION_IMMUTABLE", delete.Message);
        Assert.Equal(1, ended);
        Assert.Contains("TRANSFER_HISTORY_DECLARATION_IMMUTABLE", endedAgain.Message);
        Assert.Empty(await ClosedAsync(s.NurseDestination, s.Resident, declarationId));
    }
}

file sealed class FixedTransferHistorySessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
