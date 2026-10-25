using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;

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

/// <summary>El recuadro de la foto (_FotoResidenteEditar), en las fichas de Administración y de Enfermería: su estado (consentimiento, «No quiere
/// foto») y si quien mira puede cambiarla. Today es el día del centro, para el valor por defecto y el máximo de la fecha de la firma.</summary>
public sealed record ResidentPhotoEditorViewModel(Guid ResidentId, ResidentPhotoEditorState State, DateOnly Today, DateTimeOffset Now)
{
    public bool HasPhoto => State.Status.UpdatedAt is not null;

    public bool RenewalDue => State.Status.UpdatedAt is { } updated && ResidentPhotoRenewal.IsDue(updated, Now);
}

/// <summary>Formulario de la foto: la imagen y el consentimiento, quién lo firmó y cuándo (CJ, foto 1.1).</summary>
public sealed class ResidentPhotoFormModel
{
    public Guid ResidenteId { get; set; }

    public IFormFile? Foto { get; set; }

    public ResidentPhotoConsentSigner? Firmante { get; set; }

    public DateOnly? FechaConsentimiento { get; set; }
}
