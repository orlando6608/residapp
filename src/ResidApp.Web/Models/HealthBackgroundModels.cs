using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>Una fila del formulario de alergias: lo que escribe quien rellena la ficha. Una fila con el nombre y la reacción en blanco se ignora.</summary>
public sealed class AllergyRowModel
{
    public string? Tipo { get; set; }
    public string? Nombre { get; set; }
    public string? Reaccion { get; set; }
}

/// <summary>El formulario de alergias e intolerancias (ficha de antecedentes de salud, fase 1; script 0094). VersionVista es la versión vigente cuando se abrió
/// la pantalla (0 si estaba pendiente): si otra persona guarda mientras tanto, no se pisa. Estado, Fuente y Motivo son códigos de lista cerrada.</summary>
public sealed class AllergyFormModel
{
    public Guid ResidenteId { get; set; }
    public int VersionVista { get; set; }
    public string? Estado { get; set; }
    public string? Fuente { get; set; }
    public string? Motivo { get; set; }
    public List<AllergyRowModel> Filas { get; set; } = [];

    /// <summary>Convierte lo escrito en el contenido de la versión, o lanza DomainValidationException si no es válido.</summary>
    public AllergyRecordContent ToContent()
    {
        if (!EnumCode.TryParseCode<HealthBackgroundState>(Estado ?? "", out var state))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }
        HealthBackgroundSource? source = null;
        if (!string.IsNullOrWhiteSpace(Fuente))
        {
            source = EnumCode.TryParseCode<HealthBackgroundSource>(Fuente, out var parsed) ? parsed : throw new DomainValidationException(HealthBackground.InvalidCode);
        }
        var items = Filas
            .Where(row => !string.IsNullOrWhiteSpace(row.Nombre) || !string.IsNullOrWhiteSpace(row.Reaccion))
            .Select(row => new AllergyItem(
                EnumCode.TryParseCode<AllergyItemType>(row.Tipo ?? "", out var type) ? type : throw new DomainValidationException(HealthBackground.InvalidCode),
                row.Nombre, row.Reaccion))
            .ToList();
        return new AllergyRecordContent(state, items, source);
    }

    public HealthBackgroundReason? ReasonOrNull() =>
        EnumCode.TryParseCode<HealthBackgroundReason>(Motivo ?? "", out var reason) ? reason : null;

    public static AllergyFormModel From(Guid residentId, AllergyRecord? record) => new()
    {
        ResidenteId = residentId,
        VersionVista = record?.Version ?? 0,
        Estado = record?.Content.State.ToCode(),
        Fuente = record?.Content.Source?.ToCode(),
        Filas = record?.Content.Items.Select(i => new AllergyRowModel { Tipo = i.Type.ToCode(), Nombre = i.Name, Reaccion = i.Reaction }).ToList() ?? [],
    };
}

/// <summary>La pantalla del formulario: quién es el residente (nombre y foto, para comprobar que es la persona correcta antes de guardar) y el formulario.</summary>
public sealed record AllergyEditViewModel(ScopeResidentSummary Resident, AllergyFormModel Form, string? Error, string RecordController);

/// <summary>Una fila del formulario de enfermedades e intervenciones. El año va en su casilla, no dentro del nombre. Una fila con el nombre y el año en blanco se ignora.</summary>
public sealed class ConditionRowModel
{
    public string? Tipo { get; set; }
    public string? Nombre { get; set; }
    public string? Anio { get; set; }
}

/// <summary>El formulario de enfermedades e intervenciones (ficha de antecedentes de salud, fase 2; script 0096). Igual que el de alergias: VersionVista es la versión vigente
/// cuando se abrió la pantalla (0 si estaba pendiente); Estado, Fuente y Motivo son códigos de lista cerrada.</summary>
public sealed class ConditionFormModel
{
    public Guid ResidenteId { get; set; }
    public int VersionVista { get; set; }
    public string? Estado { get; set; }
    public string? Fuente { get; set; }
    public string? Motivo { get; set; }
    public List<ConditionRowModel> Filas { get; set; } = [];

