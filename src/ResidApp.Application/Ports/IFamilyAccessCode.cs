using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un código a punto de mandarse: nunca el código ni la dirección, solo su resumen con clave (script 0121).</summary>
public sealed record NewAccessCode(
    Guid Id, AccountId AccountId, byte[] Digest, DateTime CreatedAt, DateTime ExpiresAt, string Language, string TemplateVersion);

/// <summary>Si se puede mandar un código ahora o qué límite lo impide.</summary>
public enum AccessCodeReservation
{
    Reserved,
    /// <summary>Demasiados fallos: la cuenta necesita una invitación nueva.</summary>
    Blocked,
    /// <summary>Se mandó otro hace menos de la espera mínima.</summary>
    TooSoon,
    HourLimit,
    DayLimit,
}

/// <summary>Qué pasa al comprobar un código.</summary>
public enum AccessCodeCheck
{
    Valid,
    Wrong,
    /// <summary>Fallo con el que ese código agota sus intentos y deja de valer.</summary>
    Exhausted,
    /// <summary>No hay código vivo con ese identificador: caducado, usado, anulado o de otra cuenta. No cuenta como fallo.</summary>
    NotLive,
    /// <summary>La cuenta queda, o ya estaba, bloqueada hasta una invitación nueva.</summary>
    Blocked,
}

/// <summary>Los códigos de acceso de la familia (dbo.codigos_acceso_correo) y la dirección a la que van. Las fechas son UTC.</summary>
public interface IFamilyAccessCodeStore
{
    /// <summary>La dirección de la ficha de esta cuenta, leída con su propio ámbito familiar (seguridad por filas). Null si no hay ficha, falta el correo o las fichas no
    /// coinciden: entonces no se entra.</summary>
    Task<string?> FindAddressAsync(string externalSubject, AccountId accountId, IReadOnlyList<ActiveProfileScope> scopes, CancellationToken ct = default);

    /// <summary>Comprueba los límites y, si se puede, anula el código vivo y anota el nuevo, todo a la vez (dos peticiones simultáneas no se saltan un límite).</summary>
    Task<AccessCodeReservation> ReserveAsync(NewAccessCode code, CancellationToken ct = default);

    /// <summary>Anota cómo fue el envío. Si falló (<paramref name="failureCode"/> con valor), el código deja de valer.</summary>
    Task RecordSendAsync(Guid codeId, string? operationId, string? failureCode, DateTime now, CancellationToken ct = default);

    /// <summary>Consume el código si el resumen coincide (una sola vez, aunque lleguen dos peticiones); si no, cuenta el fallo.</summary>
    Task<AccessCodeCheck> RedeemAsync(Guid codeId, AccountId accountId, byte[] digest, DateTime now, CancellationToken ct = default);

    /// <summary>Al usar una invitación: anula el código vivo, los envíos y fallos anteriores dejan de contar y se quita el bloqueo.</summary>
    Task ResetAsync(AccountId accountId, DateTime now, CancellationToken ct = default);
}

/// <summary>La purga de los códigos de acceso (script 0121): cierra los caducados y borra los de más de 30 días con el procedimiento de la base de datos, que fija el plazo. Solo la
/// puede ejecutar la conexión de la tarea programada (rol residapp_tareas).</summary>
public interface IAccessCodePurge
{
    Task<bool> HasTaskIdentityAsync(CancellationToken ct = default);

    Task<int> PurgeAsync(CancellationToken ct = default);
}
