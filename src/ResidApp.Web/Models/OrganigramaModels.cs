using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Organization;

namespace ResidApp.Web.Models;

/// <summary>ADM-07 (0073): crear o cambiar un cargo. En el alta, OperacionId es el id del cargo (reenviar no lo duplica); al editar, CargoId.
/// UnaPersona es «una sola persona» (true) o «varias». Unidades son solo las del ámbito de quien gestiona.</summary>
public sealed class PositionFormModel
{
    public Guid OperacionId { get; set; }

    public Guid CargoId { get; set; }

    [Required(ErrorMessage = "Escribe el nombre del cargo.")]
    [StringLength(Position.MaxNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Nombre del cargo")]
    public string? Nombre { get; set; }

    [Display(Name = "¿Cuántas personas lo ocupan?")]
    public bool UnaPersona { get; set; }

    [Display(Name = "Depende de")]
    public Guid? SuperiorId { get; set; }

    [StringLength(Position.MaxNoteLength, ErrorMessage = "La nota no puede pasar de {1} caracteres.")]
    [Display(Name = "Nota de coordinación funcional")]
    public string? Nota { get; set; }

    public List<Guid> Unidades { get; set; } = [];
}

/// <summary>El formulario de un cargo con sus opciones: los cargos que pueden ser su superior (activos y que no crearían un ciclo), las unidades
/// del ámbito de quien gestiona y las unidades ya ligadas que no son de su ámbito (se enseñan, no se tocan).</summary>
public sealed record PositionFormViewModel(
    PositionFormModel Form, bool IsNew, IReadOnlyList<PositionInfo> Superiors, IReadOnlyList<ScopeUnit> Units, IReadOnlyList<PositionUnit> OtherUnits);

/// <summary>ADM-07: poner a una persona en un cargo: una cuenta del centro (CuentaId) o solo un nombre. Exactamente una de las dos.</summary>
public sealed class PositionOccupantFormModel
{
    public Guid CargoId { get; set; }

    [Display(Name = "Cuenta de la aplicación")]
    public Guid? CuentaId { get; set; }

    [StringLength(Position.MaxPersonNameLength, ErrorMessage = "El nombre no puede pasar de {1} caracteres.")]
    [Display(Name = "Persona sin cuenta (solo el nombre)")]
    public string? Nombre { get; set; }
}

public sealed record PositionOccupantViewModel(
    PositionInfo Position, PositionOccupantFormModel Form, IReadOnlyList<PositionOccupantCandidate> Candidates);

/// <summary>La pantalla del organigrama, para leerlo (Manage = false) o para gestionarlo (Manage = true, con todos los cargos y sus acciones).</summary>
public sealed record OrganigramaViewModel(
    IReadOnlyList<PositionTreeNode<PositionInfo>> Roots, int PositionCount, bool Manage, bool CanLoadTemplate);

/// <summary>Un cargo del árbol para pintarlo y, si se gestiona, con sus acciones.</summary>
public sealed record OrganigramaNodeModel(PositionTreeNode<PositionInfo> Node, bool Manage);
