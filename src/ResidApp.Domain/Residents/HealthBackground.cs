using System.ComponentModel.DataAnnotations;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Estado de un apartado de la ficha de antecedentes de salud (script 0094). «Pendiente» no es un estado: es que nadie ha guardado el apartado.
/// En alergias, CON_ENTRADAS es «tiene alergias» (al menos una), NO_REFIERE es «No refiere alergias a medicamentos» y NO_CONSTA es «No se sabe o no
/// consta» (CJ, medicina-alergias-y-plazos 2.1 y medicina-ficha-aclaraciones 1.1: el informe los imprime distintos).</summary>
public enum HealthBackgroundState
{
    [Code("CON_ENTRADAS")] [Display(Name = "Tiene alergias")] ConEntradas,
    [Code("NO_REFIERE")] [Display(Name = "No refiere alergias a medicamentos")] NoRefiere,
    [Code("NO_CONSTA")] [Display(Name = "No se sabe o no consta")] NoConsta,
}

/// <summary>De dónde sale el dato, por categoría y opcional (CJ, 2.7: no hay un método rígido). Nunca el nombre de quien informó.</summary>
public enum HealthBackgroundSource
{
    [Code("INFORME_MEDICO")] [Display(Name = "Informe médico")] InformeMedico,
    [Code("RECETA_ELECTRONICA")] [Display(Name = "Receta electrónica")] RecetaElectronica,
    [Code("RESIDENTE")] [Display(Name = "El propio residente")] Residente,
    [Code("FAMILIA_CUIDADORES")] [Display(Name = "Familia o cuidadores")] FamiliaCuidadores,
}

/// <summary>Por qué se guarda una versión nueva. El registro inicial (versión 1) no se elige; después se distingue un dato nuevo de la corrección de
/// un error. Una versión nunca se modifica: corregir es guardar otra.</summary>
public enum HealthBackgroundReason
{
    [Code("REGISTRO_INICIAL")] [Display(Name = "Registro inicial")] RegistroInicial,
    [Code("ACTUALIZACION")] [Display(Name = "Ha cambiado algo")] Actualizacion,
    [Code("CORRECCION")] [Display(Name = "Corrijo un error")] Correccion,
}

public enum AllergyItemType
{
    [Code("ALERGIA")] [Display(Name = "Alergia")] Alergia,
    [Code("INTOLERANCIA")] [Display(Name = "Intolerancia o efecto adverso")] Intolerancia,
}

/// <summary>Un elemento de la lista de alergias: a qué medicamento o grupo y, en la intolerancia, el motivo (obligatorio; ej. «náuseas»). En la alergia
/// la reacción es opcional. Texto corto de una sola línea (sin saltos ni caracteres de control; límites de la revisión del DPO, 2026-11-15, menores que los de la base).</summary>
public sealed record AllergyItem
{
    public const int MaxNameLength = 100;
    public const int MaxReactionLength = 150;

    public AllergyItemType Type { get; }
    public string Name { get; }
    public string? Reaction { get; }

    public AllergyItem(AllergyItemType type, string? name, string? reaction)
    {
        name = VitalSigns.Normalize(name);
        reaction = VitalSigns.Normalize(reaction);
        if (!Enum.IsDefined(type) || name is null || name.Length > MaxNameLength || reaction is { Length: > MaxReactionLength }
            || name.Any(HealthBackground.IsLineBreakOrControl) || (reaction?.Any(HealthBackground.IsLineBreakOrControl) ?? false)
            || (type == AllergyItemType.Intolerancia && reaction is null))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }

        Type = type;
        Name = name;
        Reaction = reaction;
    }
}

/// <summary>El contenido que se guarda en cada versión del apartado de alergias: el estado, la lista y la fuente opcional. Las intolerancias pueden
/// convivir con cualquier estado; las alergias solo con «Tiene alergias», que exige al menos una (diseño de la ficha, decisión 2).</summary>
public sealed record AllergyRecordContent
{
    public const int MaxItems = 50;

    public HealthBackgroundState State { get; }
    public IReadOnlyList<AllergyItem> Items { get; }
    public HealthBackgroundSource? Source { get; }

    public AllergyRecordContent(HealthBackgroundState state, IReadOnlyList<AllergyItem>? items, HealthBackgroundSource? source)
    {
        items ??= [];
        var allergies = items.Count(i => i.Type == AllergyItemType.Alergia);
        if (!Enum.IsDefined(state) || items.Count > MaxItems || (source is { } s && !Enum.IsDefined(s))
            || (state == HealthBackgroundState.ConEntradas ? allergies == 0 : allergies > 0)
            || items.GroupBy(i => (i.Type, Key: i.Name.ToUpperInvariant())).Any(g => g.Count() > 1))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }

        State = state;
        Items = items;
        Source = source;
    }
}

public enum ConditionItemType
{
    [Code("ENFERMEDAD")] [Display(Name = "Enfermedad")] Enfermedad,
    [Code("INTERVENCION")] [Display(Name = "Intervención quirúrgica")] Intervencion,
}

/// <summary>Un elemento de la lista de enfermedades e intervenciones (script 0096): un nombre corto de una sola línea y, si se sabe, el año (CJ, medicina-alergias-y-plazos:
/// «HTA, DM2, IAM en 2015, IQ apendicectomía en 1999»). El año es opcional: «año desconocido» es una respuesta válida (DPO, apartado 1.3); no puede ser futuro.</summary>
public sealed record ConditionItem
{
    public const int MaxNameLength = 100;
    public const int MinYear = 1900;

