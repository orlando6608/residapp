namespace ResidApp.Domain.Supervision;

/// <summary>
/// DIR-11, revisión de calidad de proceso (CJ, 2026-10-07; continuidad-supervision-comunicacion, tema 4): los hitos del proceso con plazo.
/// Cada uno mide el tiempo entre dos pasos que la aplicación ya registra.
/// </summary>
public enum ProcessMilestone
{
    /// <summary>Desde que Auxiliar registra un cambio hasta que Enfermería empieza la valoración.</summary>
    ValoracionEnfermeria,

    /// <summary>Desde el escalado a Medicina hasta que Medicina empieza su valoración.</summary>
    ValoracionMedica,

    /// <summary>Desde la transferencia de un seguimiento de Enfermería hasta que se confirma la recepción. En Medicina la recepción es
    /// opcional, así que no se mide.</summary>
    RecepcionTransferencia,

    /// <summary>Desde la activación del protocolo urgente hasta la firma del informe de derivación.</summary>
    InformeDerivacion,

    /// <summary>Desde la activación del protocolo urgente hasta el primer intento de llamada a la familia.</summary>
    LlamadaFamilia,

    /// <summary>Desde que Medicina emite una indicación hasta que Enfermería confirma la lectura.</summary>
    LecturaIndicacion,

    /// <summary>Desde que Medicina emite una indicación hasta que consta realizada o no realizada.</summary>
    RealizacionIndicacion,
}

/// <summary>Cómo está un hito en un momento dado.</summary>
public enum MilestoneStatus
{
    /// <summary>Hecho dentro del plazo.</summary>
    EnPlazo,

    /// <summary>Hecho, pero después del plazo.</summary>
    HechoFueraDePlazo,

    /// <summary>Todavía sin hacer y con plazo de sobra.</summary>
    Pendiente,

    /// <summary>Todavía sin hacer y en el último tramo del plazo (aviso al equipo responsable).</summary>
    APuntoDeVencer,

    /// <summary>Todavía sin hacer y con el plazo ya pasado.</summary>
    FueraDePlazo,
}

/// <summary>Los plazos de un hito: el normal y el de un evento prioritario. Null en uno de los dos es «no se mide».</summary>
public sealed record MilestoneDeadline(TimeSpan? Normal, TimeSpan? Priority)
{
    public TimeSpan? For(bool priority) => priority ? Priority : Normal;
}

public static class ProcessMilestoneRules
{
    /// <summary>Un hito pendiente pasa a «a punto de vencer» cuando le queda esta parte del plazo o menos.</summary>
    public const double WarningFraction = 0.25;

    public static readonly IReadOnlyList<ProcessMilestone> All = Enum.GetValues<ProcessMilestone>();

    /// <summary>Los plazos de CJ (documento continuidad-supervision-comunicacion, tema 4). Cada centro puede cambiarlos.</summary>
    public static IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline> Defaults { get; } = new Dictionary<ProcessMilestone, MilestoneDeadline>
    {
        [ProcessMilestone.ValoracionEnfermeria] = new(TimeSpan.FromHours(8), TimeSpan.FromMinutes(30)),
        [ProcessMilestone.ValoracionMedica] = new(TimeSpan.FromHours(24), TimeSpan.FromMinutes(30)),
        [ProcessMilestone.RecepcionTransferencia] = new(TimeSpan.FromHours(8), TimeSpan.FromMinutes(30)),
        [ProcessMilestone.InformeDerivacion] = new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)),
        [ProcessMilestone.LlamadaFamilia] = new(TimeSpan.FromHours(1), TimeSpan.FromMinutes(30)),
        [ProcessMilestone.LecturaIndicacion] = new(TimeSpan.FromHours(8), TimeSpan.FromMinutes(30)),
        [ProcessMilestone.RealizacionIndicacion] = new(TimeSpan.FromHours(8), TimeSpan.FromMinutes(30)),
    };

    /// <summary>Estado del hito que empezó en start y terminó en end (null si sigue sin hacer) con el plazo term, en el instante now.
    /// Todo en UTC.</summary>
    public static MilestoneStatus Evaluate(DateTime start, DateTime? end, TimeSpan term, DateTime now)
    {
        var due = start + term;
        if (end is { } done)
        {
            return done <= due ? MilestoneStatus.EnPlazo : MilestoneStatus.HechoFueraDePlazo;
        }

        if (now > due)
        {
            return MilestoneStatus.FueraDePlazo;
        }

        return due - now <= term * WarningFraction ? MilestoneStatus.APuntoDeVencer : MilestoneStatus.Pendiente;
    }

    /// <summary>Si el hito está fuera de plazo, ya hecho o todavía sin hacer.</summary>
    public static bool IsOutOfTerm(MilestoneStatus status) => status is MilestoneStatus.HechoFueraDePlazo or MilestoneStatus.FueraDePlazo;
}
