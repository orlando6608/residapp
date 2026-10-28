using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Domain.Supervision;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;
using static ResidApp.IntegrationTests.MedicinaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>Cierre excepcional de un episodio con indicaciones que nadie va a cumplir (script 0075; CJ, tercera 5.1): solo Dirección Clínica, con motivo
/// que queda en la auditoría con su nombre. Las indicaciones no se tocan.</summary>
public class CierreExcepcionalTests
{
    private const string Reason = "La residente fue trasladada a un hospital de otra provincia; ya nadie va a cumplir estas indicaciones.";

    private static DireccionApplicationService BuildDireccion(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory),
        new SqlSupervisionDirectory(TestDatabase.ConnectionFactory),
        new FixedCierreSessionIdentityProvider(externalSubject),
        new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory), new SqlProcessDeadlineRepository(TestDatabase.ConnectionFactory));

    private sealed record Setup(SeededProfile Enfermera, SeededProfile Medica, SeededProfile Direccion, Guid EventId, int Revision);

    /// <summary>Un evento escalado a Medicina con una indicación sin leer (el episodio queda «con indicación pendiente»), y Dirección Clínica en su unidad.</summary>
    private static async Task<Setup> SeedWithOpenIndicationAsync()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, enfermera.CenterId, enfermera.UnitId);
        var direccion = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId);
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).EscalateClinicalEventAsync(EscalateCommand(enfermera, eventId, revision))).Ok);
        var saved = await StartAndSaveMedicalAsync(medica, eventId);
        var indicated = await BuildMedicina(medica.ExternalSubject).RegisterMedicalIndicationAsync(Indication(medica, eventId, saved));
        Assert.True(indicated.Ok, indicated.Error?.Message);
        return new Setup(enfermera, medica, direccion, eventId, indicated.Value);
    }

    private static Task<ApplicationResult<bool>> CloseAsync(SeededProfile who, Guid eventId, string? reason = Reason) =>
        BuildDireccion(who.ExternalSubject).CloseEpisodeExceptionallyAsync(
            new CloseEpisodeExceptionallyCommand(who.ProfileScopeId, who.CenterId, eventId, reason));

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Sin resultado.");
    }

    private static Task<string> StateAsync(Guid eventId) =>
        ScalarAsync<string>("SELECT estado_codigo FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });

    [Fact]
    public async Task Direccion_CierraConMotivo_QuedaLaFilaYLaAuditoriaConSuNombre_YLasIndicacionesSeQuedanAbiertas()
    {
        var setup = await SeedWithOpenIndicationAsync();
        var before = (await BuildDireccion(setup.Direccion.ExternalSubject).FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(setup.Direccion.ProfileScopeId, setup.Direccion.CenterId, setup.EventId))).Value!;
        Assert.Equal(1, before.Episode.IndicationsPending);

        var closed = await CloseAsync(setup.Direccion, setup.EventId, "  " + Reason + "  ");

        Assert.True(closed.Ok, closed.Error?.Message);
        Assert.Equal("CERRADO", await StateAsync(setup.EventId));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(string Closure, string Communication, Guid ClosedBy)>(
            "SELECT cierre_sistema_codigo, comunicacion_familiar_codigo, cerrado_por_cuenta_id FROM dbo.eventos_asistenciales WHERE id = @id", new { id = setup.EventId });
        Assert.Equal(("DIRECCION_EXCEPCIONAL", "NO_COMUNICAR", setup.Direccion.AccountId.Value), row);
        var closure = await connection.QuerySingleAsync<(string Reason, int Indications, Guid ClosedBy)>(
            "SELECT motivo, indicaciones_abiertas, cerrado_por_cuenta_id FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @id", new { id = setup.EventId });
        Assert.Equal((Reason, 1, setup.Direccion.AccountId.Value), closure);
        var audit = await connection.QuerySingleAsync<(string Profile, string? Justification)>(
            "SELECT perfil_activo, justificacion FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @id AND accion_codigo = 'CLINICAL_EVENT_CLOSE_EXCEPTIONAL'", new { id = setup.EventId });
        Assert.Equal(("DIRECCION_CLINICA", Reason), audit);

        // Las indicaciones no se tocan: siguen sin leer, y Medicina las sigue viendo así.
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.indicaciones_medicas WHERE evento_id = @id AND estado_codigo = 'PENDIENTE_LECTURA'", new { id = setup.EventId }));
        var indications = (await BuildMedicina(setup.Medica.ExternalSubject).ListMedicalIndicationsAsync(
            new ListMedicalIndicationsCommand(setup.Medica.ProfileScopeId, setup.Medica.CenterId))).Value!;
        Assert.All(indications.Where(i => i.EventId == setup.EventId), i => Assert.Equal(MedicalIndicationStatus.PendienteLectura, i.Indication.Status));

        // Sale de los pendientes de Dirección y ya no se abre desde allí.
        var pending = (await BuildDireccion(setup.Direccion.ExternalSubject).ListPendingAsync(new ListSupervisionPendingQuery(
            setup.Direccion.ProfileScopeId, setup.Direccion.CenterId, DateOnly.FromDateTime(DateTime.Today)))).Value!;
        Assert.DoesNotContain(pending.Episodes, e => e.EventId == setup.EventId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await BuildDireccion(setup.Direccion.ExternalSubject).FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(setup.Direccion.ProfileScopeId, setup.Direccion.CenterId, setup.EventId))).Error!.Code);

        // La trazabilidad de Dirección la lleva con quién, su perfil y el motivo.
        var residentId = await ScalarAsync<Guid>("SELECT residente_id FROM dbo.eventos_asistenciales WHERE id = @id", new { id = setup.EventId });
        var traceability = await new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory).ListDirectionTraceabilityAsync(
            setup.Direccion.ProfileScopeId, setup.Direccion.CenterId, ResidentId.From(residentId));
        var entry = Assert.Single(traceability.Entries, e => e.Action == "CLINICAL_EVENT_CLOSE_EXCEPTIONAL");
        Assert.Equal((SystemProfile.DireccionClinica, Reason), (entry.ActorProfile, entry.Reason));
        Assert.All(traceability.Entries.Where(e => e.Action != "CLINICAL_EVENT_CLOSE_EXCEPTIONAL"), e => Assert.Null(e.Reason));
    }

    [Fact]
    public async Task SinMotivoOConMotivoDeMasDe300_SeRechaza_SinCerrarNada()
    {
        var setup = await SeedWithOpenIndicationAsync();

        var results = new[]
        {
            await CloseAsync(setup.Direccion, setup.EventId, null),
            await CloseAsync(setup.Direccion, setup.EventId, "   "),
            await CloseAsync(setup.Direccion, setup.EventId, new string('x', ExceptionalEpisodeClosure.MaxReasonLength + 1)),
        };

        Assert.All(results, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        Assert.Equal("CON_INDICACION_PENDIENTE", await StateAsync(setup.EventId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @id", new { id = setup.EventId }));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @id AND accion_codigo = 'CLINICAL_EVENT_CLOSE_EXCEPTIONAL'", new { id = setup.EventId }));
        // Con el motivo de 300 exactos sí cierra.
        Assert.True((await CloseAsync(setup.Direccion, setup.EventId, new string('x', ExceptionalEpisodeClosure.MaxReasonLength))).Ok);
    }

    [Fact]
    public async Task SoloCierraDireccionClinicaDelAmbito_Enfermeria_Medicina_OtroCentro_YUnEpisodioYaCerrado_NoPueden()
    {
        var setup = await SeedWithOpenIndicationAsync();
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica);

        var byNurse = await CloseAsync(setup.Enfermera, setup.EventId);
        var byMedicine = await CloseAsync(setup.Medica, setup.EventId);
        var otherCenterWithOwnScope = await CloseAsync(otherCenter, setup.EventId);
        // Con el ámbito de otro centro pero el evento de este: tampoco.
        var crossScope = await BuildDireccion(otherCenter.ExternalSubject).CloseEpisodeExceptionallyAsync(
            new CloseEpisodeExceptionallyCommand(setup.Direccion.ProfileScopeId, setup.Direccion.CenterId, setup.EventId, Reason));

        Assert.All(new[] { byNurse, byMedicine, otherCenterWithOwnScope, crossScope }, r => Assert.Equal(ApplicationFailureCode.AccessDenied, r.Error!.Code));
        Assert.Equal("CON_INDICACION_PENDIENTE", await StateAsync(setup.EventId));

        Assert.True((await CloseAsync(setup.Direccion, setup.EventId)).Ok);
        // Una segunda vez no encuentra un episodio abierto: mismo mensaje neutro, sin segunda fila.
        var again = await CloseAsync(setup.Direccion, setup.EventId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, again.Error!.Code);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @id", new { id = setup.EventId }));
    }

    [Fact]
    public async Task SinIndicacionesAbiertas_NoHayCierreExcepcional_NoEsUnCierreLibre()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var direccion = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, enfermera.CenterId, enfermera.UnitId);

        var result = await CloseAsync(direccion, eventId);

        Assert.Equal(ApplicationFailureCode.Conflict, result.Error!.Code);
        Assert.NotEqual("CERRADO", await StateAsync(eventId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @id", new { id = eventId }));
    }

    [Fact]
    public async Task EnfermeriaYMedicina_SiguenSinCerrarConIndicacionesSinCumplir()
    {
        var setup = await SeedWithOpenIndicationAsync();
        var revision = await ScalarAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @id", new { id = setup.EventId });

        var byNurse = await BuildService(setup.Enfermera.ExternalSubject).CloseClinicalEventAsync(new CloseClinicalEventCommand(
            setup.Enfermera.ProfileScopeId, setup.Enfermera.CenterId, setup.EventId, revision, Guid.NewGuid(), FamilyCommunicationDecision.NoComunicar, null, null));

        Assert.Equal(ApplicationFailureCode.InvalidInput, byNurse.Error!.Code);
        Assert.Equal("CON_INDICACION_PENDIENTE", await StateAsync(setup.EventId));
    }

    /// <summary>Una derivación a Urgencias no se cierra con el cierre excepcional de Dirección (CJ, Dirección y traslados 1.1; script 0079): ni la aplicación ni la base de datos.</summary>
    [Fact]
    public async Task UnaDerivacionAUrgencias_NoSeCierraConElCierreExcepcional_NiEnLaAplicacionNiEnLaBaseDeDatos()
    {
        // CJ (Dirección y traslados 1.1): con una derivación a Urgencias el comunicado relevante a la familia es obligatorio, y este cierre lleva NO_COMUNICAR.
        var setup = await SeedWithOpenIndicationAsync();
        var activated = await BuildMedicina(setup.Medica.ExternalSubject).ActivateUrgentProtocolAsync(
            new ActivateUrgentProtocolCommand(setup.Medica.ProfileScopeId, setup.Medica.CenterId, setup.EventId, setup.Revision, "Broncoespasmo."));
        Assert.True(activated.Ok, activated.Error?.Message);
        var direccion = BuildDireccion(setup.Direccion.ExternalSubject);
        Assert.True((await direccion.FindEpisodeAsync(
            new FindSupervisionEpisodeQuery(setup.Direccion.ProfileScopeId, setup.Direccion.CenterId, setup.EventId))).Value!.Episode.IndicationsPending > 0);

        var closed = await CloseAsync(setup.Direccion, setup.EventId);

        Assert.Equal(ApplicationFailureCode.Conflict, closed.Error!.Code);
        Assert.Equal("PROTOCOLO_URGENTE_MEDICO", await StateAsync(setup.EventId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @eventId", new { eventId = setup.EventId }));

        // Aunque alguien se saltara la aplicación, con la fila de esta misma transacción la base de datos tampoco lo cierra.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cierres_excepcionales_direccion (evento_id, centro_id, residente_id, motivo, indicaciones_abiertas, cerrado_por_cuenta_id, cerrado_en)
            SELECT id, centro_id, residente_id, N'Motivo', 1, @account, SYSUTCDATETIME() FROM dbo.eventos_asistenciales WHERE id = @id
            """, new { id = setup.EventId, account = setup.Direccion.AccountId.Value }, transaction);
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            UPDATE dbo.eventos_asistenciales
               SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @account, cerrado_en = SYSUTCDATETIME(),
                   comunicacion_familiar_codigo = 'NO_COMUNICAR', cierre_sistema_codigo = 'DIRECCION_EXCEPCIONAL'
             WHERE id = @id
            """, new { id = setup.EventId, account = setup.Direccion.AccountId.Value }, transaction));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", error.Message);
        transaction.Rollback();
    }

    /// <summary>La base de datos no deja cerrar «de sistema» por Dirección sin la fila del cierre excepcional de ESA transacción, ni cambiar o borrar la fila.</summary>
    [Fact]
    public async Task LaBaseDeDatos_SoloAdmiteElCierreExcepcionalConSuFilaEnLaMismaTransaccion_YLaFilaEsInmutable()
    {
        var setup = await SeedWithOpenIndicationAsync();
        const string closeSql = """
            UPDATE dbo.eventos_asistenciales
               SET estado_codigo = 'CERRADO', revision = revision + 1, cerrado_por_cuenta_id = @account, cerrado_en = SYSUTCDATETIME(),
                   comunicacion_familiar_codigo = 'NO_COMUNICAR', cierre_sistema_codigo = 'DIRECCION_EXCEPCIONAL'
             WHERE id = @id
            """;
        var parameters = new { id = setup.EventId, account = setup.Direccion.AccountId.Value };
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        // Sin fila: rechazado.
        var withoutRow = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(closeSql, parameters));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", withoutRow.Message);

        // Con la fila creada en OTRA transacción: rechazado.
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cierres_excepcionales_direccion (evento_id, centro_id, residente_id, motivo, indicaciones_abiertas, cerrado_por_cuenta_id, cerrado_en)
            SELECT id, centro_id, residente_id, N'Motivo', 1, @account, SYSUTCDATETIME() FROM dbo.eventos_asistenciales WHERE id = @id
            """, parameters);
        var otherTransaction = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(closeSql, parameters));
        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", otherTransaction.Message);
        Assert.Equal("CON_INDICACION_PENDIENTE", await StateAsync(setup.EventId));

        // La fila no se cambia ni se borra.
        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.cierres_excepcionales_direccion SET motivo = N'Otro' WHERE evento_id = @id", parameters));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.cierres_excepcionales_direccion WHERE evento_id = @id", parameters));
        Assert.Contains("EXCEPTIONAL_CLOSURE_IMMUTABLE", update.Message);
        Assert.Contains("EXCEPTIONAL_CLOSURE_IMMUTABLE", delete.Message);
    }
}

file sealed class FixedCierreSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
