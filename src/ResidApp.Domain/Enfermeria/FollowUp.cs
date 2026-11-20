using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Enfermeria;

/// <summary>
/// ENF-07B: plan de un seguimiento, con una fecha prevista o un criterio (o ambos), y una hora prevista opcional si hay fecha. Sin ni
/// fecha ni criterio no hay seguimiento. Un seguimiento con fecha vence cuando esa fecha ya ha pasado; con hora, cuando ya ha pasado esa
/// hora (hora local del centro, Madrid). Vencer no lo cierra ni lo oculta. Uno que solo tiene criterio no vence.
/// </summary>
public sealed record FollowUpPlan
{
    public const int MaxCriterionLength = 1000;

    public DateOnly? DueDate { get; }
    /// <summary>Hora prevista, al minuto, solo con fecha. Es orientativa: no garantiza que se haga a esa hora.</summary>
    public TimeOnly? DueTime { get; }
    public string? Criterion { get; }

    public FollowUpPlan(DateOnly? dueDate, string? criterion, TimeOnly? dueTime = null)
    {
        criterion = VitalSigns.Normalize(criterion);
        if (dueDate is null && criterion is null)
        {
            throw new DomainValidationException("FOLLOW_UP_PLAN_REQUIRED");
        }
        if (criterion is { Length: > MaxCriterionLength } || (dueTime is not null && dueDate is null))
        {
            throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID");
        }

        DueDate = dueDate;
        DueTime = dueTime is { } time ? new TimeOnly(time.Hour, time.Minute) : null;
        Criterion = criterion;
    }

    /// <summary>Sin hora, vence al día siguiente de la fecha.</summary>
    public bool IsOverdue(DateOnly today) => DueDate < today;

    /// <summary>Con hora, vence cuando <paramref name="now"/> (hora local del centro) pasa de esa hora; sin hora, al día siguiente.</summary>
    public bool IsOverdue(DateTime now) => IsOverdue(DueDate, DueTime, now);

    /// <summary>La misma regla para quien solo tiene la fecha y la hora sueltas (listas y pantallas).</summary>
    public static bool IsOverdue(DateOnly? dueDate, TimeOnly? dueTime, DateTime now) => dueDate switch
    {
        null => false,
        { } date when dueTime is { } time => date.ToDateTime(time) < now,
        { } date => date < DateOnly.FromDateTime(now),
    };
}

/// <summary>ENF-08/ENF-09 y MED-11/MED-12: tipos de acción sobre un seguimiento abierto. Conservación solo
/// existe en el seguimiento médico (MED-12: conservarlo para la propia próxima revisión).</summary>
public enum FollowUpActionType
{
    [Code("ACTUACION")] [Display(Name = "Actuación")] Actuacion,
    [Code("REPROGRAMACION")] [Display(Name = "Reprogramación")] Reprogramacion,
    [Code("TRANSFERENCIA")] [Display(Name = "Transferencia de turno")] Transferencia,
    [Code("RECEPCION")] [Display(Name = "Recepción de la transferencia")] Recepcion,
    [Code("CONSERVACION")] [Display(Name = "Conservado para la próxima revisión")] Conservacion,
}

/// <summary>
/// Una acción sobre un seguimiento abierto (ENF-08/ENF-09): registrar una actuación, reprogramar con
/// justificación, transferir al equipo o turno entrante con una nota de continuidad, o confirmar la
/// recepción de una transferencia. Cada una conserva su autoría al guardarse.
/// </summary>
public sealed record FollowUpAction
{
    public const int MaxTextLength = 2000;

    public FollowUpActionType Type { get; }
    public string? Text { get; }
    public FollowUpPlan? Plan { get; }
    /// <summary>El equipo entrante de una transferencia: el id de un equipo (script 0028); su nombre lo copia la base al guardar.</summary>
    public Guid? IncomingTeamId { get; }
    public Guid? TransferId { get; }

    private FollowUpAction(FollowUpActionType type, string? text, FollowUpPlan? plan, Guid? incomingTeamId, Guid? transferId)
    {
        if (text is { Length: > MaxTextLength })
        {
            throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID");
        }

        Type = type;
        Text = text;
        Plan = plan;
        IncomingTeamId = incomingTeamId;
        TransferId = transferId;
    }

    public static FollowUpAction Note(string? text) =>
        new(FollowUpActionType.Actuacion, Required(text), null, null, null);

    /// <summary>"Reprogramar justificadamente": el plan nuevo sustituye al vigente y la justificación es obligatoria.</summary>
    public static FollowUpAction Reschedule(FollowUpPlan plan, string? justification) =>
        new(FollowUpActionType.Reprogramacion, Required(justification), plan, null, null);

    public static FollowUpAction Transfer(Guid? incomingTeamId, string? note) =>
        new(FollowUpActionType.Transferencia, VitalSigns.Normalize(note), null,
            incomingTeamId is { } id && id != Guid.Empty ? id : throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID"), null);

    /// <summary>MED-12 "conservar para mi próxima revisión", con una nota opcional.</summary>
    public static FollowUpAction Keep(string? note) =>
        new(FollowUpActionType.Conservacion, VitalSigns.Normalize(note), null, null, null);

    public static FollowUpAction Receive(Guid transferId) =>
        transferId == Guid.Empty
            ? throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID")
            : new(FollowUpActionType.Recepcion, null, null, null, transferId);

    private static string Required(string? text) =>
        VitalSigns.Normalize(text) ?? throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID");
}
