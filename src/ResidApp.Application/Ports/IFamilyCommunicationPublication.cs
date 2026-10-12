using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Quién publica o lista los comunicados a la familia: Administración (las unidades de su ámbito) o Dirección Clínica (sus unidades y
/// residentes, CJ 2026-10-10), con el ámbito concreto desde el que actúa.</summary>
public sealed record FamilyCommunicationAccess(Guid ProfileScopeId, AccountId AccountId, CenterId CenterId, SystemProfile Profile);

/// <summary>Un comunicado a la familia de una unidad del ámbito, con su estado de publicación (script 0048). ScheduledAt es la hora a la que
/// se publica solo (fijada al guardarlo, script 0051); PublishedEarlyAt es el momento en que se publicó antes de su hora; null si no se hizo.</summary>
/// <summary>Una publicación correctora de un comunicado ya publicado (script 0053): sale en el momento de guardarla.</summary>
public sealed record FamilyCommunicationCorrective(Guid Id, FamilyCommunicationType Type, string Text, DateTimeOffset PublishedAt);

/// <summary>La retirada de un comunicado publicado (script 0053). El motivo es interno: la familia no lo ve.</summary>
public sealed record FamilyCommunicationWithdrawalNote(string Reason, DateTimeOffset WithdrawnAt);

public sealed record AdministrationFamilyCommunication(
    Guid Id, Guid EventId, ResidentId ResidentId, string ResidentName, string? UnitName, FamilyCommunicationType Type, string Text,
    DateTimeOffset PreparedAt, DateTimeOffset? PublishedEarlyAt, DateTimeOffset ScheduledAt,
    IReadOnlyList<FamilyCommunicationCorrective> Correctives, FamilyCommunicationWithdrawalNote? Withdrawal);

public interface IFamilyCommunicationDirectory
{
    /// <summary>Los comunicados preparados desde <paramref name="since"/> sobre residentes del ámbito, del más reciente al más antiguo. No
    /// comprueba que quien mira sea Administración o Dirección Clínica: lo hace quien llama.</summary>
    Task<IReadOnlyList<AdministrationFamilyCommunication>> ListAsync(
        FamilyCommunicationAccess access, DateTimeOffset since, CancellationToken ct = default);
}

/// <summary>Publicación anticipada de un comunicado por Administración o Dirección Clínica, en una transacción que comprueba de nuevo el ámbito y
/// la auditoría (FAMILY_COMMUNICATION_PUBLISH_NOW, con el perfil que la hizo). Si el comunicado no existe o es de un residente ajeno,
/// AccessDenied; si ya estaba publicado (por su hora o por otra publicación anticipada), FAMILY_COMMUNICATION_ALREADY_PUBLISHED.</summary>
public interface IFamilyCommunicationPublisher
{
    Task PublishNowAsync(FamilyCommunicationAccess access, Guid communicationId, CancellationToken ct = default);
}

/// <summary>Corrección del texto y el tipo de un comunicado por Enfermería o Medicina del ámbito (script 0049; Medicina, CJ 2026-10-10).
/// ExpectedVersion es el número de correcciones que tenía cuando se abrió el formulario (0 es el original).</summary>
public sealed record FamilyCommunicationCorrectionInput(
    AccountId AccountId, Guid ProfileScopeId, CenterId CenterId, SystemProfile Profile, Guid CommunicationId, int ExpectedVersion,
    FamilyCommunicationType Type, string Text);

/// <summary>Corrige un comunicado dentro de una transacción que comprueba de nuevo el ámbito, que no se ha publicado todavía y la versión, y
/// deja su auditoría (FAMILY_COMMUNICATION_CORRECT, con el perfil que corrige). Fuera de ámbito o inexistente, AccessDenied; llegada su hora o
/// ya publicado, FAMILY_COMMUNICATION_CORRECTION_CLOSED; con otra versión vigente, FAMILY_COMMUNICATION_REVISION_CONFLICT; sin cambios,
/// FAMILY_COMMUNICATION_INVALID.</summary>
public interface IFamilyCommunicationCorrector
{
    Task CorrectAsync(FamilyCommunicationCorrectionInput input, CancellationToken ct = default);
}

/// <summary>Publicación correctora o retirada de un comunicado ya publicado, por Administración o Dirección Clínica (script 0053; portal-familiar.md,
/// historia 3). Cada operación, en una transacción que bloquea el comunicado y comprueba de nuevo el ámbito, que esté publicado y que no esté
/// retirado, y deja su auditoría sin datos (FAMILY_COMMUNICATION_CORRECTIVE o FAMILY_COMMUNICATION_WITHDRAW, con el perfil que la hizo).
/// Fuera de ámbito o inexistente, AccessDenied; sin publicar, FAMILY_COMMUNICATION_NOT_PUBLISHED; retirado, FAMILY_COMMUNICATION_WITHDRAWN.</summary>
public interface IFamilyCommunicationAmender
{
    Task PublishCorrectiveAsync(
        FamilyCommunicationAccess access, Guid communicationId, FamilyCommunicationType type, string text, CancellationToken ct = default);

    Task WithdrawAsync(FamilyCommunicationAccess access, Guid communicationId, string reason, CancellationToken ct = default);
}
