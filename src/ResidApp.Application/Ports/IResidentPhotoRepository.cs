using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente sobre el que el caso de uso ya ha comprobado el ámbito del perfil (ResidentPhotoApplicationService), con la cuenta
/// y el perfil que firman el cambio y la unidad en la que está.</summary>
public sealed record ResidentPhotoTarget(AccountId AccountId, SystemProfile Profile, CenterId CenterId, UnitId UnitId, ResidentId ResidentId);

/// <summary>
/// Foto de identificación del residente (script 0059): una por residente. Ponerla sustituye la anterior y quitarla la borra; cada cambio deja
/// RESIDENT_PHOTO_SET o RESIDENT_PHOTO_REMOVE en dbo.eventos_auditoria, en la misma transacción.
/// </summary>
public interface IResidentPhotoRepository
{
    /// <summary>El JPEG guardado, o null si el residente no tiene foto.</summary>
    Task<byte[]?> ReadAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    Task SetAsync(ResidentPhotoTarget target, byte[] content, CancellationToken ct = default);

    /// <summary>Devuelve false si no tenía foto (y entonces no audita nada).</summary>
    Task<bool> RemoveAsync(ResidentPhotoTarget target, CancellationToken ct = default);
}
