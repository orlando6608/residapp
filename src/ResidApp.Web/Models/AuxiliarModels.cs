using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;

namespace ResidApp.Web.Models;

/// <summary>AUX-03: identidad mínima del residente asignado más el resumen del basal vigente (null si
/// todavía no tiene ninguno firmado).</summary>
public sealed record AuxiliarResidentDetailViewModel(AssignedResidentSummary Resident, CurrentBaselineSummary? Baseline);

/// <summary>AUX-02: igual que AuxiliarResidentDetailViewModel, más un OperacionId propio por cada una de
/// las dos acciones de cierre ya construidas (grupo A2) — cada formulario necesita el suyo para que un
/// reenvío accidental no duplique el cierre.</summary>
public sealed record AuxiliarRegistroViewModel(
    AssignedResidentSummary Resident, CurrentBaselineSummary? Baseline, Guid SinCambiosOperacionId, Guid NoValorableOperacionId);

/// <summary>AUX-05 "No valorable": el único campo del formulario es el motivo, obligatorio.</summary>
public sealed class NoValorableFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    [Required]
    [StringLength(500)]
    [Display(Name = "Motivo")]
    public string Motivo { get; set; } = string.Empty;
}

/// <summary>AUX-06 a AUX-12 "Registrar cambio": una sola clase de formulario reutilizada en los dos pasos
/// (RegistrarCambio, que la rellena, y ConfirmarCambio, que la recibe otra vez por campos ocultos más
/// AvisoDirecto, solo exigido en el paso de confirmación cuando la clasificación es Prioritario — de ahí
/// que aquí no lleve [Required]).</summary>
public sealed class RegistrarCambioFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    /// <summary>Una entrada por cada una de las diez áreas con contenido (AUX-06/AUX-07): sin catálogo de
    /// opciones rápidas todavía, el texto libre es el contenido. Una entrada vacía o ausente equivale a
    /// "no seleccionada".</summary>
    public Dictionary<string, string> AreaTexto { get; set; } = new();

    [Display(Name = "Temperatura (°C)")]
    public decimal? Temperatura { get; set; }

    [Required(ErrorMessage = "Elige una clasificación.")]
    [Display(Name = "Clasificación")]
    public DailyChangeClassification? Clasificacion { get; set; }

    [Display(Name = "Motivo prioritario")]
    public DailyChangePriorityReason? MotivoPrioritario { get; set; }

    [Display(Name = "Aviso directo documentado")]
    [StringLength(1000)]
    public string? AvisoDirecto { get; set; }
}

public static class DailyChangePriorityReasonDisplay
{
    public static string Label(DailyChangePriorityReason reason) => reason switch
    {
        DailyChangePriorityReason.AlteracionConcienciaEstadoGeneral => "Alteración de conciencia o del estado general",
        DailyChangePriorityReason.CaidaLesionTraumatismo => "Caída, lesión o traumatismo",
        DailyChangePriorityReason.FiebreSospechaInfeccion => "Fiebre o sospecha de infección",
        DailyChangePriorityReason.DolorNuevoIntenso => "Dolor nuevo o intenso",
        DailyChangePriorityReason.DificultadRespiratoria => "Dificultad respiratoria",
        DailyChangePriorityReason.DeficitNeurologicoNuevo => "Déficit neurológico nuevo",
        _ => reason.ToString(),
    };
}

public static class DailyChangeAreaDisplay
{
    public static string Label(DailyChangeAreaCode area) => area switch
    {
        DailyChangeAreaCode.AlimentacionHidratacion => "Alimentación / hidratación",
        DailyChangeAreaCode.MovilidadFuncionalidad => "Movilidad / funcionalidad",
        DailyChangeAreaCode.AnimoConducta => "Ánimo / conducta",
        DailyChangeAreaCode.DolorMalestar => "Dolor / malestar",
        DailyChangeAreaCode.HecesDiuresis => "Heces / diuresis",
        DailyChangeAreaCode.Sueno => "Sueño",
        DailyChangeAreaCode.LesionesPiel => "Lesiones en piel",
        DailyChangeAreaCode.ParticipacionRelacionSocial => "Participación / relación social",
        DailyChangeAreaCode.IncidenciasCaidas => "Incidencias / caídas",
        DailyChangeAreaCode.EstadoConciencia => "Estado de conciencia",
        _ => area.ToString(),
    };
}

public static class BaselineAreaDisplay
{
    public static string Label(BaselineArea area) => area switch
    {
        BaselineArea.Movilidad => "Movilidad",
        BaselineArea.Alimentacion => "Alimentación",
        BaselineArea.Continencia => "Continencia",
        BaselineArea.AseoHigiene => "Aseo e higiene",
        BaselineArea.Cognicion => "Cognición",
        BaselineArea.Comunicacion => "Comunicación",
        BaselineArea.Conducta => "Conducta",
        BaselineArea.Sueno => "Sueño",
        BaselineArea.AyudasHabituales => "Ayudas habituales",
        _ => area.ToString(),
    };

    /// <summary>Resumen genérico de una respuesta de área: solo valores (ya en español, como el resto de
    /// catálogos de dominio), nunca los nombres de propiedad en inglés del record C#. Suficiente para
    /// AUX-03 ("necesarios para el cuidado cotidiano"), sin un formateador propio por área.</summary>
    public static IReadOnlyList<string> Summarize(IBaselineAreaAnswer answer) =>
        answer.GetType().GetProperties()
            .Select(property => property.GetValue(answer))
            .Where(value => value is not null && value is not string { Length: 0 })
            .Select(value => value!.ToString()!)
            .ToList();
}
