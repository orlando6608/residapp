namespace ResidApp.Web.Models;

/// <summary>La foto de identificación de un residente en una lista o en la cabecera de su ficha (_FotoResidente). Sin foto, sus iniciales.
/// UpdatedAt va en la dirección de la imagen: al cambiar la foto cambia la dirección, y el navegador no enseña la anterior.</summary>
public sealed record ResidentPhotoViewModel(Guid ResidentId, string DisplayName, DateTimeOffset? UpdatedAt, bool Large = false)
{
    public string Initials => string.Concat(DisplayName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(word => char.ToUpperInvariant(word[0])));
}

/// <summary>El recuadro para poner, cambiar o quitar la foto (_FotoResidenteEditar), en las fichas de Administración y de Enfermería.</summary>
public sealed record ResidentPhotoEditorViewModel(Guid ResidentId, DateTimeOffset? UpdatedAt);

/// <summary>Formulario de la foto. Consentimiento es la casilla obligatoria: consta el del residente o el de su representante.</summary>
public sealed class ResidentPhotoFormModel
{
    public Guid ResidenteId { get; set; }

    public IFormFile? Foto { get; set; }

    public bool Consentimiento { get; set; }
}
