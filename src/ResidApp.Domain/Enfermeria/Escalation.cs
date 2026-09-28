using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>
/// ENF-09/ENF-10: motivo con el que Enfermería escala un evento a Medicina. Es obligatorio y lo escribe el
/// profesional: el sistema no genera ningún resumen ni decide nada al escalar.
/// </summary>
public sealed record EscalationReason
{
    public const int MaxLength = 2000;

    public string Text { get; }

    public EscalationReason(string? text)
    {
        Text = VitalSigns.Normalize(text) is { Length: <= MaxLength } normalized
            ? normalized
            : throw new DomainValidationException("CLINICAL_EVENT_ESCALATION_REASON_INVALID");
    }
}
