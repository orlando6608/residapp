using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>ENF-11/MED-13: activación del protocolo urgente, común a Enfermería y Medicina. Activar no
/// retrasa la atención: solo admite una nota opcional; profesional y hora los pone el servidor.</summary>
public sealed record UrgentProtocolActivation
{
    public const int MaxNoteLength = 2000;

    public string? Note { get; }

    public UrgentProtocolActivation(string? note)
    {
        note = VitalSigns.Normalize(note);
        if (note is { Length: > MaxNoteLength })
        {
            throw new DomainValidationException("URGENT_PROTOCOL_ACTIVATION_INVALID");
        }

        Note = note;
    }
}

/// <summary>ENF-11/MED-13: tipos de registro dentro de un protocolo urgente activo.</summary>
public enum UrgentProtocolEntryType
{
    [Code("ACTUACION")] [Display(Name = "Actuación")] Actuacion,
    [Code("EVOLUCION")] [Display(Name = "Evolución")] Evolucion,
    [Code("CONTACTO")] [Display(Name = "Contacto con un servicio")] Contacto,
}

/// <summary>
/// Un registro del protocolo urgente, con su autoría al guardarse: una actuación, la evolución del residente
/// o un contacto con un servicio (DER-04: servicio y hora del contacto, solo en la trazabilidad interna). La
/// hora del contacto la escribe el profesional porque puede documentarse después, pero nunca es futura (se
/// admiten unos minutos de desfase entre relojes).
/// </summary>
public sealed record UrgentProtocolEntry
{
    public const int MaxTextLength = 2000;
    public const int MaxServiceLength = 200;
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    public UrgentProtocolEntryType Type { get; }
    public string? Text { get; }
    public string? Service { get; }
    public DateTimeOffset? ContactedAt { get; }

    private UrgentProtocolEntry(UrgentProtocolEntryType type, string? text, string? service, DateTimeOffset? contactedAt)
    {
        if (text is { Length: > MaxTextLength } || service is { Length: > MaxServiceLength })
        {
            throw new DomainValidationException("URGENT_PROTOCOL_ENTRY_INVALID");
        }

        Type = type;
        Text = text;
        Service = service;
        ContactedAt = contactedAt;
    }

    public static UrgentProtocolEntry Action(string? text) => new(UrgentProtocolEntryType.Actuacion, Required(text), null, null);

    public static UrgentProtocolEntry Evolution(string? text) => new(UrgentProtocolEntryType.Evolucion, Required(text), null, null);

    public static UrgentProtocolEntry Contact(string? service, DateTimeOffset? contactedAt, string? note, DateTimeOffset now) =>
        contactedAt is not { } at || at > now + ClockTolerance
            ? throw new DomainValidationException("URGENT_PROTOCOL_ENTRY_INVALID")
            : new(UrgentProtocolEntryType.Contacto, VitalSigns.Normalize(note), Required(service), at);

    private static string Required(string? text) =>
        VitalSigns.Normalize(text) ?? throw new DomainValidationException("URGENT_PROTOCOL_ENTRY_INVALID");
}
