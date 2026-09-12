using System.ComponentModel.DataAnnotations;

namespace ResidApp.Web.Models;

/// <summary>Modelo del formulario de firma de un borrador de basal ya existente. No existe todavía, ni en
/// este puerto ni en el prototipo legado, un caso de uso para crear el contenido del borrador (las 9 áreas
/// más Barthel) — ver el aviso en Views/Baseline/Sign.cshtml.</summary>
public sealed class SignBaselineFormModel
{
    [Required]
    public Guid ProfileScopeId { get; set; }

    [Required]
    public Guid CenterId { get; set; }

    [Required]
    public Guid ResidentId { get; set; }

    [Required]
    public Guid DraftId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int ExpectedDraftRevision { get; set; } = 1;

    [Required]
    public Guid OperationId { get; set; }
}

/// <summary>Modelo de la consulta de historial/estado basal vigente para el perfil Dirección Clínica.
/// ResourceType/Purpose viajan como texto libre, igual que en el caso de uso: es la política quien decide
/// si son válidos, no este formulario.</summary>
public sealed class DirectionBaselineQueryModel
{
    [Required]
    public Guid ProfileScopeId { get; set; }

    [Required]
    public Guid CenterId { get; set; }

    [Required]
    public Guid ResidentId { get; set; }

    [Required]
    public string ResourceType { get; set; } = "BASELINE_HISTORY";

    public string? Purpose { get; set; } = "SUPERVISION_CLINICA";

    [Required]
    public Guid OperationId { get; set; }
}
