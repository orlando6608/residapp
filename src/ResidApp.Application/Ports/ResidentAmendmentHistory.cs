using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>DIR-15: las valoraciones de un residente que se corrigieron o rectificaron, con la versión original, cada corrección
/// (contenido corregido y motivo) y cada rectificación, y todos los basales firmados como versiones vinculadas (cada una sustituye a la
/// anterior; un basal firmado no se edita). Solo lectura. El autor es el perfil que lo hizo: las cuentas no tienen nombre en la
/// línea temporal, y solo el autor de una valoración puede corregirla o rectificarla.</summary>
public sealed record ResidentAmendmentHistory(
    IReadOnlyList<AmendedAssessment> Assessments, IReadOnlyList<BaselineHistoryEntry> Baselines)
{
    /// <summary>Agrupa los hitos de la línea temporal por evento y tipo de valoración. Una valoración sin correcciones ni
    /// rectificaciones no aparece. Primero la más recientemente modificada.</summary>
    public static ResidentAmendmentHistory From(IReadOnlyList<TimelineEntry> timeline, IReadOnlyList<BaselineHistoryEntry> baselines)
    {
        static bool IsCorrection(TimelineEntry e) => e is TimelineEntry.NursingAssessmentCorrected or TimelineEntry.MedicalAssessmentCorrected;
        static bool IsSaved(TimelineEntry e) => e is TimelineEntry.NursingAssessmentSaved or TimelineEntry.MedicalAssessmentSaved;

        var assessments = timeline
            .Where(e => e.EventId is not null && e.Profile is SystemProfile.Enfermeria or SystemProfile.Medicina
                && (IsCorrection(e) || e is TimelineEntry.AssessmentRectified))
            .GroupBy(e => (EventId: e.EventId!.Value, Profile: e.Profile!.Value))
            .Select(group =>
            {
                var forAssessment = timeline.Where(e => e.EventId == group.Key.EventId && e.Profile == group.Key.Profile).ToList();
                return new AmendedAssessment(
                    group.Key.EventId, group.Key.Profile,
                    timeline.OfType<TimelineEntry.EventRegistered>().FirstOrDefault(e => e.EventId == group.Key.EventId),
                    forAssessment.Where(IsSaved).OrderByDescending(e => e.At).FirstOrDefault(),
                    forAssessment.Where(IsCorrection).OrderBy(e => e.At).ToList(),
                    forAssessment.OfType<TimelineEntry.AssessmentRectified>().OrderBy(e => e.At).ToList());
            })
            .OrderByDescending(a => a.LastChangeAt)
            .ToList();
        return new ResidentAmendmentHistory(assessments, baselines);
    }
}

/// <summary>Una valoración (de Enfermería o de Medicina) de un evento con sus correcciones y rectificaciones. Original es la última
/// versión guardada, la que había al cerrarse; Event, el evento registrado (null si no es visible).</summary>
public sealed record AmendedAssessment(
    Guid EventId, SystemProfile Profile, TimelineEntry.EventRegistered? Event, TimelineEntry? Original,
    IReadOnlyList<TimelineEntry> Corrections, IReadOnlyList<TimelineEntry.AssessmentRectified> Rectifications)
{
    public DateTimeOffset LastChangeAt => Corrections.Select(c => c.At).Concat(Rectifications.Select(r => r.At)).Max();
}
