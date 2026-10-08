using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;

namespace ResidApp.Web.Models;

/// <summary>DIR-01/DIR-02: contadores por unidad del ámbito de supervisión.</summary>
public sealed record DireccionInicioViewModel(IReadOnlyList<SupervisionUnitSummary> Units)
{
    public int Open => Units.Sum(u => u.Open);
    public int FollowUpsOverdue => Units.Sum(u => u.FollowUpsOverdue);
}

/// <summary>DIR-12: la lista de derivaciones y, si se pidieron las de episodios cerrados, el periodo elegido (From y To incluidos).</summary>
public sealed record SupervisionReferralsViewModel(
    IReadOnlyList<SupervisionReferral> Referrals, bool IncludeClosed = false, DateOnly? From = null, DateOnly? To = null)
{
    public int Signed => Referrals.Count(r => r.ReportSigned);
    public int Unsigned => Referrals.Count - Signed;
    public int WithCalls => Referrals.Count(r => r.FamilyCallAttempts > 0);
    public int Closed => Referrals.Count(r => r.Closed);
}

/// <summary>DIR-12: pedir también las derivaciones de episodios cerrados, en un periodo. Llega por GET (?cerradas=true&amp;desde=&amp;hasta=);
/// sin fechas, los últimos 30 días.</summary>
public sealed class SupervisionReferralFilter
{
    public bool Cerradas { get; set; }

    public DateOnly? Desde { get; set; }

    public DateOnly? Hasta { get; set; }

    public IndicatorPeriodFilter Period => new() { Desde = Desde, Hasta = Hasta };
}

/// <summary>DIR-03: filtrar los pendientes por tipo y unidad. Llega por GET (?tipo=&amp;unidad=); los campos vacíos no filtran.</summary>
public sealed class SupervisionFilter
{
    public SupervisionPendingType? Tipo { get; set; }

    public Guid? Unidad { get; set; }

    public bool IsEmpty => Tipo is null && Unidad is null;
}

/// <summary>DIR-03: la lista ya filtrada con las unidades para el desplegable (las del ámbito).</summary>
public sealed record SupervisionPendingViewModel(
    SupervisionPendingList List, SupervisionFilter Filter, IReadOnlyList<(Guid Id, string Name)> Units, DateOnly Today);

/// <summary>DIR-08 a DIR-10: el periodo de los indicadores. Llega por GET (?desde=&amp;hasta=); una fecha vacía o mal formada
/// toma el valor por defecto: los últimos 30 días, hoy incluido.</summary>
public sealed class IndicatorPeriodFilter
{
    public const int DefaultDays = 30;

    public DateOnly? Desde { get; set; }

    public DateOnly? Hasta { get; set; }

    public (DateOnly From, DateOnly To) Resolve(DateOnly today)
    {
        var to = Hasta ?? today;
        return (Desde ?? to.AddDays(1 - DefaultDays), to);
    }

    /// <summary>Mensaje para un periodo imposible; null si es válido.</summary>
    public string? Validate(DateOnly today)
    {
        var (from, to) = Resolve(today);
        if (from > to)
        {
            return "La fecha «desde» no puede ser posterior a la fecha «hasta».";
        }

        return to.DayNumber - from.DayNumber + 1 > SupervisionIndicatorRules.MaxPeriodDays
            ? $"El periodo no puede pasar de {SupervisionIndicatorRules.MaxPeriodDays} días."
            : null;
    }
}

/// <summary>DIR-08 a DIR-10 y DIR-16: los indicadores (null si el periodo no es válido o falló la lectura) y cuándo se
/// generaron, para la cabecera del informe imprimible.</summary>
public sealed record SupervisionIndicatorsViewModel(
    DateOnly From, DateOnly To, SupervisionIndicators? Indicators, DateTime GeneratedAt);

