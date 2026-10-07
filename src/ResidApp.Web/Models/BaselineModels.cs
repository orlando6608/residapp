using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Authorization;
using ResidApp.Application.UseCases;

namespace ResidApp.Web.Models;

/// <summary>Modelo del formulario de firma de un borrador de basal ya existente. No existe todavía, ni en
/// este puerto ni en el prototipo legado, un caso de uso para crear el contenido del borrador (las 9 áreas
/// más Barthel) — ver el aviso en Views/Baseline/Sign.cshtml.</summary>
public sealed class SignBaselineFormModel
{
    [Required(ErrorMessage = "Indica el ámbito de perfil.")]
    [Display(Name = "Ámbito de perfil")]
    public Guid? AmbitoPerfilId { get; set; }

    [Required(ErrorMessage = "Indica el centro.")]
    [Display(Name = "Centro")]
    public Guid? CentroId { get; set; }

    [Required(ErrorMessage = "Indica el residente.")]
    [Display(Name = "Residente")]
    public Guid? ResidenteId { get; set; }

    [Required(ErrorMessage = "Indica el borrador.")]
    [Display(Name = "Borrador")]
    public Guid? BorradorId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(Name = "Revisión de borrador esperada")]
    public int RevisionBorradorEsperada { get; set; } = 1;

    [Required]
    public Guid OperacionId { get; set; }
}

/// <summary>Modelo de la consulta de historial/estado basal vigente para el perfil Dirección Clínica. El ámbito y el
/// centro salen del ámbito activo, no del formulario; el residente se elige entre los del ámbito.
/// TipoRecurso/Proposito viajan como texto libre, igual que en el caso de uso: es la política quien decide
/// si son válidos, no este formulario.</summary>
public sealed class DirectionBaselineQueryModel
{
    [Required(ErrorMessage = "Elige el residente.")]
    [Display(Name = "Residente")]
    public Guid? ResidenteId { get; set; }

    [Required]
    [Display(Name = "Tipo de recurso")]
    public string TipoRecurso { get; set; } = "BASELINE_HISTORY";

    [Display(Name = "Finalidad")]
    public string? Proposito { get; set; }

    [StringLength(ClinicalAccessSettings.MaxJustificationLength, ErrorMessage = "La justificación admite hasta 300 caracteres.")]
    [Display(Name = "Justificación")]
    public string? Justificacion { get; set; }

    [Required]
    public Guid OperacionId { get; set; }
}

/// <summary>Qué significa cada finalidad que puede declarar Dirección, con las frases de CJ (2026-10-06).</summary>
public static class ClinicalDetailAccessPurposeDisplay
{
    public static string Description(ClinicalDetailAccessPurpose purpose) => purpose switch
    {
        ClinicalDetailAccessPurpose.ContinuidadAsistencial =>
            "Comprender la secuencia de actuaciones de un caso y comprobar su continuidad entre profesionales o turnos. " +
            "Por ejemplo: revisar un seguimiento vencido para entender qué se hizo y qué quedó pendiente.",
        ClinicalDetailAccessPurpose.IncidenciaReclamacion =>
            "Aclarar los hechos de un caso ante una incidencia o reclamación documentada. " +
            "Por ejemplo: revisar los hechos tras una reclamación familiar.",
        ClinicalDetailAccessPurpose.TrazabilidadDocumental =>
            "Comprobar autoría, secuencia y relación entre originales, correcciones y rectificaciones. " +
            "Por ejemplo: revisar por qué se rectificó una valoración y qué información cambió.",
        _ => string.Empty,
    };
}
