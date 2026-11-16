using System.Text.RegularExpressions;
using ResidApp.Shared;

namespace ResidApp.Domain.Modules;

/// <summary>Lo que ve un centro de un módulo (ADR 0015). Solo lectura y no disponible son distintos: en solo lectura se consulta lo registrado.</summary>
public enum ModuleState
{
    /// <summary>Sin fila, apagado (módulo sin datos propios), fuera de vigencia (ídem) o sin la clave global de su proveedor: el servidor responde 404.</summary>
    NotAvailable,
    Active,
    /// <summary>Apagado o vencido en un módulo con datos propios: se consulta lo ya registrado, no se crea ni se edita nada.</summary>
    ReadOnly,
}

/// <summary>
/// Un módulo opcional del catálogo (ADR 0015, punto 1). Todo lo que no figura aquí es base. Un módulo nuevo se da de alta con una entrada, sus acciones marcadas
/// con <c>[RequiereModulo]</c> y, si guarda datos, su script. <see cref="PrivacySheetVersion"/> es la versión de la ficha de privacidad
/// (<see cref="PrivacySheetPath"/>) que el centro acepta al activarlo; si la ficha cambia de forma sustancial se sube la versión y se vuelve a activar.
/// </summary>
/// <param name="HasOwnData">El módulo guarda datos propios del centro. Si no (el dictado no guarda ni audio ni transcripción), apagarlo equivale a «no disponible».</param>
/// <param name="UsesThirdParty">Un tercero interviene (el dictado, Mistral): el interruptor global del proveedor manda y la vigencia es obligatoria.</param>
/// <param name="GlobalCut">Cómo se corta para todos los centros a la vez y quién lo hace (no hay apagado global dentro de la aplicación).</param>
public sealed record CenterModule(
    string Code, string Name, IReadOnlyList<string> Dependencies, string PrivacySheetVersion, string PrivacySheetPath,
    IReadOnlyList<string> PrivacyConditions, bool HasOwnData, bool UsesThirdParty, string GlobalCut);

/// <summary>El catálogo de módulos opcionales, en código y al estilo de <c>ProfilePermissions</c>.</summary>
public static class CenterModules
{
    public const string DictadoVoz = "DICTADO_VOZ";

    /// <summary>Condiciones de privacidad del dictado, por código (su texto vive en la ficha). Son bloqueantes antes de activarlo en un centro real.</summary>
    public const string ConditionAnnexSigned = "ANEXO_FIRMADO";
    public const string ConditionZeroRetention = "RETENCION_CERO_MISTRAL";
    public const string ConditionImpactAssessment = "EIPD_CENTRO";
    public const string ConditionStaffInformed = "INFORMACION_PERSONAL_Y_RESIDENTES";

    public static readonly IReadOnlyList<CenterModule> All =
    [
        new CenterModule(
            DictadoVoz, "Dictado por voz", Dependencies: [], PrivacySheetVersion: "1",
            PrivacySheetPath: "docs/privacidad/ficha-modulo-dictado-voz.md",
            PrivacyConditions: [ConditionAnnexSigned, ConditionZeroRetention, ConditionImpactAssessment, ConditionStaffInformed],
            HasOwnData: false, UsesThirdParty: true,
            GlobalCut: "Quitar el ajuste IA__Mistral__ApiKey de la Web App (lo hace Plataforma; procedimiento escrito pendiente)."),
    ];

    public static CenterModule? Find(string? code) => All.FirstOrDefault(m => m.Code == code);
}

/// <summary>Vencimiento y estado: el estado se calcula al consultar, sin tarea programada ni caché.</summary>
public static class CenterModuleRules
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    /// <summary>El día de Madrid de un instante (decidido por Orlando, 2026-11-16): la vigencia de un contrato empieza y acaba a medianoche en España, no en UTC.</summary>
    public static DateOnly TodayInMadrid(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Madrid).DateTime);

    /// <summary>
    /// El cambio vigente (el último) y el día de hoy dan el estado. Una baja, una vigencia vencida o una que aún no ha empezado dejan el módulo apagado: solo lectura si guarda
    /// datos propios, no disponible si no. <paramref name="today"/> es el día de Madrid (<see cref="TodayInMadrid"/>) y <c>vigente_hasta</c> vale hasta el final de ese día.
    /// </summary>
    public static ModuleState Resolve(CenterModule module, CenterModuleChange? last, DateOnly today)
    {
        if (last is null)
        {
            return ModuleState.NotAvailable;
        }

        var inForce = last.Enabled && last.ValidFrom <= today && today <= last.ValidUntil;
        return inForce ? ModuleState.Active : module.HasOwnData ? ModuleState.ReadOnly : ModuleState.NotAvailable;
    }
}

