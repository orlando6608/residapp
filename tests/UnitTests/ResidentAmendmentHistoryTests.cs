using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>DIR-15: el agrupado de correcciones y rectificaciones de la línea temporal por evento y tipo de valoración.</summary>
public class ResidentAmendmentHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Corrected = Guid.NewGuid();
    private static readonly Guid Untouched = Guid.NewGuid();

    private static NursingAssessmentContent Content(string findings) =>
        new(findings, null, null, null, null, new VitalSigns(null, null, null, null, null, null, null, null, null, null, null, null));

    [Fact]
    public void From_AgrupaPorEventoYValoracion_ConElOriginalLasCorreccionesYLasRectificaciones()
    {
        var timeline = new TimelineEntry[]
        {
            new TimelineEntry.AssessmentRectified(T0.AddHours(30), Corrected, SystemProfile.Enfermeria, "Era la base izquierda.", "Lado equivocado."),
            new TimelineEntry.NursingAssessmentCorrected(T0.AddHours(3), Corrected, Content("Segunda."), "Segundo ajuste."),
            new TimelineEntry.NursingAssessmentCorrected(T0.AddHours(2), Corrected, Content("Primera."), "Primer ajuste."),
            new TimelineEntry.NursingAssessmentSaved(T0.AddHours(1), Corrected, Content("Final al cerrar.")),
            new TimelineEntry.NursingAssessmentSaved(T0, Corrected, Content("Borrador.")),
            new TimelineEntry.NursingAssessmentSaved(T0, Untouched, Content("Sin cambios.")),
            new TimelineEntry.EventRegistered(T0, Corrected, SystemProfile.Enfermeria, ClinicalEventOrigin.EventoEnfermeria,
                DailyChangeClassification.Ordinario, "Caída.", []),
        };

        var history = ResidentAmendmentHistory.From(timeline, []);

        var item = Assert.Single(history.Assessments);
        Assert.Equal(Corrected, item.EventId);
        Assert.Equal(SystemProfile.Enfermeria, item.Profile);
        Assert.Equal("Final al cerrar.", ((TimelineEntry.NursingAssessmentSaved)item.Original!).Content.Findings);
        Assert.Equal(["Primer ajuste.", "Segundo ajuste."], item.Corrections.Cast<TimelineEntry.NursingAssessmentCorrected>().Select(c => c.Reason));
        Assert.Equal("Lado equivocado.", Assert.Single(item.Rectifications).Reason);
        Assert.NotNull(item.Event);
        Assert.Equal(T0.AddHours(30), item.LastChangeAt);
    }

    [Fact]
    public void From_SinCorreccionesNiRectificaciones_NoMuestraValoraciones_PeroConservaLosBasales()
    {
        var baselines = new[]
        {
            new BaselineHistoryEntry(2, BaselineReason.RevisionProgramada, SystemProfile.Enfermeria, T0, 90, true, 1),
            new BaselineHistoryEntry(1, BaselineReason.Alta, SystemProfile.Enfermeria, T0.AddDays(-30), 100, false, null),
        };

        var history = ResidentAmendmentHistory.From(
            [new TimelineEntry.NursingAssessmentSaved(T0, Untouched, Content("Sin cambios."))], baselines);

        Assert.Empty(history.Assessments);
        Assert.Equal([2, 1], history.Baselines.Select(b => b.VersionNumber));
        Assert.Equal(1, history.Baselines[0].ReplacesVersionNumber);
    }

    [Fact]
    public void From_LaMismaCorreccionDeEnfermeriaYDeMedicina_SonDosValoracionesDistintas()
    {
        var timeline = new TimelineEntry[]
        {
            new TimelineEntry.AssessmentRectified(T0, Corrected, SystemProfile.Enfermeria, "A", "B"),
            new TimelineEntry.AssessmentRectified(T0.AddHours(1), Corrected, SystemProfile.Medicina, "C", "D"),
        };

        var history = ResidentAmendmentHistory.From(timeline, []);

        Assert.Equal([SystemProfile.Medicina, SystemProfile.Enfermeria], history.Assessments.Select(a => a.Profile));
    }
}
