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
}
