using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una unidad del centro y si ya está en el ámbito de la Administración que mira.</summary>
public sealed record CenterUnitScope(UnitId UnitId, string Name, bool InScope);

/// <summary>
/// Administración «principal» del centro (script 0047; CJ, 2026-10-07). Lectura: si el ámbito de Administración de quien mira es
/// principal (su último cambio de marca es «principal») y las unidades del centro. Los métodos de lectura no comprueban que sea principal;
/// quien llama decide qué enseña. Las escrituras sí lo comprueban dentro de su transacción.
/// </summary>
public interface IAdministrationScopeDirectory
{
    Task<bool> IsPrincipalAsync(AccountAdministrationAccess access, CancellationToken ct = default);

    /// <summary>Todas las unidades activas del centro, por nombre, con las que ya están en el ámbito de quien mira.</summary>
    Task<IReadOnlyList<CenterUnitScope>> ListCenterUnitsAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}

/// <summary>
/// Escrituras de la Administración principal: cada una en una transacción que comprueba de nuevo que quien actúa es una Administración
/// vigente y principal, y deja su auditoría sin datos.
/// </summary>
public interface IAdministrationScopeRepository
{
    /// <summary>Añade una unidad activa del centro al ámbito de quien actúa (la suya, no la de otra cuenta). Si ya la tenía,
    /// ACCOUNT_CHANGE_CONFLICT; si no es principal, ADMIN_PRINCIPAL_NOT_AUTHORIZED.</summary>
    Task AddUnitToOwnScopeAsync(AccountAdministrationAccess access, UnitId unitId, CancellationToken ct = default);

    /// <summary>Marca o desmarca como principal otro ámbito de Administración vigente del centro. Quien actúa tiene que ser principal. No se
    /// puede dejar al centro sin ninguna principal (ADMIN_PRINCIPAL_LAST); marcar a quien ya lo es o desmarcar a quien no lo es,
    /// ACCOUNT_CHANGE_CONFLICT.</summary>
    Task SetPrincipalAsync(AccountAdministrationAccess access, Guid targetProfileScopeId, bool principal, CancellationToken ct = default);
}

/// <summary>Un ámbito de Administración de un centro, tal como lo ve el operador de plataforma: sin datos de residentes.</summary>
public sealed record PlatformAdministrationScope(
    Guid ProfileScopeId, string AccountSubject, string? AccountName, bool Active, bool IsPrincipal, IReadOnlyList<CenterUnitScope> Units);

/// <summary>Un centro con sus unidades y sus ámbitos de Administración, para el operador de plataforma.</summary>
public sealed record PlatformCenterDetail(
    CenterId CenterId, string Code, string Name, IReadOnlyList<(UnitId UnitId, string Name)> Units,
    IReadOnlyList<PlatformAdministrationScope> Administrations);
