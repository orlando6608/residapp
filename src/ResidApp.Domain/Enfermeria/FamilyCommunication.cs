using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>ENF-14: al cerrar un evento se decide explícitamente si se comunica a la familia.</summary>
public enum FamilyCommunicationDecision
{
    [Code("NO_COMUNICAR")] [Display(Name = "No comunicar")] NoComunicar,
    [Code("PREPARAR")] [Display(Name = "Preparar comunicación")] Preparar,
}

/// <summary>ENF-15 "tipo": la relevancia la decide el profesional, nunca el sistema (FAM-06). Una
/// publicación ordinaria permanece visible 30 días y una relevante 6 meses (FAM-07).</summary>
public enum FamilyCommunicationType
{
    [Code("ORDINARIA")] [Display(Name = "Ordinaria")] Ordinaria,
    [Code("RELEVANTE")] [Display(Name = "Relevante")] Relevante,
}

/// <summary>
/// Decisión de comunicación familiar al cerrar un evento (ENF-12, ENF-14, ENF-15). La decisión es
/// obligatoria; "preparar" exige tipo y un texto comprensible, y "no comunicar" no admite ninguno de los
/// dos. El texto no se publica aquí: queda pendiente de aprobación humana y se firma siempre como
/// <see cref="VisibleAuthor"/>.
/// </summary>
public sealed record FamilyCommunicationChoice
{
    public const int MaxTextLength = 2000;
    public const string VisibleAuthor = "Equipo asistencial del centro";

    public FamilyCommunicationDecision Decision { get; }
    public FamilyCommunicationType? Type { get; }
    public string? Text { get; }

    public FamilyCommunicationChoice(FamilyCommunicationDecision? decision, FamilyCommunicationType? type, string? text)
    {
        if (decision is null)
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_DECISION_REQUIRED");
        }
        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var valid = decision == FamilyCommunicationDecision.Preparar
            ? type is not null && text is { Length: <= MaxTextLength }
            : type is null && text is null;
        if (!valid)
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_INVALID");
        }

        Decision = decision.Value;
        Type = type;
        Text = text;
    }
}
