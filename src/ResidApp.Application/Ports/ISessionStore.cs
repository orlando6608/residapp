using ResidApp.Application.Sessions;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una sesión de dbo.sesiones (0061) con el sujeto y el estado de su cuenta, lo necesario para validarla en cada petición.
/// Las fechas son UTC.</summary>
public sealed record StoredSession(
    Guid Id, string ExternalSubject, bool AccountActive, DateTime ExpiresAt, DateTime LastActivityAt, DateTime? ClosedAt);

/// <summary>Una sesión abierta de una cuenta, para enseñársela a su propietaria en «Mi cuenta» (ADR 0007, I3). <c>Ip</c> es la de la
/// entrada que la abrió, si consta. Las fechas son UTC.</summary>
public sealed record OpenSession(Guid Id, DateTime CreatedAt, DateTime LastActivityAt, DateTime ExpiresAt, string? Ip);

/// <summary>Sesiones de usuario guardadas en SQL (ADR 0007, incremento I0). La cookie solo lleva el id: cerrar la sesión aquí la
/// invalida en la petición siguiente.</summary>
public interface ISessionStore
{
    Task OpenAsync(Guid sessionId, AccountId accountId, DateTime now, DateTime expiresAt, CancellationToken ct = default);

    Task<StoredSession?> FindAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Anota actividad en una sesión abierta; no hace nada si ya está cerrada.</summary>
    Task TouchAsync(Guid sessionId, DateTime now, CancellationToken ct = default);

    /// <summary>Cierra una sesión abierta; no hace nada si ya estaba cerrada.</summary>
    Task CloseAsync(Guid sessionId, SessionCloseReason reason, DateTime now, CancellationToken ct = default);

    /// <summary>Cierra todas las sesiones abiertas de la cuenta (al recuperarla).</summary>
    Task CloseAllAsync(AccountId accountId, SessionCloseReason reason, DateTime now, CancellationToken ct = default);

    /// <summary>Las sesiones de la cuenta sin cerrar ni caducar, la más reciente primero. Puede incluir alguna parada por inactividad,
    /// que la aplicación ya no aceptaría: filtra quien llama con la <see cref="SessionPolicy"/>.</summary>
    Task<IReadOnlyList<OpenSession>> ListOpenAsync(AccountId accountId, DateTime now, CancellationToken ct = default);

    /// <summary>Cierra una sesión abierta solo si es de esta cuenta. Devuelve si la cerró.</summary>
    Task<bool> CloseOwnedAsync(AccountId accountId, Guid sessionId, SessionCloseReason reason, DateTime now, CancellationToken ct = default);

    /// <summary>Cierra todas las sesiones abiertas de la cuenta salvo una (la actual).</summary>
    Task CloseOthersAsync(AccountId accountId, Guid keepSessionId, SessionCloseReason reason, DateTime now, CancellationToken ct = default);
}
