using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente sobre el que el caso de uso ya ha comprobado el ámbito del perfil (ResidentPhotoApplicationService), con la cuenta,
/// el ámbito y el perfil que firman el cambio y la unidad en la que está.</summary>
public sealed record ResidentPhotoTarget(
    AccountId AccountId, Guid ProfileScopeId, SystemProfile Profile, CenterId CenterId, UnitId UnitId, ResidentId ResidentId);

/// <summary>La foto de un residente sin la imagen: cuándo se puso y con qué consentimiento (null si no tiene), y si está anotado que no quiere
/// foto.</summary>
public sealed record ResidentPhotoStatus(DateTimeOffset? UpdatedAt, ResidentPhotoConsent? Consent, bool Refused);

/// <summary>
/// Foto de identificación del residente (scripts 0059 y 0071): una por residente, con el consentimiento (quién lo firmó y cuándo). Ponerla
/// sustituye la anterior y quitarla la borra; «No quiere foto» la borra e impide poner otra hasta quitar la marca (RESIDENT_PHOTO_REFUSED). Cada
/// cambio deja RESIDENT_PHOTO_SET, RESIDENT_PHOTO_REMOVE, RESIDENT_PHOTO_REFUSAL_SET o RESIDENT_PHOTO_REFUSAL_CLEAR en dbo.eventos_auditoria, en la
/// misma transacción. Enfermería solo con el permiso RESIDENT_PHOTO_MANAGE, que se vuelve a comprobar dentro de la transacción.
/// </summary>
public interface IResidentPhotoRepository
{
    /// <summary>El JPEG guardado, o null si el residente no tiene foto.</summary>
    Task<byte[]?> ReadAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    Task<ResidentPhotoStatus> ReadStatusAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    Task SetAsync(ResidentPhotoTarget target, byte[] content, ResidentPhotoConsent consent, CancellationToken ct = default);

    /// <summary>Devuelve false si no tenía foto (y entonces no audita nada).</summary>
    Task<bool> RemoveAsync(ResidentPhotoTarget target, CancellationToken ct = default);

    /// <summary>Anota que no quiere foto y borra la que hubiera. Devuelve false si ya estaba anotado (y no audita nada).</summary>
    Task<bool> MarkRefusalAsync(ResidentPhotoTarget target, CancellationToken ct = default);

    /// <summary>Quita la marca. Devuelve false si no la tenía.</summary>
    Task<bool> ClearRefusalAsync(ResidentPhotoTarget target, CancellationToken ct = default);
}