    public ConditionItemType Type { get; }
    public string Name { get; }
    public int? Year { get; }

    public ConditionItem(ConditionItemType type, string? name, int? year)
    {
        name = VitalSigns.Normalize(name);
        if (!Enum.IsDefined(type) || name is null || name.Length > MaxNameLength || name.Any(HealthBackground.IsLineBreakOrControl)
            || (year is { } y && (y < MinYear || y > CenterTime.Now.Year)))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }

        Type = type;
        Name = name;
        Year = year;
    }
}

/// <summary>El contenido de cada versión del apartado de enfermedades e intervenciones: el estado, la lista y la fuente opcional. «Con entradas» exige al menos un elemento;
/// «Refiere que no hay» y «No consta» no admiten ninguno (a diferencia de las alergias, que admiten intolerancias).</summary>
public sealed record ConditionRecordContent
{
    public const int MaxItems = 100;

    public HealthBackgroundState State { get; }
    public IReadOnlyList<ConditionItem> Items { get; }
    public HealthBackgroundSource? Source { get; }

    public ConditionRecordContent(HealthBackgroundState state, IReadOnlyList<ConditionItem>? items, HealthBackgroundSource? source)
    {
        items ??= [];
        if (!Enum.IsDefined(state) || items.Count > MaxItems || (source is { } s && !Enum.IsDefined(s))
            || (state == HealthBackgroundState.ConEntradas ? items.Count == 0 : items.Count > 0)
            || items.GroupBy(i => (i.Type, Key: i.Name.ToUpperInvariant(), i.Year)).Any(g => g.Count() > 1))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }

        State = state;
        Items = items;
        Source = source;
    }
}

/// <summary>Un medicamento de la lista de medicación habitual (script 0097): nombre, dosis y pauta, en texto corto de una sola línea (ejemplo inventado: «Fármaco Alfa», «1 g», «cada 8 h»).
/// La dosis y la pauta son obligatorias; cuando no se saben se escribe «no consta». No hay vía, fechas, prescriptor, motivo ni adherencia: la lista no es administración de medicación.</summary>
public sealed record MedicationItem
{
    public const int MaxNameLength = 100;
    public const int MaxDoseLength = 50;
    public const int MaxScheduleLength = 100;

    public string Name { get; }
    public string Dose { get; }
    public string Schedule { get; }

    public MedicationItem(string? name, string? dose, string? schedule)
    {
        name = VitalSigns.Normalize(name);
        dose = VitalSigns.Normalize(dose);
        schedule = VitalSigns.Normalize(schedule);
        if (name is null || dose is null || schedule is null || !name.Any(char.IsLetterOrDigit)
            || name.Length > MaxNameLength || dose.Length > MaxDoseLength || schedule.Length > MaxScheduleLength
            || name.Any(HealthBackground.IsLineBreakOrControl) || dose.Any(HealthBackground.IsLineBreakOrControl) || schedule.Any(HealthBackground.IsLineBreakOrControl)
            // La dosis y la pauta llevan al menos una letra o un número: «-» o «?» taparían el hueco que «no consta» debe declarar.
            || !dose.Any(char.IsLetterOrDigit) || !schedule.Any(char.IsLetterOrDigit))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }

        Name = name;
        Dose = dose;
        Schedule = schedule;
    }
}

/// <summary>El contenido de cada versión del apartado de medicación habitual: el estado, la lista y la fuente opcional. «Con entradas» exige al menos un medicamento; «No toma medicación
/// habitual» y «No consta» no admiten ninguno.</summary>
public sealed record MedicationRecordContent
{
    public const int MaxItems = 60;

    public HealthBackgroundState State { get; }
    public IReadOnlyList<MedicationItem> Items { get; }
    public HealthBackgroundSource? Source { get; }

    public MedicationRecordContent(HealthBackgroundState state, IReadOnlyList<MedicationItem>? items, HealthBackgroundSource? source)
    {
        items ??= [];
        if (!Enum.IsDefined(state) || items.Count > MaxItems || (source is { } s && !Enum.IsDefined(s))
            || (state == HealthBackgroundState.ConEntradas ? items.Count == 0 : items.Count > 0)
            || items.GroupBy(i => (Name: i.Name.ToUpperInvariant(), Dose: i.Dose.ToUpperInvariant(), Schedule: i.Schedule.ToUpperInvariant())).Any(g => g.Count() > 1))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }

        State = state;
        Items = items;
        Source = source;
    }
}

public static class HealthBackground
{
    public const string InvalidCode = "HEALTH_BACKGROUND_INVALID";
    public const string RevisionConflictCode = "HEALTH_BACKGROUND_REVISION_CONFLICT";
    public const string AllergiesSection = "ALERGIAS";
    public const string ConditionsSection = "ENFERMEDADES";
    public const string MedicationSection = "MEDICACION";
    public const string MedicationItemCode = "MEDICAMENTO";

    /// <summary>Un carácter de control o un separador de línea o de párrafo Unicode (U+2028, U+2029), que char.IsControl no cubre: el texto es de una sola línea.</summary>
    internal static bool IsLineBreakOrControl(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
}
