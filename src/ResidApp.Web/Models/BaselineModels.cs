using System.ComponentModel.DataAnnotations;

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

    [Display(Name = "Propósito")]
    public string? Proposito { get; set; } = "SUPERVISION_CLINICA";

    [Required]
    public Guid OperacionId { get; set; }
}
