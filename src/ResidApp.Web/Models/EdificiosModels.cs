using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Structure;

namespace ResidApp.Web.Models;

/// <summary>Historia 2 (0029): alta de un edificio o de una planta (EdificioId solo en la planta). OperacionId nace con el formulario y es el
/// id de lo que se crea, así que un reenvío no lo duplica.</summary>
public sealed class NewLayoutFormModel
{
    public Guid OperacionId { get; set; }

    public Guid? EdificioId { get; set; }

    /// <summary>Solo en habitaciones y plazas (historia 2, fase 2): la unidad a la que se vuelve y, en la plaza, su habitación.</summary>
    public Guid? UnidadId { get; set; }

    public Guid? HabitacionId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(CenterLayout.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

public sealed class RenameLayoutFormModel
{
    public Guid Id { get; set; }

    /// <summary>Solo en habitaciones y plazas: la unidad a la que se vuelve.</summary>
    public Guid? UnidadId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre.")]
    [StringLength(CenterLayout.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre")]
    public string? Nombre { get; set; }
}

/// <summary>Kind es «edificio», «planta», «habitación» o «plaza» (para los textos y para a dónde volver).</summary>
public sealed record RenameLayoutViewModel(string Kind, string CurrentName, RenameLayoutFormModel Form);

/// <summary>Historia 2, fase 2: las habitaciones de una unidad del ámbito con sus plazas.</summary>
public sealed record RoomsViewModel(StructureUnit Unit, IReadOnlyList<LayoutRoom> Rooms);

public sealed record BuildingsViewModel(IReadOnlyList<LayoutBuilding> Buildings);

/// <summary>Ubicacion es «b:{edificio}» o «f:{edificio}:{planta}»; vacía quita la unidad del edificio. Solo se ofrecen edificios y plantas
/// activos; el servidor lo vuelve a comprobar.</summary>
public sealed class UnitLocationFormModel
{
    public Guid UnidadId { get; set; }

    [Display(Name = "Edificio y planta")]
    public string? Ubicacion { get; set; }

    /// <summary>El edificio y la planta elegidos, o null si el texto no es ninguna de las formas válidas.</summary>
    public (Guid? BuildingId, Guid? FloorId)? Parse()
    {
        var text = Ubicacion?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }

        var parts = text.Split(':');
        return parts switch
        {
            ["b", var b] when Guid.TryParse(b, out var building) => (building, null),
            ["f", var b, var f] when Guid.TryParse(b, out var building) && Guid.TryParse(f, out var floor) => (building, floor),
            _ => null,
        };
    }
}

public sealed record UnitLocationViewModel(StructureUnit Unit, IReadOnlyList<LayoutBuilding> Buildings, UnitLocationFormModel Form);