    /// <summary>Convierte lo escrito en el contenido de la versión, o lanza DomainValidationException si no es válido (incluido un año que no es un número).</summary>
    public ConditionRecordContent ToContent()
    {
        if (!EnumCode.TryParseCode<HealthBackgroundState>(Estado ?? "", out var state))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }
        HealthBackgroundSource? source = null;
        if (!string.IsNullOrWhiteSpace(Fuente))
        {
            source = EnumCode.TryParseCode<HealthBackgroundSource>(Fuente, out var parsed) ? parsed : throw new DomainValidationException(HealthBackground.InvalidCode);
        }
        var items = Filas
            .Where(row => !string.IsNullOrWhiteSpace(row.Nombre) || !string.IsNullOrWhiteSpace(row.Anio))
            .Select(row => new ConditionItem(
                EnumCode.TryParseCode<ConditionItemType>(row.Tipo ?? "", out var type) ? type : throw new DomainValidationException(HealthBackground.InvalidCode),
                row.Nombre, ParseYear(row.Anio)))
            .ToList();
        return new ConditionRecordContent(state, items, source);
    }

    private static int? ParseYear(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year) ? year
        : throw new DomainValidationException(HealthBackground.InvalidCode);

    public HealthBackgroundReason? ReasonOrNull() =>
        EnumCode.TryParseCode<HealthBackgroundReason>(Motivo ?? "", out var reason) ? reason : null;

    public static ConditionFormModel From(Guid residentId, ConditionRecord? record) => new()
    {
        ResidenteId = residentId,
        VersionVista = record?.Version ?? 0,
        Estado = record?.Content.State.ToCode(),
        Fuente = record?.Content.Source?.ToCode(),
        Filas = record?.Content.Items.Select(i => new ConditionRowModel { Tipo = i.Type.ToCode(), Nombre = i.Name, Anio = i.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToList() ?? [],
    };
}

public sealed record ConditionEditViewModel(ScopeResidentSummary Resident, ConditionFormModel Form, string? Error, string RecordController);

/// <summary>Una fila del formulario de medicación habitual. Una fila con el nombre, la dosis y la pauta en blanco se ignora.</summary>
public sealed class MedicationRowModel
{
    public string? Nombre { get; set; }
    public string? Dosis { get; set; }
    public string? Pauta { get; set; }
}

/// <summary>El formulario de medicación habitual (ficha de antecedentes de salud, fase 3; script 0097). Igual que los otros: VersionVista es la versión vigente cuando se abrió la
/// pantalla (0 si estaba pendiente); Estado, Fuente y Motivo son códigos de lista cerrada.</summary>
public sealed class MedicationFormModel
{
    public Guid ResidenteId { get; set; }
    public int VersionVista { get; set; }
    public string? Estado { get; set; }
    public string? Fuente { get; set; }
    public string? Motivo { get; set; }
    public List<MedicationRowModel> Filas { get; set; } = [];

    /// <summary>Convierte lo escrito en el contenido de la versión, o lanza DomainValidationException si no es válido (incluida una fila sin dosis o sin pauta).</summary>
    public MedicationRecordContent ToContent()
    {
        if (!EnumCode.TryParseCode<HealthBackgroundState>(Estado ?? "", out var state))
        {
            throw new DomainValidationException(HealthBackground.InvalidCode);
        }
        HealthBackgroundSource? source = null;
        if (!string.IsNullOrWhiteSpace(Fuente))
        {
            source = EnumCode.TryParseCode<HealthBackgroundSource>(Fuente, out var parsed) ? parsed : throw new DomainValidationException(HealthBackground.InvalidCode);
        }
        var items = Filas
            .Where(row => !string.IsNullOrWhiteSpace(row.Nombre) || !string.IsNullOrWhiteSpace(row.Dosis) || !string.IsNullOrWhiteSpace(row.Pauta))
            .Select(row => new MedicationItem(row.Nombre, row.Dosis, row.Pauta))
            .ToList();
        return new MedicationRecordContent(state, items, source);
    }

    public HealthBackgroundReason? ReasonOrNull() =>
        EnumCode.TryParseCode<HealthBackgroundReason>(Motivo ?? "", out var reason) ? reason : null;

    public static MedicationFormModel From(Guid residentId, MedicationRecord? record) => new()
    {
        ResidenteId = residentId,
        VersionVista = record?.Version ?? 0,
        Estado = record?.Content.State.ToCode(),
        Fuente = record?.Content.Source?.ToCode(),
        Filas = record?.Content.Items.Select(i => new MedicationRowModel { Nombre = i.Name, Dosis = i.Dose, Pauta = i.Schedule }).ToList() ?? [],
    };
}

/// <summary>LastModified es la fecha y el perfil de la versión vigente, visibles antes de editar (revisión del DPO: la edad del dato no se esconde); null si está pendiente.</summary>
public sealed record MedicationEditViewModel(ScopeResidentSummary Resident, MedicationFormModel Form, string? Error, string RecordController, string? LastModified = null);

/// <summary>El bloque «Antecedentes de salud» de la ficha del residente. View es null si no se pudo leer (el bloque entonces no se enseña).</summary>
public sealed record HealthBackgroundCardModel(Guid ResidentId, HealthBackgroundView View);

/// <summary>Los textos de la ficha. «Pendiente», «No refiere» y «No consta» se dicen distinto y ninguno equivale a «sin alergias» salvo «No refiere» (CJ,
/// medicina-ficha-aclaraciones 1.1). No dice «revisado»: la ficha no se revisa de forma periódica; dice cuándo y quién la modificó por última vez.</summary>
public static class HealthBackgroundDisplay
{
    public const string AllergiesTitle = "Alergias e intolerancias a medicamentos";

    public const string Pending =
        "Ficha pendiente de rellenar en este centro: no hay datos registrados. No equivale a «sin alergias».";

    /// <summary>Lo que dice el informe de derivación a Urgencias sobre las alergias (revisión del DPO, 2026-11-15, apartado 4.3; CJ, aclaración 1.1): «No refiere» y
    /// «No consta» salen distintos, ninguno de los demás equivale a «sin alergias», y no dice «revisado» sino cuándo y quién la modificó por última vez. Null
    /// es que no se pudo leer la ficha (o es incoherente): se dice, no se imprime como válida.</summary>
    public static IReadOnlyList<string> ReportLines(HealthBackgroundView? view)
    {
        if (view is null || view.UnreliableVersion is not null)
        {
            return ["No se ha podido leer la ficha de alergias: este dato no es fiable. Contrasta con el residente o su familia y con la hoja de tratamiento oficial del centro."];
        }
        if (view.Allergies is not { } record)
        {
            return [Pending];
        }

        var lines = new List<string>
        {
            record.Content.State switch
            {
                HealthBackgroundState.NoRefiere => "No refiere alergias a medicamentos.",
                HealthBackgroundState.NoConsta => "No consta: información no fiable o sin confirmar. No equivale a «sin alergias».",
                _ => "Tiene alergias a medicamentos:",
            },
        };
        lines.AddRange(record.Content.Items.Select(Item));
        if (Source(record.Content.Source) is { } source)
        {
            lines.Add(source);
        }
        lines.Add($"Registrado en la ficha del residente (versión {record.Version}); última modificación el {CenterTime.Local(record.ModifiedAt):dd'/'MM'/'yyyy HH':'mm} por {SystemProfileDisplay.Label(record.ModifiedByProfile)}. " +
            "Esta ficha no se revisa de forma periódica. Contrastar con la hoja de tratamiento oficial del centro y con el residente o su familia.");
        return lines;
    }

    public static string State(HealthBackgroundState state) => state switch
    {
        HealthBackgroundState.NoRefiere => "No refiere alergias a medicamentos.",
        HealthBackgroundState.NoConsta => "No se sabe o no consta: información no fiable o sin confirmar. No equivale a «sin alergias».",
        _ => "Tiene alergias a medicamentos:",
    };

    public static string Item(AllergyItem item) => item.Type == AllergyItemType.Alergia
        ? $"Alergia a: {item.Name}{(item.Reaction is null ? "" : $" ({item.Reaction})")}"
        : $"Intolerancia o efecto adverso: {item.Name} ({item.Reaction})";

    public static string LastModified(AllergyRecord record) =>
        $"Última modificación el {CenterTime.Local(record.ModifiedAt):dd'/'MM'/'yyyy HH':'mm} por {SystemProfileDisplay.Label(record.ModifiedByProfile)}. " +
        "Esta ficha no se revisa de forma periódica: se actualiza cuando ocurre algo.";

    public static string? Source(HealthBackgroundSource? source) => source is { } value ? $"Fuente: {EnumDisplay.Label(value).ToLowerInvariant()}." : null;

    public const string ConditionsTitle = "Enfermedades e intervenciones";

    public const string ConditionsPending =
        "Ficha pendiente de rellenar en este centro: no hay datos registrados. No equivale a «sin enfermedades».";

    public static string ConditionsState(HealthBackgroundState state) => state switch
    {
        HealthBackgroundState.NoRefiere => "Sin enfermedades ni intervenciones conocidas.",
        HealthBackgroundState.NoConsta => "No se sabe o no consta: información no fiable o sin confirmar. No equivale a «sin enfermedades».",
        _ => "Enfermedades e intervenciones:",
    };

    public static string ConditionLine(ConditionItem item) =>
        $"{(item.Type == ConditionItemType.Enfermedad ? "Enfermedad" : "Intervención")}: {item.Name}{(item.Year is { } year ? $" ({year})" : "")}";

    public static string ConditionsLastModified(ConditionRecord record) =>
        $"Última modificación el {CenterTime.Local(record.ModifiedAt):dd'/'MM'/'yyyy HH':'mm} por {SystemProfileDisplay.Label(record.ModifiedByProfile)}. " +
        "Esta ficha no se revisa de forma periódica: se actualiza cuando ocurre algo.";

    /// <summary>Lo que dice el informe de derivación sobre las enfermedades e intervenciones: la lista completa (CJ: «no importa que crezca»), con la misma cautela que las
    /// alergias: no dice «revisado», dice cuándo y quién la modificó por última vez, y una ficha ilegible se dice, no se imprime como válida.</summary>
    public static IReadOnlyList<string> ConditionsReportLines(HealthBackgroundView? view)
    {
        if (view is null || view.UnreliableConditionsVersion is not null)
        {
            return ["No se ha podido leer la ficha de enfermedades e intervenciones: este dato no es fiable. Contrasta con el residente o su familia y con la documentación clínica del centro."];
        }
        if (view.Conditions is not { } record)
        {
            return [ConditionsPending];
        }

        var lines = new List<string> { ConditionsState(record.Content.State) };
        lines.AddRange(record.Content.Items.Select(ConditionLine));
        if (Source(record.Content.Source) is { } source)
        {
            lines.Add(source);
        }
        lines.Add($"Registrado en la ficha del residente (versión {record.Version}); última modificación el {CenterTime.Local(record.ModifiedAt):dd'/'MM'/'yyyy HH':'mm} por {SystemProfileDisplay.Label(record.ModifiedByProfile)}. " +
            "Esta ficha no se revisa de forma periódica. Contrastar con la documentación clínica del centro y con el residente o su familia.");
        return lines;
    }

    public const string MedicationTitle = "Medicación habitual";

    public const string MedicationPending =
        "Ficha pendiente de rellenar en este centro: no hay datos registrados. No equivale a «no toma medicación».";

    /// <summary>Texto fijo de la medicación, en pantalla y en el informe (revisión del DPO, 2026-11-16): es una copia y no sustituye a la fuente oficial.</summary>
    public const string MedicationCopyNotice =
        "Copia informativa: la fuente es la receta electrónica o la hoja de tratamiento oficial del centro; si no coinciden, manda la oficial.";

    public static string MedicationState(HealthBackgroundState state) => state switch
    {
        HealthBackgroundState.NoRefiere => "No refiere medicación habitual.",
        HealthBackgroundState.NoConsta => "No se sabe o no consta: información no fiable o sin confirmar. No equivale a que no tome medicación.",
        _ => "Medicación habitual:",
    };

    public static string MedicationLine(MedicationItem item) => $"{item.Name}, {item.Dose}, {item.Schedule}";

    public static string MedicationLastModified(MedicationRecord record) =>
        $"Última modificación el {CenterTime.Local(record.ModifiedAt):dd'/'MM'/'yyyy HH':'mm} por {SystemProfileDisplay.Label(record.ModifiedByProfile)}. " +
        "Esta ficha no se revisa de forma periódica: se actualiza cuando ocurre algo. No sustituye a la receta electrónica ni a la hoja de tratamiento oficial del centro.";

    /// <summary>Lo que dice el informe de derivación sobre la medicación habitual: la lista completa (nombre, dosis y pauta), con la cautela de que es una copia y no sustituye a la
    /// receta electrónica ni a la hoja de tratamiento oficial; no dice «revisado», dice cuándo y quién la modificó por última vez, y una ficha ilegible se dice, no se imprime como válida.</summary>
    public static IReadOnlyList<string> MedicationReportLines(HealthBackgroundView? view)
    {
        if (view is null || view.UnreliableMedicationVersion is not null)
        {
            return ["No se ha podido leer la ficha de medicación habitual: este dato no es fiable. Contrasta con la receta electrónica o la hoja de tratamiento oficial del centro y con el residente o su familia."];
        }
        if (view.Medication is not { } record)
        {
            return [MedicationPending];
        }

        var lines = new List<string> { MedicationState(record.Content.State), MedicationCopyNotice };
        lines.AddRange(record.Content.Items.Select(MedicationLine));
        if (Source(record.Content.Source) is { } source)
        {
            lines.Add(source);
        }
        lines.Add($"Registrado en la ficha del residente (versión {record.Version}); última modificación el {CenterTime.Local(record.ModifiedAt):dd'/'MM'/'yyyy HH':'mm} por {SystemProfileDisplay.Label(record.ModifiedByProfile)}. " +
            "Esta ficha no se revisa de forma periódica. Contrastar con la receta electrónica o la hoja de tratamiento oficial del centro y con el residente o su familia.");
        return lines;
    }
}
