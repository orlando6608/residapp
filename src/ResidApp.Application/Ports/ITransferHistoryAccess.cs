using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// La petición del historial tras un traslado (script 0077): la unidad de destino de un traslado abre los episodios cerrados de la unidad anterior solo
/// si lo pide con un motivo. Una petición es de una cuenta, un ámbito y un residente, vale unos minutos y se termina al cambiar de residente, de perfil o de
/// ámbito y al cerrar sesión. Las lecturas que la usan (IChangeInboxDirectory) la revalidan y la auditan.
/// </summary>
public interface ITransferHistoryAccess
{
    /// <summary>Crea la petición (terminando las abiertas de la cuenta) y escribe su fila de auditoría en la misma transacción. Devuelve su id, o null si la
    /// cuenta no tiene ese ámbito activo de Enfermería o Medicina, el residente no está ahora en una de sus unidades o no tiene episodios cerrados de una unidad
    /// anterior que ese ámbito pueda abrir.</summary>
    Task<Guid?> DeclareAsync(
        string externalSubject, Guid profileScopeId, CenterId centerId, ResidentId residentId, TransferHistoryReason reason, int minutes, CancellationToken ct = default);
}
