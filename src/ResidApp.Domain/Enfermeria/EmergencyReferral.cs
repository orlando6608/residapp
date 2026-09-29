using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>ENF-12/MED-14: lo que escribe el profesional en el informe de derivación a Urgencias, común a
/// Enfermería y Medicina (DER-01). El resto del informe son datos automáticos que no se editan: editar el
/// informe nunca altera los registros de origen (DER-02).</summary>
public sealed record ReferralReportInput
{
    public const int MaxReasonLength = 2000;
    public const int MaxAdditionalInformationLength = 4000;

    public string Reason { get; }
    public string? AdditionalInformation { get; }

    public ReferralReportInput(string? reason, string? additionalInformation)
    {
        reason = VitalSigns.Normalize(reason);
        additionalInformation = VitalSigns.Normalize(additionalInformation);
        if (reason is null || reason.Length > MaxReasonLength || additionalInformation is { Length: > MaxAdditionalInformationLength })
        {
            throw new DomainValidationException("REFERRAL_REPORT_INVALID");
        }

        Reason = reason;
        AdditionalInformation = additionalInformation;
    }
}

/// <summary>Una sección del informe: su título, sus líneas y si es un dato automático (reunido de los
/// registros de origen) o lo escribió el profesional.</summary>
public sealed record ReferralReportSection(string Title, bool Automatic, IReadOnlyList<string> Lines);

/// <summary>
/// DER-02/DER-03: el informe tal como se previsualiza y se firma: las secciones automáticas seguidas de las
/// que escribe el profesional. Hash es el SHA-256 de su JSON, en hexadecimal: la vista previa lo muestra y
/// la firma exige el mismo, así que no se firma nada distinto de lo que se revisó.
/// </summary>
public sealed record ReferralReportContent
{
    public const string ReasonTitle = "Motivo de la derivación";
    public const string AdditionalInformationTitle = "Información adicional para Urgencias";

    public IReadOnlyList<ReferralReportSection> Sections { get; }

    private ReferralReportContent(IReadOnlyList<ReferralReportSection> sections) => Sections = sections;

    public static ReferralReportContent Compose(IReadOnlyList<ReferralReportSection> automaticSections, ReferralReportInput input)
    {
        if (automaticSections.Count == 0 || automaticSections.Any(s => !s.Automatic))
        {
            throw new DomainValidationException("REFERRAL_REPORT_INVALID");
        }

        var sections = automaticSections.ToList();
        sections.Add(new ReferralReportSection(ReasonTitle, false, [input.Reason]));
        if (input.AdditionalInformation is not null)
        {
            sections.Add(new ReferralReportSection(AdditionalInformationTitle, false, [input.AdditionalInformation]));
        }
        return new ReferralReportContent(sections);
    }

    public string ToJson() => JsonSerializer.Serialize(Sections);

    public string Hash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson())));
}

/// <summary>DER-06: resultado de un intento de llamada al contacto familiar.</summary>
public enum FamilyCallResult
{
    [Code("CONTACTADO")] [Display(Name = "Contactado")] Contactado,
    [Code("NO_CONTESTA")] [Display(Name = "No contesta")] NoContesta,
    [Code("NUMERO_ERRONEO")] [Display(Name = "Número erróneo")] NumeroErroneo,
}

/// <summary>
/// DER-06: un intento de llamada al contacto familiar tras la derivación. A quién se llamó es texto libre
/// hasta que Administración tenga el contacto designado. La hora la escribe el profesional porque puede
/// documentarse después, pero nunca es futura (se admiten unos minutos de desfase entre relojes).
/// </summary>
public sealed record FamilyCallAttempt
{
    public const int MaxContactLength = 200;
    public const int MaxNoteLength = 2000;

    public string Contact { get; }
    public DateTimeOffset CalledAt { get; }
    public FamilyCallResult Result { get; }
    public string? Note { get; }

    private FamilyCallAttempt(string contact, DateTimeOffset calledAt, FamilyCallResult result, string? note)
    {
        Contact = contact;
        CalledAt = calledAt;
        Result = result;
        Note = note;
    }

    public static FamilyCallAttempt Create(
        string? contact, DateTimeOffset? calledAt, FamilyCallResult? result, string? note, DateTimeOffset now)
    {
        contact = VitalSigns.Normalize(contact);
        note = VitalSigns.Normalize(note);
        if (contact is null || contact.Length > MaxContactLength || note is { Length: > MaxNoteLength }
            || calledAt is not { } at || at > now + UrgentProtocolEntry.ClockTolerance
            || result is not { } value || !Enum.IsDefined(value))
        {
            throw new DomainValidationException("FAMILY_CALL_ATTEMPT_INVALID");
        }

        return new FamilyCallAttempt(contact, at, value, note);
    }
}
