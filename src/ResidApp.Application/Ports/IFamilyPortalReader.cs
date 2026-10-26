using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>La cuenta de familiar que lee, ya comprobada contra su ámbito activo (perfil FAMILIAR).</summary>
public sealed record FamilyPortalAccess(Guid ProfileScopeId, AccountId AccountId, CenterId CenterId);

/// <summary>Un residente al que el familiar tiene acceso hoy. LinkId es el identificador del vínculo y es el único que viaja en las
/// direcciones del portal; no dice nada del residente. En la lista de residentes, NewCommunications son las novedades publicadas desde la última
/// visita de esta cuenta y ProposalAwaitingAnswer, si tiene una propuesta de cita viva que responder (CJ, tercera 6.3).</summary>
public sealed record FamilyPortalResident(
    Guid LinkId, string ResidentName, string Relationship, int NewCommunications = 0, bool ProposalAwaitingAnswer = false);

/// <summary>Lo que es una novedad para la familia (portal-familiar.md, historia 3): un comunicado publicado, una publicación correctora vinculada a
/// otro, o un comunicado retirado, del que solo se sabe que ya no está disponible.</summary>
public enum FamilyPortalCommunicationKind
{
    Original,
    Corrective,
    Withdrawn,
}

/// <summary>El comunicado que corrige una publicación correctora: su identificador y cuándo se publicó. La familia siempre puede abrirlo, porque la
/// correctora deja de verse con él (CJ, portal 4.2).</summary>
public sealed record FamilyPortalCorrection(Guid OriginalId, DateTimeOffset OriginalPublishedAt);

/// <summary>Una novedad publicada, tal como la ve la familia: tipo, texto vigente y cuándo se publicó. Nada del evento del que sale ni de quien
/// lo escribió (se firma siempre como FamilyCommunicationChoice.VisibleAuthor). Una retirada llega sin texto y sin tipo que mostrar: el tipo
/// solo decide cuánto tiempo sigue en la lista.</summary>
public sealed record FamilyPortalCommunication(
    Guid Id, FamilyCommunicationType Type, string Text, DateTimeOffset PublishedAt,
    FamilyPortalCommunicationKind Kind = FamilyPortalCommunicationKind.Original, FamilyPortalCorrection? Corrects = null)
{
    /// <summary>Nueva desde la visita indicada; una retirada nunca lo es (no hay nada que leer).</summary>
    public bool IsNewSince(DateTimeOffset? lastVisitAt) =>
        Kind != FamilyPortalCommunicationKind.Withdrawn && FamilyPortalVisibility.IsNewSince(PublishedAt, lastVisitAt);
}

/// <summary>Un comunicado con el residente al que pertenece (para el título de la pantalla) y, si es un original, las publicaciones correctoras
/// que la familia todavía puede ver, de la más antigua a la más reciente.</summary>
public sealed record FamilyPortalCommunicationDetail(
    FamilyPortalResident Resident, FamilyPortalCommunication Communication, IReadOnlyList<FamilyPortalCommunication> Corrections);

/// <summary>El resumen periódico (CJ, tercera 1.1 a 1.5): los comunicados ya publicados en el último periodo cerrado, del más antiguo al más reciente,
/// sin texto nuevo. Solo existe si hay alguno; no se guarda, se calcula al leer. Termina en PeriodEnd, el último corte del centro.</summary>
public sealed record FamilyPortalSummary(
    FamilyCommunicationSummaryFrequency Frequency, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd, IReadOnlyList<FamilyPortalCommunication> Items)
{
    /// <summary>Nuevo desde la visita indicada: se cerró después de la última vez que esta cuenta abrió al residente; si nunca lo abrió, lo es.</summary>
    public bool IsNewSince(DateTimeOffset? lastVisitAt) => FamilyPortalVisibility.IsNewSince(PeriodEnd, lastVisitAt);
}

/// <summary>El residente con sus novedades. PreviousVisitAt es la visita de esta cuenta anterior a esta (null si es la primera), para marcar las nuevas;
/// ProposalRequestId, la solicitud de la cuenta con una propuesta de cita viva que responder, la que caduca antes (CJ, tercera 6.3); Summary, el
/// resumen del último periodo cerrado, que se suma a los comunicados sueltos (null si en ese periodo no se publicó nada: entonces no hay resumen).</summary>
public sealed record FamilyPortalResidentHome(
    FamilyPortalResident Resident, IReadOnlyList<FamilyPortalCommunication> Communications, DateTimeOffset? PreviousVisitAt = null,
    Guid? ProposalRequestId = null, FamilyPortalSummary? Summary = null);

/// <summary>
/// Lecturas del Portal Familiar (script 0052). Todo sale de un solo criterio de acceso: la cuenta es la del familiar, su vínculo con el
/// residente no está desvinculado y su autorización es Activa y vigente hoy. Si no se cumple, no hay nada: sin distinguir un vínculo ajeno,
/// suspendido, revocado o inexistente. Cada lectura de un residente o de un comunicado se audita en la misma transacción.
/// </summary>
public interface IFamilyPortalReader
{
    /// <summary>Los residentes con lo que tienen nuevo para esta cuenta; no apunta ninguna visita.</summary>
    Task<IReadOnlyList<FamilyPortalResident>> ListResidentsAsync(
        FamilyPortalAccess access, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>El residente con sus comunicados publicados y dentro de su ventana, del más reciente al más antiguo; null si el vínculo no da acceso.
    /// Apunta la visita de la cuenta en la misma transacción que la auditoría.</summary>
    Task<FamilyPortalResidentHome?> ReadResidentAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Un comunicado publicado y visible del residente; null si no existe, no es de ese residente, no está publicado o ya salió de su ventana.</summary>
    Task<FamilyPortalCommunicationDetail?> ReadCommunicationAsync(
        FamilyPortalAccess access, Guid linkId, Guid communicationId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);
}
