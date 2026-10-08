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

    /// <summary>Se elige entre las unidades del ámbito activo (ListActiveScopeUnits). Anulable para que, con varias, el
    /// selector empiece sin ninguna elegida.</summary>
    [Required(ErrorMessage = "Elige la unidad.")]
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

    /// <summary>Habitación o plaza dentro de la unidad (opcional, historia 2, script 0029): «r:{habitación}» o «p:{plaza}». El servidor comprueba
    /// que sea de la unidad elegida y, si es una plaza, que esté libre.</summary>
    [Display(Name = "Ubicación en la unidad (opcional)")]
    public string? Ubicacion { get; set; }

    /// <summary>La habitación y la plaza elegidas, o null si el texto no es ninguna de las formas válidas (vacío es válido: ninguna).</summary>
    public (Guid? RoomId, Guid? PlaceId)? ParseLocation() => ParseLocation(Ubicacion);

    /// <summary>Lo mismo para un texto «r:{habitación}» / «p:{plaza}» de cualquier formulario con selector de ubicación.</summary>
    public static (Guid? RoomId, Guid? PlaceId)? ParseLocation(string? ubicacion)
    {
        var text = ubicacion?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }

        return text.Split(':') switch
        {
            ["r", var id] when Guid.TryParse(id, out var room) => (room, null),
            ["p", var id] when Guid.TryParse(id, out var place) => (null, place),
            _ => null,
        };
    }

    /// <summary>Los familiares de contacto prioritario, referentes y tutores legales (CJ, 2026-10-07). Tres filas en el formulario; las que
    /// se dejan en blanco se ignoran, y más desde la ficha del residente.</summary>
    public List<NewResidentFamilyFormRow> Familiares { get; set; } = [new(), new(), new()];

    public IReadOnlyList<ResidApp.Domain.Families.NewResidentFamilyInput> FamilyInputs() =>
        Familiares.Select(row => new ResidApp.Domain.Families.NewResidentFamilyInput(
            row.NombreVisible, row.Relacion, row.Telefono, row.Correo, row.Referente, row.TutorLegal, row.ContactoPrioritario)).ToList();

    /// <summary>Se genera al mostrar el formulario y viaja oculto: da soporte a la idempotencia del caso de
    /// uso (un reenvío accidental con el mismo OperacionId no duplica el alta).</summary>
    [Required]
    public Guid OperacionId { get; set; }
}

/// <summary>Una fila de familiar del formulario de alta. Sin validación de campo: una fila en blanco se ignora y una a medias la rechaza la
/// regla de ResidentFamilyAtAdmission, que usa las reglas de siempre de un familiar.</summary>
public sealed class NewResidentFamilyFormRow
{
    [Display(Name = "Nombre")]
    public string? NombreVisible { get; set; }

    [Display(Name = "Relación con el residente (p. ej., «Hija»)")]
    public string? Relacion { get; set; }

    [Display(Name = "Teléfono")]
    public string? Telefono { get; set; }

    [Display(Name = "Correo electrónico (opcional)")]
    public string? Correo { get; set; }

    [Display(Name = "Familiar referente")]
    public bool Referente { get; set; }

    [Display(Name = "Tutor legal")]
    public bool TutorLegal { get; set; }

    [Display(Name = "Contacto prioritario (contacto urgente)")]
    public bool ContactoPrioritario { get; set; }
}
