using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;

namespace ResidApp.Web.Models;

/// <summary>Baja del residente (script 0041). OperacionId nace con el formulario y es el identificador de la baja, así que un reenvío no la
/// repite. El texto es obligatorio con «Otro motivo».</summary>
public sealed class DischargeResidentFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid OperacionId { get; set; }

    [Display(Name = "Motivo de la baja")]
    public ResidentDischargeReason Motivo { get; set; }

    [StringLength(ResidentDischarge.MaxReasonTextLength, ErrorMessage = "El texto no puede pasar de {1} caracteres.")]
    [Display(Name = "Detalle del motivo (obligatorio con «Otro motivo»)")]
    public string? MotivoTexto { get; set; }
}

public sealed record DischargeResidentViewModel(AdministrativeResidentSummary Resident, DischargeResidentFormModel Form);

public sealed record DischargedResidentsViewModel(IReadOnlyList<DischargedResident> Residents, DateOnly Today);

/// <summary>Reactivación de un residente dado de baja: unidad de destino y, opcional, habitación o plaza («r:{habitación}» o «p:{plaza}»).</summary>
public sealed class ReactivateResidentFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Elige la unidad.")]
    [Display(Name = "Unidad")]
    public Guid? UnidadId { get; set; }

    [Display(Name = "Habitación o plaza en la unidad (opcional)")]
    public string? Ubicacion { get; set; }

    public (Guid? RoomId, Guid? PlaceId)? ParseLocation() => CreateResidentFormModel.ParseLocation(Ubicacion);
}

public sealed record ReactivateResidentViewModel(
    DischargedResident Resident, ReactivateResidentFormModel Form, IReadOnlyList<SelectListItem> Units, IReadOnlyList<LocationOption> Locations);

/// <summary>Suspensión por ingreso hospitalario prolongado: nota opcional.</summary>
public sealed class SuspendResidentFormModel
{
    public Guid ResidenteId { get; set; }

    public Guid OperacionId { get; set; }

    [StringLength(500, ErrorMessage = "La nota no puede pasar de {1} caracteres.")]
    [Display(Name = "Nota (por ejemplo, el hospital; opcional)")]
    public string? Nota { get; set; }
}

public sealed record SuspendResidentViewModel(AdministrativeResidentSummary Resident, SuspendResidentFormModel Form);
