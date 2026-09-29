using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Domain.Medicina;

/// <summary>
/// MED-10 "crear seguimiento médico": fecha prevista o criterio (FollowUpPlan: al menos uno de los dos) y el
/// objetivo del seguimiento, obligatorio. El equipo responsable es Medicina de la unidad del evento. El sistema
/// no decide qué resultado es necesario: lo escribe el médico en el objetivo o el criterio.
/// </summary>
public sealed record MedicalFollowUp
{
    public const int MaxObjectiveLength = 1000;

    public FollowUpPlan Plan { get; }
    public string Objective { get; }

    public MedicalFollowUp(FollowUpPlan plan, string? objective)
    {
        objective = VitalSigns.Normalize(objective);
        if (objective is null || objective.Length > MaxObjectiveLength)
        {
            throw new DomainValidationException("MEDICAL_FOLLOW_UP_OBJECTIVE_REQUIRED");
        }

        Plan = plan;
        Objective = objective;
    }
}
