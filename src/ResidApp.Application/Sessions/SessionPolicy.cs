using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.Sessions;

/// <summary>Por qué se cerró una sesión (dbo.sesiones.motivo_cierre, script 0061).</summary>
public enum SessionCloseReason
{
    [Code("LOGOUT")] Logout,
    [Code("INACTIVIDAD")] Inactividad,
    [Code("CADUCIDAD")] Caducidad,
    [Code("CAMBIO_IDENTIDAD")] CambioIdentidad,
    /// <summary>Se usó una invitación de recuperación de la cuenta (script 0063): se cierran todas sus sesiones.</summary>
    [Code("RECUPERACION")] Recuperacion,
    /// <summary>La propia persona cierra sus otras sesiones o las cierra al cambiar la contraseña (script 0064).</summary>
    [Code("CIERRE_PROPIO")] CierrePropio,
}

/// <summary>Resultado de comprobar una sesión en una petición.</summary>
public enum SessionCheck
{
    Valid,
    /// <summary>No existe, ya está cerrada, es de otro sujeto o la cuenta no está activa.</summary>
    Rejected,
    /// <summary>Lleva más de la inactividad permitida sin uso.</summary>
    Idle,
    /// <summary>Pasó su caducidad absoluta, que la renovación de la cookie no amplía.</summary>
    Expired,
}

/// <summary>Reglas de la sesión (ADR 0007, incremento I0). Todo falla cerrado: ante la duda, la sesión no vale.</summary>
public sealed record SessionPolicy(TimeSpan IdleTimeout, TimeSpan MaxDuration)
{
    /// <summary>La última actividad se guarda como mucho una vez por minuto, para no escribir en cada petición.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public SessionCheck Check(StoredSession? session, string? externalSubject, DateTime now) =>
        session is null || session.ClosedAt is not null || !session.AccountActive
            || !string.Equals(session.ExternalSubject, externalSubject, StringComparison.Ordinal) ? SessionCheck.Rejected
        : now >= session.ExpiresAt ? SessionCheck.Expired
        : now - session.LastActivityAt > IdleTimeout ? SessionCheck.Idle
        : SessionCheck.Valid;

    public bool NeedsTouch(StoredSession session, DateTime now) => now - session.LastActivityAt >= TouchInterval;
}
