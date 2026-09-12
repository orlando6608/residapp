using System.ComponentModel.DataAnnotations;
using ResidApp.Domain.Residents;

namespace ResidApp.Web.Models;

/// <summary>Modelo del formulario de alta de residente. Los campos de autorización (perfil, cuenta) no
/// viajan aquí: los resuelve el servidor a partir de la identidad de sesión, nunca del formulario.</summary>
public sealed class CreateResidentFormModel
{
    [Required]
    public Guid ProfileScopeId { get; set; }

    [Required]
    public Guid CenterId { get; set; }

    [Required]
    public Guid UnitId { get; set; }

    [Required]
    [StringLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateOnly? BirthDate { get; set; }

    [Required]
    public DocumentedSexCode DocumentedSexCode { get; set; }

    [StringLength(200)]
    public string? InternalReference { get; set; }

    /// <summary>Se genera al mostrar el formulario y viaja oculto: da soporte a la idempotencia del caso de
    /// uso (un reenvío accidental con el mismo OperationId no duplica el alta).</summary>
    [Required]
    public Guid OperationId { get; set; }
}
