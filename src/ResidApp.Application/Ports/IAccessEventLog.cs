using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Tipo de un evento del registro de accesos (dbo.eventos_acceso.tipo, script 0062).</summary>
public enum AccessEventType
{
    [Code("ENTRADA")] Entrada,
    [Code("ENTRADA_DESARROLLO")] EntradaDesarrollo,
    [Code("CONTRASENA_FALLIDA")] ContrasenaFallida,
    [Code("BLOQUEO")] Bloqueo,
    [Code("SEGUNDO_FACTOR_FALLIDO")] SegundoFactorFallido,
    [Code("SEGUNDO_FACTOR_ALTA")] SegundoFactorAlta,
    [Code("CODIGO_RECUPERACION_USADO")] CodigoRecuperacionUsado,
    [Code("CREDENCIALES_DESARROLLO")] CredencialesDesarrollo,
    [Code("INVITACION_USADA")] InvitacionUsada,
    [Code("RECUPERACION_USADA")] RecuperacionUsada,
    [Code("INVITACION_NO_VALIDA")] InvitacionNoValida,
    [Code("CONTRASENA_CAMBIADA")] ContrasenaCambiada,
    [Code("CODIGOS_REGENERADOS")] CodigosRegenerados,
    [Code("SESION_CERRADA")] SesionCerrada,
}

/// <summary>Un evento del registro de accesos de una cuenta, tal como se le enseña a ella en «Mi cuenta». Fecha UTC.</summary>
public sealed record AccessEventEntry(DateTime OccurredAt, AccessEventType Type, string? Ip);

/// <summary>Registro de seguridad de la autenticación (ADR 0007, incremento I1), separado de la auditoría clínica. Sin cuenta cuando el
/// usuario escrito no existe: nunca se guarda lo tecleado. Las fechas son UTC.</summary>
public interface IAccessEventLog
{
    Task RecordAsync(
        AccessEventType type, AccountId? accountId, Guid? sessionId, string? ip, DateTime now, CancellationToken ct = default);

    /// <summary>Los últimos eventos de la cuenta, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<AccessEventEntry>> ListRecentAsync(AccountId accountId, int take, CancellationToken ct = default);
}
