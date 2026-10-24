using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Structure;

namespace ResidApp.Web.Models;

/// <summary>Datos del propio centro (script 0068): su nombre y su logo. Branding es null si no se pudo leer.</summary>
public sealed record DatosCentroViewModel(CenterBranding? Branding, DatosCentroFormModel Form);

public sealed class DatosCentroFormModel
{
    [Required(ErrorMessage = "Escribe el nombre del centro.")]
    [StringLength(CenterUnit.MaxNameLength, ErrorMessage = "El nombre del centro no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre del centro")]
    public string? Nombre { get; set; }
}

public sealed class LogoCentroFormModel
{
    public IFormFile? Logo { get; set; }
}
