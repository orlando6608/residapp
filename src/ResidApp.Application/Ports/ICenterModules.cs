using ResidApp.Domain.Modules;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// Los módulos opcionales de un centro (ADR 0015, script 0098). Lectura: el último cambio de un módulo en un centro (la seguridad por filas deja al centro ver solo el suyo y a
/// Plataforma todos). Escritura: solo Plataforma, siempre añadiendo una fila (nunca UPDATE ni DELETE) y dejando su auditoría sin datos en la misma transacción.
/// </summary>
public interface ICenterModuleRepository
{
    /// <summary>El cambio vigente del módulo en el centro, o null si nunca se activó. Sin caché: se pregunta en cada petición.</summary>
    Task<CenterModuleChange?> FindLastAsync(CenterId centerId, string moduleCode, CancellationToken ct = default);

    /// <summary>
    /// Añade el cambio (alta, renovación o baja) con el número siguiente y la auditoría <c>CENTER_MODULE_ENABLE</c> o <c>CENTER_MODULE_DISABLE</c> (solo centro y código del módulo).
    /// Centro inexistente o reservado: MODULE_INVALID. Apagar un módulo que ya está apagado: MODULE_CHANGE_CONFLICT. Si faltan los módulos de los que depende: MODULE_DEPENDENCY_MISSING.
    /// </summary>
    Task AppendAsync(PlatformAccess access, NewCenterModuleChange change, DateOnly today, CancellationToken ct = default);

    /// <summary>Todos los cambios de módulos de un centro, el más reciente primero, para la pantalla de Plataforma. Sin la cuenta de quien los hizo.</summary>
    Task<IReadOnlyList<PlatformModuleChange>> ListChangesAsync(PlatformAccess access, CenterId centerId, CancellationToken ct = default);

    /// <summary>Módulos de cualquier centro activo cuyo último cambio es un alta que acaba entre las dos fechas (las dos incluidas): vencimientos próximos y vencidos sin renovar.</summary>
    Task<IReadOnlyList<PlatformExpiringModule>> ListExpiringAsync(PlatformAccess access, DateOnly from, DateOnly until, CancellationToken ct = default);
}

/// <summary>Interruptores globales de entorno: el ajuste de desarrollo con todo activo y la clave del proveedor de cada módulo con tercero.</summary>
public interface IModuleSettings
{
    /// <summary>Ajuste <c>Modulos__ActivarTodos</c>: en desarrollo y pruebas, todos los módulos activos sin filas. En producción impide arrancar.</summary>
    bool ActivateAll { get; }

    /// <summary>Un módulo con tercero solo funciona si su interruptor global está puesto (el dictado, con la clave de Mistral). Sin él, para todos los centros, no funciona aunque la fila diga ACTIVO.</summary>
    bool IsThirdPartyConfigured(string moduleCode);
}

/// <summary>Un cambio de módulo ya guardado, tal como lo ve Plataforma: sin la cuenta de quien lo hizo ni ningún nombre; solo lo que prueba la instrucción del centro y el plazo.</summary>
public sealed record PlatformModuleChange(
    string ModuleCode, int Number, bool Enabled, DateOnly? ValidFrom, DateOnly? ValidUntil, string? AnnexReference, string? AnnexVersion, string? SheetVersion,
    ModuleDisableReason? DisableReason, DateTimeOffset RequestReceivedAt, ModuleRequestChannel Channel, string RequestReference, DateTimeOffset ChangedAt);

/// <summary>Un módulo cuya última alta vence (o venció sin renovarse) en el periodo consultado.</summary>
public sealed record PlatformExpiringModule(CenterId CenterId, string CenterName, string ModuleCode, DateOnly ValidUntil);
