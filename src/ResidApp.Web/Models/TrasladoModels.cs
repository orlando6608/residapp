using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Ports;

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
    AdministrativeResidentSummary Resident, TransferResidentFormModel Form, IReadOnlyList<SelectListItem> Units,
    IReadOnlyList<LocationOption> Locations);
