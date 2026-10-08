using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un comunicado a la familia de una unidad del ámbito de Administración, con su estado de publicación (script 0048).
/// PublishedEarlyAt es el momento en que Administración lo publicó antes de su hora; null si no lo hizo.</summary>
public sealed record AdministrationFamilyCommunication(
    Guid Id, Guid EventId, ResidentId ResidentId, string ResidentName, string? UnitName, FamilyCommunicationType Type, string Text,
    DateTimeOffset PreparedAt, DateTimeOffset? PublishedEarlyAt);

public interface IFamilyCommunicationDirectory
{
    /// <summary>Los comunicados preparados desde <paramref name="since"/> sobre residentes de las unidades del ámbito, del más reciente al
    /// más antiguo. No comprueba que quien mira sea Administración: lo hace quien llama.</summary>
    Task<IReadOnlyList<AdministrationFamilyCommunication>> ListAsync(
        AccountAdministrationAccess access, DateTimeOffset since, CancellationToken ct = default);
}

/// <summary>Publicación anticipada de un comunicado por Administración, en una transacción que comprueba de nuevo el ámbito y la
/// auditoría (FAMILY_COMMUNICATION_PUBLISH_NOW). Si el comunicado no existe o es de una unidad ajena, AccessDenied; si ya estaba publicado
/// (por su hora o por otra publicación anticipada), FAMILY_COMMUNICATION_ALREADY_PUBLISHED.</summary>
public interface IFamilyCommunicationPublisher
{
    Task PublishNowAsync(AccountAdministrationAccess access, Guid communicationId, CancellationToken ct = default);
}

/// <summary>Corrección del texto y el tipo de un comunicado por Enfermería del ámbito (script 0049). ExpectedVersion es el número de
/// correcciones que tenía cuando se abrió el formulario (0 es el original).</summary>
public sealed record FamilyCommunicationCorrectionInput(
    AccountId AccountId, Guid ProfileScopeId, CenterId CenterId, Guid CommunicationId, int ExpectedVersion, FamilyCommunicationType Type, string Text);

/// <summary>Corrige un comunicado dentro de una transacción que comprueba de nuevo el ámbito, el margen y la versión, y deja su auditoría
/// (FAMILY_COMMUNICATION_CORRECT). Fuera de ámbito o inexistente, AccessDenied; pasado el margen o ya publicado,
/// FAMILY_COMMUNICATION_CORRECTION_CLOSED; con otra versión vigente, FAMILY_COMMUNICATION_REVISION_CONFLICT; sin cambios,
/// FAMILY_COMMUNICATION_INVALID.</summary>
public interface IFamilyCommunicationCorrector
{
    Task CorrectAsync(FamilyCommunicationCorrectionInput input, CancellationToken ct = default);
}
