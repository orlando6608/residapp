using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Errors;
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

/// <summary>Item null: al enviar el formulario no se pudo leer el comunicado, así que solo se enseña lo escrito para volver a intentarlo.</summary>
public sealed record PublicarCorreccionViewModel(FamilyCommunicationStatusView? Item, PublicarCorreccionFormModel Form);

/// <summary>Item null: como en <see cref="PublicarCorreccionViewModel"/>.</summary>
public sealed record RetirarComunicadoViewModel(FamilyCommunicationStatusView? Item, RetirarComunicadoFormModel Form);

/// <summary>No se pudo leer el comunicado que se quiere corregir o retirar: no se sabe si lo admite, así que no se puede dar ese motivo.</summary>
public static class FamilyCommunicationReadFailure
{
    private const string Denied = "No se puede acceder a ese comunicado.";

    public static string OnOpen(ApplicationFailure failure) =>
        failure.Code == ApplicationFailureCode.AccessDenied ? Denied : "Ahora mismo no se puede leer ese comunicado. Vuelve a intentarlo.";

    public static string OnSend(ApplicationFailure failure) => failure.Code == ApplicationFailureCode.AccessDenied
        ? Denied
        : "Ahora mismo no se puede leer el comunicado, así que no se ha cambiado nada. Lo que has escrito sigue aquí: vuelve a intentarlo.";
}
