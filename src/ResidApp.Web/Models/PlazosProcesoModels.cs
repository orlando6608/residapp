using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Supervision;

namespace ResidApp.Web.Models;

/// <summary>Un hito del formulario de plazos: su plazo normal y el de un evento prioritario, en minutos. Vacío: no se mide.</summary>
public sealed class PlazoHitoFormRow
{
    public ProcessMilestone Hito { get; set; }

    [Range(1, ProcessMilestoneRules.MaxDeadlineMinutes, ErrorMessage = "El plazo va de 1 minuto a 10080 (una semana).")]
    public int? PlazoMinutos { get; set; }

    [Range(1, ProcessMilestoneRules.MaxDeadlineMinutes, ErrorMessage = "El plazo va de 1 minuto a 10080 (una semana).")]
    public int? PlazoPrioritarioMinutos { get; set; }
}

public sealed class PlazosProcesoFormModel
{
    /// <summary>La versión leída: si otra persona cambió los plazos entretanto, el guardado se rechaza.</summary>
    public int Version { get; set; }

    public List<PlazoHitoFormRow> Plazos { get; set; } = [];

    public static PlazosProcesoFormModel From(ProcessDeadlinesView view) => new()
    {
        Version = view.Version,
        Plazos = [.. ProcessMilestoneRules.All.Select(h => new PlazoHitoFormRow
        {
            Hito = h,
            PlazoMinutos = ProcessMilestoneRules.ToMinutes(view.Deadlines[h].Normal),
            PlazoPrioritarioMinutos = ProcessMilestoneRules.ToMinutes(view.Deadlines[h].Priority),
        })],
    };

    /// <summary>Los plazos de CJ, sin guardarlos.</summary>
    public static PlazosProcesoFormModel Defaults(int version) => new()
    {
        Version = version,
        Plazos = [.. ProcessMilestoneRules.All.Select(h => new PlazoHitoFormRow
        {
            Hito = h,
            PlazoMinutos = ProcessMilestoneRules.ToMinutes(ProcessMilestoneRules.Defaults[h].Normal),
            PlazoPrioritarioMinutos = ProcessMilestoneRules.ToMinutes(ProcessMilestoneRules.Defaults[h].Priority),
        })],
    };
}

/// <summary>View es null si no se pudo leer (sin permiso o fallo técnico). RestoredDefaults avisa de que el formulario trae los plazos de CJ
/// sin guardar.</summary>
public sealed record PlazosProcesoViewModel(ProcessDeadlinesView? View, PlazosProcesoFormModel Form, bool RestoredDefaults = false);
