using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. Historial, bloque 3 (COR-01/COR-02, script 0020): el autor
/// corrige su valoración de Enfermería dentro de la ventana o la rectifica fuera de ella, y la BD impide cambiar
/// una valoración cerrada sin su corrección trazada.</summary>
public class CorreccionTests
{
    private const string OriginalFindings = "Crepitantes en base derecha.";
    private const string CorrectedFindings = "Crepitantes en base izquierda.";
    private const string Reason = "Hallazgo anotado en el lado equivocado.";

    [Fact]
    public async Task Correccion_DentroDeLaVentana_ElAutorCorrigeLaValoracionCerrada_YQuedaTrazada()
    {
        var (enfermera, companera, eventId) = await SeedClosedAsync();
        var service = BuildService(enfermera.ExternalSubject);

        var before = await FindDetailAsync(enfermera, eventId);
        Assert.True(before.Assessment!.Amendments!.AuthoredByCurrentAccount);
        Assert.Empty(before.Assessment.Amendments.Corrections);
        Assert.False((await FindDetailAsync(companera, eventId)).Assessment!.Amendments!.AuthoredByCurrentAccount);

        var result = await service.CorrectNursingAssessmentAsync(Correct(enfermera, eventId, 0));
        Assert.True(result.Ok, result.Error?.Message);

        var after = await FindDetailAsync(enfermera, eventId);
        Assert.Equal(ClinicalEventStatus.Cerrado, after.Status);
        Assert.Equal(CorrectedFindings, after.Assessment!.Content.Findings);
        Assert.Equal(Reason, Assert.Single(after.Assessment.Amendments!.Corrections).Reason);
        Assert.Empty(after.Assessment.Amendments.Rectifications);
        Assert.Equal(1, await CountAuditAsync(eventId, "NURSING_ASSESSMENT_CORRECT"));

        // El mismo formulario reenviado no duplica la corrección.
        Assert.Equal(ApplicationFailureCode.Conflict, (await service.CorrectNursingAssessmentAsync(Correct(enfermera, eventId, 0))).Error!.Code);
        // Sin motivo no hay corrección, y dentro de la ventana no se rectifica: se corrige.
        Assert.Equal(ApplicationFailureCode.InvalidInput,
            (await service.CorrectNursingAssessmentAsync(Correct(enfermera, eventId, 1, reason: "  "))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await service.RectifyAssessmentAsync(Rectify(enfermera, eventId, 0))).Error!.Code);
        // Otra enfermera de la unidad ve la valoración, pero no es su autora.
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await BuildService(companera.ExternalSubject).CorrectNursingAssessmentAsync(Correct(companera, eventId, 1))).Error!.Code);

        // La línea temporal conserva la versión original y la corrección con su motivo.
        var timeline = await service.ReadResidentTimelineAsync(
            new ReadResidentTimelineCommand(enfermera.ProfileScopeId, enfermera.CenterId, after.ResidentId, SystemProfile.Enfermeria));
        Assert.True(timeline.Ok, timeline.Error?.Message);
        Assert.Equal(OriginalFindings, Assert.Single(timeline.Value!.OfType<TimelineEntry.NursingAssessmentSaved>()).Content.Findings);
        var corrected = Assert.Single(timeline.Value!.OfType<TimelineEntry.NursingAssessmentCorrected>());
        Assert.Equal(CorrectedFindings, corrected.Content.Findings);
        Assert.Equal(Reason, corrected.Reason);
    }

    [Fact]
    public async Task Correccion_MientrasLaValoracionSeGuardaDeFormaNormal_NoSeOfrece()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        await StartAndSaveAsync(enfermera, eventId);

        Assert.Equal(ApplicationFailureCode.InvalidInput,
            (await BuildService(enfermera.ExternalSubject).CorrectNursingAssessmentAsync(Correct(enfermera, eventId, 0))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput,
            (await BuildService(enfermera.ExternalSubject, TimeSpan.Zero).RectifyAssessmentAsync(Rectify(enfermera, eventId, 0))).Error!.Code);
    }

    [Fact]
    public async Task Rectificacion_FueraDeLaVentana_SeAnadeSinCambiarLaValoracion()
    {
        var (enfermera, companera, eventId) = await SeedClosedAsync();
        var service = BuildService(enfermera.ExternalSubject, TimeSpan.Zero);

        Assert.Equal(ApplicationFailureCode.InvalidInput, (await service.CorrectNursingAssessmentAsync(Correct(enfermera, eventId, 0))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput,
            (await service.RectifyAssessmentAsync(Rectify(enfermera, eventId, 0, text: " "))).Error!.Code);
        var result = await service.RectifyAssessmentAsync(Rectify(enfermera, eventId, 0));
        Assert.True(result.Ok, result.Error?.Message);

        var detail = await FindDetailAsync(enfermera, eventId);
        Assert.Equal(OriginalFindings, detail.Assessment!.Content.Findings);
        Assert.Empty(detail.Assessment.Amendments!.Corrections);
        var rectification = Assert.Single(detail.Assessment.Amendments.Rectifications);
        Assert.Equal("La crepitación era en la base izquierda.", rectification.Text);
        Assert.Equal(Reason, rectification.Reason);
        Assert.Equal(1, await CountAuditAsync(eventId, "ASSESSMENT_RECTIFY"));

        Assert.Equal(ApplicationFailureCode.Conflict, (await service.RectifyAssessmentAsync(Rectify(enfermera, eventId, 0))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied,
            (await BuildService(companera.ExternalSubject, TimeSpan.Zero).RectifyAssessmentAsync(Rectify(companera, eventId, 1))).Error!.Code);

        var timeline = await service.ReadResidentTimelineAsync(
            new ReadResidentTimelineCommand(enfermera.ProfileScopeId, enfermera.CenterId, detail.ResidentId, SystemProfile.Enfermeria));
        var entry = Assert.Single(timeline.Value!.OfType<TimelineEntry.AssessmentRectified>());
        Assert.Equal(SystemProfile.Enfermeria, entry.Profile);
        Assert.Equal("La crepitación era en la base izquierda.", entry.Text);
    }

    [Fact]
    public async Task LaBaseDeDatos_SoloDejaCambiarUnaValoracionCerradaConSuCorreccion_YLasConservaInmutables()
    {
        var (enfermera, _, eventId) = await SeedClosedAsync();
        Assert.True((await BuildService(enfermera.ExternalSubject).CorrectNursingAssessmentAsync(Correct(enfermera, eventId, 0))).Ok);
        Assert.True((await BuildService(enfermera.ExternalSubject, TimeSpan.Zero).RectifyAssessmentAsync(Rectify(enfermera, eventId, 0))).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        foreach (var (sql, expected) in new[]
        {
            ("UPDATE dbo.valoraciones_enfermeria SET hallazgos = 'x', actualizado_en = SYSUTCDATETIME() WHERE evento_id = @EventId", "NURSING_ASSESSMENT_IMMUTABLE"),
            ("UPDATE dbo.valoraciones_enfermeria SET hallazgos = 'x' WHERE evento_id = @EventId", "NURSING_ASSESSMENT_IMMUTABLE"),
            ("UPDATE dbo.valoraciones_enfermeria SET estado_codigo = 'BORRADOR' WHERE evento_id = @EventId", "NURSING_ASSESSMENT_IMMUTABLE"),
            ("UPDATE dbo.valoraciones_enfermeria_correcciones SET motivo = 'x' WHERE evento_id = @EventId", "NURSING_ASSESSMENT_CORRECTION_IMMUTABLE"),
            ("DELETE FROM dbo.valoraciones_enfermeria_correcciones WHERE evento_id = @EventId", "NURSING_ASSESSMENT_CORRECTION_IMMUTABLE"),
            ("UPDATE dbo.valoraciones_rectificaciones SET texto = 'x' WHERE evento_id = @EventId", "ASSESSMENT_RECTIFICATION_IMMUTABLE"),
            ("DELETE FROM dbo.valoraciones_rectificaciones WHERE evento_id = @EventId", "ASSESSMENT_RECTIFICATION_IMMUTABLE"),
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { EventId = eventId }));
            Assert.Contains(expected, ex.Message);
        }
    }

    /// <summary>Un evento propio de Enfermería valorado y cerrado por la primera enfermera.</summary>
    private static async Task<(SeededProfile Enfermera, SeededProfile Companera, Guid EventId)> SeedClosedAsync()
    {
        var (enfermera, companera, eventId) = await SeedOwnEventAsync();
        var revision = await StartAndSaveAsync(enfermera, eventId);
        Assert.True((await BuildService(enfermera.ExternalSubject).CloseClinicalEventAsync(new CloseClinicalEventCommand(
            enfermera.ProfileScopeId, enfermera.CenterId, eventId, revision, Guid.NewGuid(),
            FamilyCommunicationDecision.NoComunicar, null, null))).Ok);
        return (enfermera, companera, eventId);
    }

    private static CorrectNursingAssessmentCommand Correct(SeededProfile seed, Guid eventId, int corrections, string? reason = Reason) =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, corrections, reason, CorrectedFindings, null, "Se incorpora a 45º.", null, null,
            37.8m, 130, 80, 92, 22, 93, RespiratorySupportCode.AireAmbiente, null, null, null, null, null);

    private static RectifyAssessmentCommand Rectify(
        SeededProfile seed, Guid eventId, int rectifications, string? text = "La crepitación era en la base izquierda.") =>
        new(seed.ProfileScopeId, seed.CenterId, eventId, rectifications, text, Reason, SystemProfile.Enfermeria);

    private static async Task<PendingChangeDetail> FindDetailAsync(SeededProfile seed, Guid eventId) =>
        (await BuildService(seed.ExternalSubject).FindPendingChangeDetailAsync(
            new FindPendingChangeDetailCommand(seed.ProfileScopeId, seed.CenterId, eventId))).Value!;
}