/// <summary>Un cambio ya guardado (una fila de <c>modulos_centro</c>), con lo que hace falta para calcular el estado y mostrar vencimientos.</summary>
public sealed record CenterModuleChange(int Number, bool Enabled, DateOnly? ValidFrom, DateOnly? ValidUntil);

/// <summary>Cómo pidió el centro el cambio (lista cerrada).</summary>
public enum ModuleRequestChannel
{
    [Code("CORREO")] Correo,
    [Code("TELEFONO")] Telefono,
    [Code("TICKET")] Ticket,
    [Code("OTRO")] Otro,
}

/// <summary>Por qué se apaga un módulo (lista cerrada, sin texto libre).</summary>
public enum ModuleDisableReason
{
    [Code("PETICION_CENTRO")] PeticionCentro,
    [Code("FIN_CONTRATO")] FinContrato,
    [Code("IMPAGO")] Impago,
    [Code("OTRO")] Otro,
}

/// <summary>Un cambio de módulo ya validado, listo para guardar. En una baja, todo lo contractual es nulo.</summary>
public sealed record NewCenterModuleChange(
    CenterId CenterId, string ModuleCode, bool Enable, DateOnly? ValidFrom, DateOnly? ValidUntil, string? AnnexReference, string? AnnexVersion,
    string? PrivacySheetVersion, string? ConditionCodes, ModuleDisableReason? DisableReason, DateTimeOffset RequestReceivedAt,
    ModuleRequestChannel Channel, string RequestReference);

/// <summary>
/// Reglas de entrada de un cambio de módulo (ADR 0015; lectura del DPO del 2026-11-16). Las referencias son códigos opacos (nunca nombres): letras sin acentos, dígitos y
/// «. _ / # -», de 1 a 40 caracteres. La versión de la ficha y las condiciones salen del catálogo, no de la persona que opera: activar es aceptar la ficha vigente con todas
/// sus condiciones comprobadas.
/// </summary>
public static partial class NewCenterModuleChangeRules
{
    public const string Invalid = "MODULE_INVALID";

    /// <summary>La hora de la solicitud prueba el plazo de apagado en un registro inmutable: una fecha absurda (año 0001) no se admite. Una petición más antigua que esto no es una petición vigente.</summary>
    public const int MaxRequestAgeDays = 366;

    public static NewCenterModuleChange Validate(
        CenterId centerId, Guid platformCenterId, string? moduleCode, bool enable, DateOnly? validFrom, DateOnly? validUntil, string? annexReference, string? annexVersion,
        bool conditionsChecked, ModuleDisableReason? disableReason, DateTimeOffset? requestReceivedAt, ModuleRequestChannel? channel, string? requestReference,
        DateTimeOffset now)
    {
        var module = CenterModules.Find(moduleCode);
        var reference = requestReference?.Trim();
        if (module is null || centerId.Value == Guid.Empty || centerId.Value == platformCenterId || channel is null || requestReceivedAt is null
            || requestReceivedAt > now || requestReceivedAt < now.AddDays(-MaxRequestAgeDays) || !IsOpaque(reference))
        {
            throw new DomainValidationException(Invalid);
        }

        if (!enable)
        {
            return disableReason is null
                ? throw new DomainValidationException(Invalid)
                : new NewCenterModuleChange(centerId, module.Code, false, null, null, null, null, null, null, disableReason, requestReceivedAt.Value, channel.Value, reference!);
        }

        var annex = annexReference?.Trim();
        var version = annexVersion?.Trim();
        // El último cambio manda: una alta con vigencia futura apagaría hoy el módulo que aún está vigente. Una renovación se registra el día en que empieza (o antes).
        // La tabla es inmutable: una alta que ya nace vencida (un año mal tecleado) dejaría el módulo apagado con la auditoría diciendo «activado», y no se podría corregir.
        if (validFrom is null || validUntil is null || validUntil < validFrom || validUntil < CenterModuleRules.TodayInMadrid(now)
            || validFrom > CenterModuleRules.TodayInMadrid(now)
            || !conditionsChecked || !IsOpaque(annex) || !IsOpaque(version, 16))
        {
            throw new DomainValidationException(Invalid);
        }

        return new NewCenterModuleChange(
            centerId, module.Code, true, validFrom, validUntil, annex, version, module.PrivacySheetVersion, string.Join(',', module.PrivacyConditions), null,
            requestReceivedAt.Value, channel.Value, reference!);
    }

    private static bool IsOpaque(string? value, int maxLength = 40) =>
        !string.IsNullOrEmpty(value) && value.Length <= maxLength && OpaqueReference().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._/#-]+$")]
    private static partial Regex OpaqueReference();
}
