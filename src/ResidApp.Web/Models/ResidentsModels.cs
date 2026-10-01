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

    /// <summary>Anulable para que el formulario empiece vacío y no con el identificador de ceros.</summary>
    [Required(ErrorMessage = "Indica el identificador de la unidad.")]
    [Display(Name = "Unidad")]
    public Guid? UnidadId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre completo.")]
    [StringLength(200, ErrorMessage = "El nombre no puede pasar de 200 caracteres.")]
    [Display(Name = "Nombre completo")]
    public string NombreVisible { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica la fecha de nacimiento.")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha de nacimiento")]
    public DateOnly? FechaNacimiento { get; set; }

    [Required(ErrorMessage = "Elige el sexo documentado.")]
    [Display(Name = "Sexo documentado")]
    public DocumentedSexCode SexoDocumentadoCodigo { get; set; }

    [StringLength(200, ErrorMessage = "La referencia interna no puede pasar de 200 caracteres.")]
    [Display(Name = "Referencia interna")]
    public string? ReferenciaInterna { get; set; }

    /// <summary>Se genera al mostrar el formulario y viaja oculto: da soporte a la idempotencia del caso de
    /// uso (un reenvío accidental con el mismo OperacionId no duplica el alta).</summary>
    [Required]
    public Guid OperacionId { get; set; }
}
