using ResidApp.Domain.Organization;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-07: una persona que ocupa un cargo ahora. Con cuenta (AccountId) se muestra su nombre vigente; sin cuenta, solo el nombre que
/// se escribió (CJ 1.1). Ocupar un cargo no concede ningún permiso.</summary>
public sealed record PositionOccupant(Guid OccupantId, AccountId? AccountId, string Name)
{
    public bool HasAccount => AccountId is not null;
}

/// <summary>ADM-07: una unidad ligada a un cargo (CJ 1.6). Los equipos de turnos no tienen relación con el organigrama.</summary>
public sealed record PositionUnit(UnitId UnitId, string Name);

/// <summary>ADM-07: un cargo del organigrama del centro con quién lo ocupa y sus unidades. SuperiorId es el cargo del que depende.</summary>
public sealed record PositionInfo(
    Guid PositionId, string Name, bool OnePerson, Guid? SuperiorId, string? Note, bool Active,
    IReadOnlyList<PositionOccupant> Occupants, IReadOnlyList<PositionUnit> Units);

/// <summary>ADM-07: una cuenta profesional activa del centro que puede ocupar un cargo.</summary>
public sealed record PositionOccupantCandidate(AccountId AccountId, string Name);

/// <summary>
/// ADM-07 (0073): lectura del organigrama. El organigrama es del centro y lo ven todos sus profesionales, por eso ListAsync no depende del
/// ámbito de unidades de quien lee. ListCandidatesAsync es solo para Administración.
/// </summary>
public interface IOrganizationDirectory
{
    /// <summary>Los cargos del centro (también los inactivos si includeInactive) con sus ocupantes y unidades.</summary>
    Task<IReadOnlyList<PositionInfo>> ListAsync(CenterId centerId, bool includeInactive, CancellationToken ct = default);

    /// <summary>Las cuentas profesionales activas del centro (no familiares), por nombre.</summary>
    Task<IReadOnlyList<PositionOccupantCandidate>> ListCandidatesAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}

/// <summary>
/// ADM-07 (0073): escrituras sobre los cargos. Cada una va en una transacción que comprueba de nuevo el ámbito de Administración, bloquea la
/// fila del centro y deja su evento en dbo.eventos_auditoria, sin datos (un cargo no se borra, se inactiva, y la auditoría guarda su
/// identificador). Quien gestiona solo liga unidades de su ámbito: al guardar se reemplazan sus ligas y se conservan las demás.
/// </summary>
public interface IOrganizationRepository
{
    /// <summary>Crea el cargo (con OperationId como id). Reenviar la misma operación devuelve el cargo ya creado.</summary>
    Task<Guid> CreateAsync(
        AccountAdministrationAccess access, Guid operationId, PositionData data, IReadOnlyList<UnitId> units, CancellationToken ct = default);

    Task UpdateAsync(
        AccountAdministrationAccess access, Guid positionId, PositionData data, IReadOnlyList<UnitId> units, CancellationToken ct = default);

    /// <summary>Inactiva (active = false) o reactiva un cargo. Inactivar exige que no tenga ocupantes ni cargos activos que dependan de él.</summary>
    Task ChangeStatusAsync(AccountAdministrationAccess access, Guid positionId, bool active, CancellationToken ct = default);

    /// <summary>Pone a una persona en el cargo: una cuenta profesional del centro o, sin cuenta, solo un nombre (exactamente una de las dos).</summary>
    Task AddOccupantAsync(
        AccountAdministrationAccess access, Guid positionId, AccountId? accountId, string? personName, CancellationToken ct = default);

    Task RemoveOccupantAsync(AccountAdministrationAccess access, Guid positionId, Guid occupantId, CancellationToken ct = default);

    /// <summary>Carga los ocho cargos de CJ. Solo si el centro aún no tiene ninguno.</summary>
    Task LoadTemplateAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}
