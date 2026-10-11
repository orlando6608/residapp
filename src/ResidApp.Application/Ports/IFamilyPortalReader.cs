using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>La cuenta de familiar que lee, ya comprobada contra su ámbito activo (perfil FAMILIAR).</summary>
public sealed record FamilyPortalAccess(Guid ProfileScopeId, AccountId AccountId, CenterId CenterId);

/// <summary>Un residente al que el familiar tiene acceso hoy. LinkId es el identificador del vínculo y es el único que viaja en las
/// direcciones del portal; no dice nada del residente.</summary>
public sealed record FamilyPortalResident(Guid LinkId, string ResidentName, string Relationship);

/// <summary>Un comunicado publicado, tal como lo ve la familia: tipo, texto vigente y cuándo se publicó. Nada del evento del que sale ni de quien
/// lo escribió (se firma siempre como FamilyCommunicationChoice.VisibleAuthor).</summary>
public sealed record FamilyPortalCommunication(Guid Id, FamilyCommunicationType Type, string Text, DateTimeOffset PublishedAt);

/// <summary>Un comunicado con el residente al que pertenece (para el título de la pantalla).</summary>
public sealed record FamilyPortalCommunicationDetail(FamilyPortalResident Resident, FamilyPortalCommunication Communication);

public sealed record FamilyPortalResidentHome(FamilyPortalResident Resident, IReadOnlyList<FamilyPortalCommunication> Communications);

/// <summary>
/// Lecturas del Portal Familiar (script 0052). Todo sale de un solo criterio de acceso: la cuenta es la del familiar, su vínculo con el
/// residente no está desvinculado y su autorización es Activa y vigente hoy. Si no se cumple, no hay nada: sin distinguir un vínculo ajeno,
/// suspendido, revocado o inexistente. Cada lectura de un residente o de un comunicado se audita en la misma transacción.
/// </summary>
public interface IFamilyPortalReader
{
    Task<IReadOnlyList<FamilyPortalResident>> ListResidentsAsync(FamilyPortalAccess access, DateOnly today, CancellationToken ct = default);

    /// <summary>El residente con sus comunicados publicados y dentro de su ventana, del más reciente al más antiguo; null si el vínculo no da acceso.</summary>
    Task<FamilyPortalResidentHome?> ReadResidentAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Un comunicado publicado y visible del residente; null si no existe, no es de ese residente, no está publicado o ya salió de su ventana.</summary>
    Task<FamilyPortalCommunicationDetail?> ReadCommunicationAsync(
        FamilyPortalAccess access, Guid linkId, Guid communicationId, DateOnly today, DateTimeOffset now, CancellationToken ct = default);
}
