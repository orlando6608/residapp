namespace ResidApp.Application.Ports;

/// <summary>Lo que SQL necesita para aplicar la seguridad por filas (ADR 0008): el sujeto verificado por el proveedor de identidad
/// y el id del ámbito activo. El centro no viaja: lo deduce el predicado de SQL, que comprueba que el ámbito es de esa cuenta,
/// está activo y es de ese centro. Un ámbito manipulado en la cookie no sale de los ámbitos de la propia cuenta.</summary>
public sealed record TenantScope(string ExternalSubject, Guid ProfileScopeId);

public interface ITenantContext
{
    /// <summary>Null cuando no hay sesión verificada o ámbito elegido (login, selección de ámbito): SQL no verá ninguna fila de las
    /// tablas con seguridad por filas.</summary>
    Task<TenantScope?> GetAsync(CancellationToken ct = default);
}
