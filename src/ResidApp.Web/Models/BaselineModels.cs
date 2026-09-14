using System.ComponentModel.DataAnnotations;

namespace ResidApp.Web.Models;

/// <summary>Modelo del formulario de firma de un borrador de basal ya existente. No existe todavía, ni en
/// este puerto ni en el prototipo legado, un caso de uso para crear el contenido del borrador (las 9 áreas
/// más Barthel) — ver el aviso en Views/Baseline/Sign.cshtml.</summary>
public sealed class SignBaselineFormModel
{
    [Required]
    [Display(Name = "Ámbito de perfil")]
    public Guid AmbitoPerfilId { get; set; }

    [Required]
    [Display(Name = "Centro")]
    public Guid CentroId { get; set; }

    [Required]
    [Display(Name = "Residente")]
    public Guid ResidenteId { get; set; }

    [Required]
    [Display(Name = "Borrador")]
    public Guid BorradorId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    [Display(Name = "Revisión de borrador esperada")]
    public int RevisionBorradorEsperada { get; set; } = 1;

    [Required]
    public Guid OperacionId { get; set; }
}

/// <summary>Modelo de la consulta de historial/estado basal vigente para el perfil Dirección Clínica.
/// TipoRecurso/Proposito viajan como texto libre, igual que en el caso de uso: es la política quien decide
/// si son válidos, no este formulario.</summary>
public sealed class DirectionBaselineQueryModel
{
    [Required]
    [Display(Name = "Ámbito de perfil")]
    public Guid AmbitoPerfilId { get; set; }

    [Required]
    [Display(Name = "Centro")]
    public Guid CentroId { get; set; }

    [Required]
    [Display(Name = "Residente")]
    public Guid ResidenteId { get; set; }

    [Required]
    [Display(Name = "Tipo de recurso")]
    public string TipoRecurso { get; set; } = "BASELINE_HISTORY";

    [Display(Name = "Propósito")]
    public string? Proposito { get; set; } = "SUPERVISION_CLINICA";

    [Required]
    public Guid OperacionId { get; set; }
}
