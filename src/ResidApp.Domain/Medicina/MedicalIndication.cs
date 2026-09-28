using System.ComponentModel.DataAnnotations;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Domain.Medicina;

/// <summary>ENF-10/MED-08: estado de una indicación médica. Lectura y realización son hitos distintos; una
/// indicación no caduca: sigue visible hasta que se registra como realizada o no realizada.</summary>
public enum MedicalIndicationStatus
{
    [Code("PENDIENTE_LECTURA")] [Display(Name = "Pendiente de lectura")] PendienteLectura,
    [Code("LEIDA")] [Display(Name = "Leída")] Leida,
    [Code("REALIZADA")] [Display(Name = "Realizada")] Realizada,
    [Code("NO_REALIZADA")] [Display(Name = "No realizada")] NoRealizada,
}

/// <summary>
/// MED-07: indicación de Medicina a Enfermería, con texto, fecha prevista o criterio (FollowUpPlan: al menos
/// uno de los dos) e información adicional. No hay selector de prioridad: el sistema no la infiere.
/// </summary>
public sealed record MedicalIndication
{
    public const int MaxTextLength = 2000;

    public string Text { get; }
    public FollowUpPlan Plan { get; }
    public string? AdditionalInformation { get; }

    public MedicalIndication(string? text, FollowUpPlan plan, string? additionalInformation)
    {
        text = VitalSigns.Normalize(text);
        additionalInformation = VitalSigns.Normalize(additionalInformation);
        if (text is null || text.Length > MaxTextLength || additionalInformation is { Length: > MaxTextLength })
        {
            throw new DomainValidationException("MEDICAL_INDICATION_INVALID");
        }

        Text = text;
        Plan = plan;
        AdditionalInformation = additionalInformation;
    }
}

/// <summary>ENF-10: resultado que registra Enfermería sobre una indicación ya leída. "No realizada" exige
/// describir la incidencia.</summary>
public sealed record MedicalIndicationOutcome
{
    public bool Done { get; }
    public string? Incident { get; }

    public MedicalIndicationOutcome(bool done, string? incident)
    {
        incident = VitalSigns.Normalize(incident);
        var valid = done ? incident is null : incident is { Length: <= MedicalIndication.MaxTextLength };
        if (!valid)
        {
            throw new DomainValidationException("MEDICAL_INDICATION_INCIDENT_INVALID");
        }

        Done = done;
        Incident = incident;
    }
}