public static class SupervisionDisplay
{
    /// <summary>DIR-10: un mes del periodo, recortado si no está entero («septiembre de 2026» o «1–15 de septiembre de 2026»).</summary>
    public static string MonthLabel(SupervisionMonthIndicators month)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        var whole = month.From.Day == 1 && month.To == month.From.AddMonths(1).AddDays(-1);
        var name = month.From.ToString("MMMM 'de' yyyy", culture);
        return whole ? name
            : month.From == month.To ? $"{month.From.Day} de {name}"
            : $"{month.From.Day}–{month.To.Day} de {name}";
    }

    public static string Label(SupervisionPendingType type) => type switch
    {
        SupervisionPendingType.SinValorar => "Sin valorar",
        SupervisionPendingType.Escalado => "Escalado a Medicina",
        SupervisionPendingType.Seguimiento => "En seguimiento",
        SupervisionPendingType.SeguimientoVencido => "Seguimiento pendiente",
        SupervisionPendingType.IndicacionPendiente => "Con indicación pendiente",
        SupervisionPendingType.IndicacionConIncidencia => "Con indicación no realizada",
        SupervisionPendingType.ProtocoloUrgente => "Protocolo urgente activo",
        _ => type.ToString(),
    };

    public static string Label(SupervisionMilestoneKind kind) => kind switch
    {
        SupervisionMilestoneKind.Registrado => "Registrado",
        SupervisionMilestoneKind.ValoracionIniciada => "Valoración de Enfermería iniciada",
        SupervisionMilestoneKind.ValoracionMedicaIniciada => "Valoración médica iniciada",
        SupervisionMilestoneKind.Escalado => "Escalado a Medicina",
        SupervisionMilestoneKind.IndicacionEmitida => "Indicación emitida",
        SupervisionMilestoneKind.SeguimientoIniciado => "Seguimiento iniciado",
        SupervisionMilestoneKind.ProtocoloUrgenteActivado => "Protocolo urgente activado",
        SupervisionMilestoneKind.InformeDerivacionFirmado => "Informe de derivación firmado",
        _ => kind.ToString(),
    };

    /// <summary>DIR-17 y ADM-13 (0024): nombre en español de los permisos configurables.</summary>
    public static string PermissionLabel(string code) => code switch
    {
        "RESIDENT_IDENTITY_CREATE" => "Dar de alta residentes",
        "BASELINE_INITIAL_COMPLETE" => "Basal inicial (al alta)",
        "BASELINE_REEVALUATE" => "Reevaluar el basal",
        "CLINICAL_DETAIL_READ" => "Lectura clínica detallada y auditada",
        "REFERENCE_RANGES_MANAGE" => "Gestionar los rangos de referencia de constantes",
        "PROCESS_DEADLINES_MANAGE" => "Gestionar los plazos de los hitos del proceso",
        _ => code,
    };
}

/// <summary>DIR-11: la revisión de calidad de proceso (null si el periodo no es válido o falló la lectura).</summary>
public sealed record ProcessQualityViewModel(DateOnly From, DateOnly To, ProcessQualityReport? Report, bool CanManageDeadlines = false);

/// <summary>DIR-11: textos de los hitos del proceso y de su estado.</summary>
public static class ProcessMilestoneDisplay
{
    public static string Label(ResidApp.Domain.Supervision.ProcessMilestone milestone) => milestone switch
    {
        ResidApp.Domain.Supervision.ProcessMilestone.ValoracionEnfermeria => "Valoración de Enfermería",
        ResidApp.Domain.Supervision.ProcessMilestone.ValoracionMedica => "Valoración médica",
        ResidApp.Domain.Supervision.ProcessMilestone.RecepcionTransferencia => "Recepción de la transferencia",
        ResidApp.Domain.Supervision.ProcessMilestone.InformeDerivacion => "Informe de derivación",
        ResidApp.Domain.Supervision.ProcessMilestone.LlamadaFamilia => "Llamada a la familia",
        ResidApp.Domain.Supervision.ProcessMilestone.LecturaIndicacion => "Lectura de la indicación médica",
        _ => "Realización de la indicación médica",
    };

    public static string Range(ResidApp.Domain.Supervision.ProcessMilestone milestone) => milestone switch
    {
        ResidApp.Domain.Supervision.ProcessMilestone.ValoracionEnfermeria => "Desde que Auxiliar registra un cambio hasta que Enfermería empieza la valoración.",
        ResidApp.Domain.Supervision.ProcessMilestone.ValoracionMedica => "Desde el escalado hasta que Medicina empieza su valoración.",
        ResidApp.Domain.Supervision.ProcessMilestone.RecepcionTransferencia => "Desde la transferencia de un seguimiento de Enfermería hasta que se confirma la recepción.",
        ResidApp.Domain.Supervision.ProcessMilestone.InformeDerivacion => "Desde la activación del protocolo urgente hasta la firma del informe.",
        ResidApp.Domain.Supervision.ProcessMilestone.LlamadaFamilia => "Desde la activación del protocolo urgente hasta el primer intento de llamada.",
        ResidApp.Domain.Supervision.ProcessMilestone.LecturaIndicacion => "Desde que Medicina la emite hasta que Enfermería confirma la lectura.",
        _ => "Desde que Medicina la emite hasta que consta realizada o no realizada.",
    };

    public static string Status(ResidApp.Domain.Supervision.MilestoneStatus status) => status switch
    {
        ResidApp.Domain.Supervision.MilestoneStatus.EnPlazo => "En plazo",
        ResidApp.Domain.Supervision.MilestoneStatus.HechoFueraDePlazo => "Hecho fuera de plazo",
        ResidApp.Domain.Supervision.MilestoneStatus.Pendiente => "Pendiente, en plazo",
        ResidApp.Domain.Supervision.MilestoneStatus.APuntoDeVencer => "A punto de vencer",
        _ => "Sin hacer, fuera de plazo",
    };

    /// <summary>«30 min», «8 h», «1 h 30 min»; «No se mide» si no hay plazo.</summary>
    public static string Term(TimeSpan? term)
    {
        if (term is not { } value)
        {
            return "No se mide";
        }

        var hours = (int)value.TotalHours;
        var minutes = value.Minutes;
        return hours == 0 ? $"{minutes} min" : minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }
}

/// <summary>DIR-11: los avisos de hitos a punto de vencer o vencidos que ve Enfermería o Medicina en su inicio. Controller es el de su ficha
/// de residente.</summary>
public sealed record MilestoneWarningsModel(IReadOnlyList<MilestoneEntry>? Items, string Controller);
