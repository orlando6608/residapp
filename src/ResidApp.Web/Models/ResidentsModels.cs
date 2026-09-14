using System.ComponentModel.DataAnnotations;
using ResidApp.Domain.Residents;

namespace ResidApp.Web.Models;

/// <summary>Modelo del formulario de alta de residente. Los campos de autorización (perfil, cuenta) no
/// viajan aquí: los resuelve el servidor a partir de la identidad de sesión, nunca del formulario.</summary>
public sealed class CreateResidentFormModel
{
    [Required]
    [Display(Name = "Ámbito de perfil")]
    public Guid AmbitoPerfilId { get; set; }

    [Required]
    [Display(Name = "Centro")]
    public Guid CentroId { get; set; }

    [Required]
    [Display(Name = "Unidad")]
    public Guid UnidadId { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Nombre completo")]
    public string NombreVisible { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha de nacimiento")]
    public DateOnly? FechaNacimiento { get; set; }

    [Required]
    [Display(Name = "Sexo documentado")]
    public DocumentedSexCode SexoDocumentadoCodigo { get; set; }

    [StringLength(200)]
    [Display(Name = "Referencia interna")]
    public string? ReferenciaInterna { get; set; }

    /// <summary>Se genera al mostrar el formulario y viaja oculto: da soporte a la idempotencia del caso de
    /// uso (un reenvío accidental con el mismo OperacionId no duplica el alta).</summary>
    [Required]
    public Guid OperacionId { get; set; }
}
