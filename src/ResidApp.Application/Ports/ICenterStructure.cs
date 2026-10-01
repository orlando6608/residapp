using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-05: una unidad del centro concedida al ámbito de quien gestiona. CurrentResidents son los residentes con la
/// unidad como ubicación vigente; mientras haya alguno, no se puede inactivar.</summary>
public sealed record StructureUnit(UnitId UnitId, string Code, string Name, bool Active, int CurrentResidents);

/// <summary>ADM-05: lectura de las unidades del ámbito de quien gestiona, activas e inactivas.</summary>
public interface ICenterStructureDirectory
{
    Task<IReadOnlyList<StructureUnit>> ListUnitsAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}

/// <summary>
/// ADM-05 (0025): escrituras sobre las unidades. Cada una va en una transacción que comprueba de nuevo el ámbito de
/// Administración de quien gestiona, serializa los cambios de estructura del centro (bloqueo de la fila del centro) y deja su
/// evento en dbo.eventos_auditoria, sin datos. Una unidad nueva se concede al ámbito de quien la crea. Renombrar e
/// inactivar solo valen sobre unidades de ese ámbito; el código no cambia y nada se borra.
/// </summary>
public interface ICenterStructureRepository
{
    /// <summary>Crea la unidad (con OperationId como id) y la concede al ámbito de quien gestiona. Reenviar la misma
    /// operación devuelve la unidad ya creada.</summary>
    Task<UnitId> CreateUnitAsync(AccountAdministrationAccess access, Guid operationId, CenterUnitData data, CancellationToken ct = default);

    Task RenameUnitAsync(AccountAdministrationAccess access, UnitId unitId, string name, CancellationToken ct = default);

    /// <summary>Inactiva (Active = false) o reactiva la unidad. Inactivar con residentes ubicados en ella es inválido.</summary>
    Task ChangeUnitStatusAsync(AccountAdministrationAccess access, UnitId unitId, bool active, CancellationToken ct = default);
}
