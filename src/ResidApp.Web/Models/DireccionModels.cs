using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;

namespace ResidApp.Web.Models;

/// <summary>DIR-01/DIR-02: contadores por unidad del ámbito de supervisión.</summary>
public sealed record DireccionInicioViewModel(IReadOnlyList<SupervisionUnitSummary> Units)
{
    public int Open => Units.Sum(u => u.Open);
    public int FollowUpsOverdue => Units.Sum(u => u.FollowUpsOverdue);
}

/// <summary>DIR-12: las derivaciones en curso con su resumen (cuántas tienen el informe firmado, cuántas no y cuántas llamadas a la familia constan).</summary>
public sealed record SupervisionReferralsViewModel(IReadOnlyList<SupervisionReferral> Referrals)
{
    public int Signed => Referrals.Count(r => r.ReportSigned);
    public int Unsigned => Referrals.Count - Signed;
    public int WithCalls => Referrals.Count(r => r.FamilyCallAttempts > 0);
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
        _ => code,
    };
}
