using System.ComponentModel.DataAnnotations;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Structure;

namespace ResidApp.Web.Models;

/// <summary>Alta de un centro por el operador de plataforma. OperacionId es el id del centro nuevo, así que reenviar no lo duplica.</summary>
public sealed class NewCenterFormModel
{
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Escribe el código del centro.")]
    [StringLength(CenterUnit.MaxCodeLength, MinimumLength = CenterUnit.MinCodeLength, ErrorMessage = "El código del centro lleva entre {2} y {1} caracteres.")]
    [Display(Name = "Código del centro")]
    public string? CodigoCentro { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del centro.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre del centro no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre del centro")]
    public string? NombreCentro { get; set; }

    [Required(ErrorMessage = "Escribe el código de la primera unidad.")]
    [StringLength(CenterUnit.MaxCodeLength, MinimumLength = CenterUnit.MinCodeLength, ErrorMessage = "El código de la unidad lleva entre {2} y {1} caracteres.")]
    [Display(Name = "Código de la primera unidad")]
    public string? CodigoUnidad { get; set; }

    [Required(ErrorMessage = "Escribe el nombre de la primera unidad.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre de la unidad no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre de la primera unidad")]
    public string? NombreUnidad { get; set; }

    [Required(ErrorMessage = "Escribe el identificador de acceso del administrador.")]
    [StringLength(ProfessionalAccount.MaxSubjectLength, MinimumLength = ProfessionalAccount.MinSubjectLength,
        ErrorMessage = "El identificador de acceso lleva entre {2} y {1} caracteres.")]
    [Display(Name = "Identificador de acceso del administrador")]
    public string? IdentificadorAdministrador { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del administrador.")]
    [StringLength(ProfessionalAccount.MaxDisplayNameLength, ErrorMessage = "El nombre del administrador no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre del administrador")]
    public string? NombreAdministrador { get; set; }
}

public sealed record NewCenterViewModel(NewCenterFormModel Form);
