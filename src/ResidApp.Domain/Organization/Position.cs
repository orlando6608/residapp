using ResidApp.Shared;

namespace ResidApp.Domain.Organization;

/// <summary>ADM-07 (script 0073): los datos de un cargo del organigrama del centro. Superior es el cargo del que depende (uno solo, CJ 1.5);
/// Note es la coordinación funcional u otra aclaración, en texto libre.</summary>
public sealed record PositionData(string Name, bool OnePerson, Guid? SuperiorId, string? Note);

/// <summary>
/// Reglas de los cargos (CJ, organigrama-y-cargos-cj): el nombre lleva de 1 a 100 caracteres tras recortar, la nota hasta 500 y el nombre de
/// una persona sin cuenta hasta 200. Ocupar un cargo no concede ningún permiso: cargo, turno, perfil y permiso son conceptos separados.
/// </summary>
public static class Position
{
    public const int MaxNameLength = 100;
    public const int MaxNoteLength = 500;
    public const int MaxPersonNameLength = 200;
    public const string InvalidCode = "POSITION_INVALID";

    public static PositionData Validate(string? name, bool onePerson, Guid? superiorId, string? note) =>
        new(ValidateName(name), onePerson, superiorId, ValidateNote(note));

    public static string ValidateName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength ? throw new DomainValidationException(InvalidCode) : text;
    }

    /// <summary>La nota recortada; vacía es «sin nota».</summary>
    public static string? ValidateNote(string? note)
    {
        var text = note?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxNoteLength ? throw new DomainValidationException(InvalidCode) : text;
    }

    /// <summary>El nombre de una persona sin cuenta que ocupa un cargo: solo el nombre, sin contacto ni otros datos (CJ 1.1).</summary>
    public static string ValidatePersonName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxPersonNameLength ? throw new DomainValidationException("POSITION_OCCUPANT_INVALID") : text;
    }
}

/// <summary>Un cargo con los cargos que dependen de él, para pintar el organigrama anidado.</summary>
public sealed record PositionTreeNode<T>(T Item, IReadOnlyList<PositionTreeNode<T>> Children);

public static class PositionTree
{
    /// <summary>Cambiar el superior de positionId a newSuperiorId crearía un ciclo: el cargo no puede depender de sí mismo ni de uno de sus
    /// subordinados (directos o no). superiors dice de qué cargo depende cada cargo del centro (null si de ninguno).</summary>
    public static bool WouldCreateCycle(IReadOnlyDictionary<Guid, Guid?> superiors, Guid positionId, Guid? newSuperiorId)
    {
        var visited = new HashSet<Guid>();
        for (var current = newSuperiorId; current is { } id; current = superiors.TryGetValue(id, out var next) ? next : null)
        {
            if (id == positionId || !visited.Add(id))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly StringComparer ByName = StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);

    /// <summary>Los cargos anidados: cada raíz con sus subordinados debajo, todos por nombre dentro de su nivel. Un cargo cuyo superior no está
    /// en la lista (por ejemplo, porque está inactivo) se pinta como raíz, y un ciclo (que la BD impide) no cuelga el recorrido.</summary>
    public static IReadOnlyList<PositionTreeNode<T>> Nest<T>(IEnumerable<T> items, Func<T, Guid> id, Func<T, Guid?> superior, Func<T, string> name)
    {
        var all = items.ToList();
        var ids = all.Select(id).ToHashSet();
        var children = all.Where(i => superior(i) is { } s && ids.Contains(s) && s != id(i)).ToLookup(i => superior(i)!.Value);
        var seen = new HashSet<Guid>();

        PositionTreeNode<T>? Build(T item) => !seen.Add(id(item))
            ? null
            : new PositionTreeNode<T>(item, children[id(item)].OrderBy(name, ByName).Select(Build).OfType<PositionTreeNode<T>>().ToList());

        return all.Where(i => superior(i) is not { } s || !ids.Contains(s) || s == id(i)).OrderBy(name, ByName)
            .Select(Build).OfType<PositionTreeNode<T>>().ToList();
    }
}

/// <summary>Un cargo de la plantilla, con la clave de su superior dentro de la misma plantilla.</summary>
public sealed record PositionTemplateEntry(string Key, string Name, bool OnePerson, string? SuperiorKey, string? Note);

/// <summary>
/// Los ocho cargos orientativos de CJ (docs/tareas/alta-prioridad/pendientes-administracion.md, «Guardado para el futuro»), que un centro
/// puede cargar al empezar y después cambiar, quitar o ampliar (CJ 1.2). Suposiciones de ingeniería, editables por el centro: cuáles son de una
/// sola persona; que «Entidad titular o gerencia» no entra (el centro la añade como cargo si quiere, CJ 1.1); y que el personal administrativo
/// va en la nota del Responsable de Administración y no como noveno cargo.
/// </summary>
public static class PositionTemplate
{
    public static readonly IReadOnlyList<PositionTemplateEntry> Entries =
    [
        new("DIRECCION", "Dirección del centro", true, null, null),
        new("MEDICA", "Dirección médica / Coordinación médica", true, "DIRECCION", null),
        new("ENFERMERIA_COORD", "Coordinación / Supervisión de Enfermería", true, "DIRECCION",
            "Coordinación funcional con la Dirección médica."),
        new("UNIDAD", "Responsable de unidad o planta", false, "ENFERMERIA_COORD", "Solo si el centro usa esta figura."),
        new("MEDICINA", "Medicina", false, "MEDICA", "Sin Dirección médica, depende de la Dirección del centro."),
        new("ENFERMERIA", "Enfermería", false, "ENFERMERIA_COORD", null),
        new("AUXILIARES", "Auxiliares / Gerocultores", false, "UNIDAD", "Sin Responsable de unidad, dependen de la Coordinación de Enfermería."),
        new("ADMINISTRACION", "Responsable de Administración", true, "DIRECCION", "El personal administrativo depende de este cargo."),
    ];
}
