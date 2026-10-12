using System.ComponentModel.DataAnnotations;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;

namespace ResidApp.Web.Models;

/// <summary>Una publicación correctora de un comunicado ya publicado (script 0053), de Administración o de Dirección Clínica. Sale en el acto.</summary>
public sealed class PublicarCorreccionFormModel
{
    [Required]
    public Guid ComunicadoId { get; set; }

    [Required(ErrorMessage = "Indica el tipo de la corrección.")]
    [Display(Name = "Tipo de la corrección")]
    public FamilyCommunicationType? Tipo { get; set; }

    [Required(ErrorMessage = "Escribe el texto de la corrección.")]
    [StringLength(FamilyCommunicationChoice.MaxTextLength)]
    [Display(Name = "Texto de la corrección")]
    public string? Texto { get; set; }
}

/// <summary>La retirada excepcional de un comunicado publicado. El motivo es interno: la familia no lo ve.</summary>
public sealed class RetirarComunicadoFormModel
{
    [Required]
    public Guid ComunicadoId { get; set; }

    [Required(ErrorMessage = "Escribe el motivo de la retirada.")]
    [StringLength(FamilyCommunicationWithdrawal.MaxReasonLength)]
    [Display(Name = "Motivo de la retirada")]
    public string? Motivo { get; set; }
}

public sealed record PublicarCorreccionViewModel(FamilyCommunicationStatusView Item, PublicarCorreccionFormModel Form);

public sealed record RetirarComunicadoViewModel(FamilyCommunicationStatusView Item, RetirarComunicadoFormModel Form);
