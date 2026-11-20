using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>Traslado del residente (script 0040). UnidadOrigenId es la unidad en la que estaba al abrir el formulario y OperacionId el
/// identificador del traslado, para que un reenvío no lo repita. Ubicacion es «r:{habitación}» o «p:{plaza}» de la unidad de destino.</summary>
public sealed class TransferResidentFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid UnidadOrigenId { get; set; }

    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Elige la unidad de destino.")]
    [Display(Name = "Unidad de destino")]
    public Guid? UnidadDestinoId { get; set; }

    [Display(Name = "Habitación o plaza en la unidad (opcional)")]
    public string? Ubicacion { get; set; }

    public (Guid? RoomId, Guid? PlaceId)? ParseLocation() => CreateResidentFormModel.ParseLocation(Ubicacion);
}

public sealed record TransferResidentViewModel(
    ResidentId ResidentId, string DisplayName, string UnitName, TransferResidentFormModel Form, IReadOnlyList<SelectListItem> Units,
    IReadOnlyList<LocationOption> Locations);

/// <summary>Los textos del traslado, comunes a Administración y a Enfermería con permiso.</summary>
public static class TransferTexts
{
    public static string Success(TransferResidentResult result) =>
        "Residente trasladado."
        + (result.OpenEventsMoved > 0
            ? $" {result.OpenEventsMoved} {(result.OpenEventsMoved == 1 ? "episodio abierto pasa" : "episodios abiertos pasan")} a la unidad de destino."
            : string.Empty)
        + (result.BaselineDraftCancelled ? " Se canceló el borrador de basal: se rehace en la unidad de destino." : string.Empty);

    public static string Failure(ApplicationFailure error) => error.Code switch
    {
        ApplicationFailureCode.Conflict =>
            "El residente ya no está donde lo veías, esa plaza está ocupada o el traslado ya se hizo. Revisa la ficha y vuelve a intentarlo.",
        ApplicationFailureCode.InvalidInput =>
            "Revisa el destino: tiene que ser una unidad activa de tu ámbito, con una habitación o plaza disponibles de esa unidad, y algo tiene que cambiar.",
        _ => error.Message,
    };
}
