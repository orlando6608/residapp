using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Quién opera la plataforma: ProfileScopeId es el ámbito activo de Plataforma de AccountId, siempre en el centro
/// reservado (PlatformCenter.Id). El repositorio lo vuelve a comprobar dentro de la transacción.</summary>
public sealed record PlatformAccess(Guid ProfileScopeId, AccountId AccountId);

/// <summary>Un centro del sistema distinto del reservado. UnitCount cuenta todas sus unidades, activas o no.</summary>
public sealed record PlatformCenterSummary(CenterId CenterId, string Code, string Name, bool Active, int UnitCount, DateTimeOffset CreatedAt);

/// <summary>Lectura de los centros para el operador de plataforma. Solo trae datos del centro, nunca de residentes.</summary>
public interface IPlatformCenterDirectory
{
    Task<IReadOnlyList<PlatformCenterSummary>> ListAsync(PlatformAccess access, CancellationToken ct = default);

    /// <summary>Un centro con sus unidades y sus ámbitos de Administración vigentes (con su marca de principal y las unidades que tiene cada
    /// uno). Null si el centro no existe o es el reservado. Nunca residentes.</summary>
    Task<PlatformCenterDetail?> FindAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default);
}

/// <summary>
/// Alta de un centro. Una sola transacción que comprueba de nuevo el ámbito del operador, bloquea la fila del centro reservado
/// y escribe el centro, su primera unidad, la cuenta de su primer administrador, el ámbito de Administración y la concesión de la
/// unidad, con su auditoría sin datos. O queda todo, o nada.
/// </summary>
public interface IPlatformCenterRepository
{
    /// <summary>Crea el centro (con OperationId como id). Reenviar la misma operación con los mismos datos devuelve el centro ya
    /// creado; con otros datos, es un conflicto.</summary>
    Task<CenterId> CreateCenterAsync(PlatformAccess access, Guid operationId, NewCenterData data, CancellationToken ct = default);

    /// <summary>Marca o desmarca como principal un ámbito de Administración vigente del centro (el soporte puede dejar el centro sin
    /// ninguna). Ya tenía esa marca: ACCOUNT_CHANGE_CONFLICT; ámbito ajeno al centro: acceso denegado.</summary>
    Task SetAdministrationPrincipalAsync(PlatformAccess access, CenterId centerId, Guid profileScopeId, bool principal, CancellationToken ct = default);

    /// <summary>Añade una unidad activa del centro al ámbito de una Administración del centro. Ya la tenía: ACCOUNT_CHANGE_CONFLICT.</summary>
    Task AddUnitToAdministrationAsync(PlatformAccess access, CenterId centerId, Guid profileScopeId, UnitId unitId, CancellationToken ct = default);
}
