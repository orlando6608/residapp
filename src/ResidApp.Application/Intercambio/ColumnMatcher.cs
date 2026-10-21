using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ResidApp.Domain.Residents;

namespace ResidApp.Application.Intercambio;

/// <summary>Un fichero leído: la fila de cabecera y las filas con datos (las vacías no se incluyen). Number es la fila en el fichero,
/// para que los avisos coincidan con lo que se ve en Excel. Cada fila tiene tantas celdas como cabeceras, ya recortadas.</summary>
public sealed record ExchangeTable(IReadOnlyList<string> Headers, IReadOnlyList<ExchangeTableRow> Rows)
{
    public IEnumerable<string> Column(int index) => Rows.Select(r => r.Cells[index]);
}

public sealed record ExchangeTableRow(int Number, IReadOnlyList<string> Cells);

/// <summary>Límites de un fichero de importación (ADR 0010): de sobra para un centro, y acotan memoria y tiempo de la petición.</summary>
public static class ExchangeLimits
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxRows = 2000;
    public const int MaxColumns = 60;
    public const int MaxSexValues = 20;
    public const int MaxTemplateNameLength = 100;
}

/// <summary>Qué parecen contener los datos de una columna; se enseña en lugar de los valores (minimización).</summary>
public enum ColumnKind
{
    [Display(Name = "Vacía")] Vacia,
    [Display(Name = "Fechas")] Fechas,
    [Display(Name = "Números")] Numeros,
    [Display(Name = "Texto")] Texto,
}

/// <summary>Por qué se propuso el campo de una columna sin que nadie lo eligiera.</summary>
public enum MatchOrigin
{
    [Display(Name = "por la plantilla guardada")] Plantilla,
    [Display(Name = "por el nombre de la columna")] Nombre,
    [Display(Name = "por el formato de los datos")] Formato,
}

public sealed record ColumnMatch(ExchangeField? Field, MatchOrigin? Origin);

/// <summary>
/// ADR 0010: emparejamiento automático y determinista de las columnas de un fichero con los campos de ResidApp, en este orden:
///   1. por el nombre de la columna: igual a un sinónimo del catálogo, o que lo contiene como palabras seguidas (gana el sinónimo más
///      largo; si empatan dos campos distintos, no se empareja);
///   2. por el formato de los datos, para lo que quede: la única columna con todo fechas es la fecha de nacimiento, y la única con
///      pocos valores, todos de sexo, es el sexo.
/// Un campo que no admite varias columnas se queda con la mejor; lo demás queda sin emparejar y se elige a mano. La plantilla
/// guardada, si la hay, se aplica antes y en lugar de esto (lo decide el servicio).
/// </summary>
public static class ColumnMatcher
{
    public static IReadOnlyList<ColumnMatch> Match(ExchangeEntity entity, ExchangeTable table, DateOnly today)
    {
        var specs = ExchangeCatalog.Fields(entity);
        var best = table.Headers.Select(header => BestByName(specs, ExchangeText.Normalize(header))).ToArray();

        // Un campo de una sola columna se queda con la de mejor puntuación (la primera si empatan).
        foreach (var spec in specs.Where(s => !s.Joinable))
        {
            var claimants = Enumerable.Range(0, best.Length).Where(i => best[i]?.Field == spec.Field).ToList();
            foreach (var loser in claimants.OrderByDescending(i => best[i]!.Value.Score).ThenBy(i => i).Skip(1))
            {
                best[loser] = null;
            }
        }

        var matches = best.Select(b => b is { } m ? new ColumnMatch(m.Field, MatchOrigin.Nombre) : new ColumnMatch(null, null)).ToArray();
        if (entity == ExchangeEntity.Residentes)
        {
            MatchByFormat(matches, table, ExchangeField.FechaNacimiento, i => LooksLikeDates(table.Column(i), today));
            MatchByFormat(matches, table, ExchangeField.Sexo, i => LooksLikeSex(table.Column(i)));
        }

        return matches;
    }

    /// <summary>Qué parecen contener los datos de la columna.</summary>
    public static ColumnKind Kind(IEnumerable<string> values, DateOnly today)
    {
        var filled = values.Where(v => v.Length > 0).ToList();
        if (filled.Count == 0)
        {
            return ColumnKind.Vacia;
        }

        if (LooksLikeDates(filled, today))
        {
            return ColumnKind.Fechas;
        }

        return filled.All(v => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) ? ColumnKind.Numeros : ColumnKind.Texto;
    }

    private static (ExchangeField Field, int Score)? BestByName(IReadOnlyList<ExchangeFieldSpec> specs, string header)
    {
        if (header.Length == 0)
        {
            return null;
        }

        var tokens = header.Split(' ');
        var scored = specs
            .SelectMany(spec => spec.Synonyms.Select(synonym => (spec.Field, Score: Score(tokens, header, synonym))))
            .Where(s => s.Score > 0)
            .GroupBy(s => s.Field)
            .Select(g => (Field: g.Key, Score: g.Max(s => s.Score)))
            .OrderByDescending(s => s.Score)
            .ToList();
        return scored.Count == 0 || (scored.Count > 1 && scored[0].Score == scored[1].Score) ? null : scored[0];
    }

    /// <summary>1000 si la cabecera es el sinónimo (dos columnas así empatan y gana la primera); las palabras del sinónimo si lo contiene
    /// seguidas; 0 si no.</summary>
    private static int Score(string[] tokens, string header, string synonym)
    {
        if (header == synonym)
        {
            return 1000;
        }

        var words = synonym.Split(' ');
        for (var start = 0; start + words.Length <= tokens.Length; start++)
        {
            if (words.Select((w, i) => tokens[start + i] == w).All(equal => equal))
            {
                return words.Length;
            }
        }

        return 0;
    }

    private static void MatchByFormat(ColumnMatch[] matches, ExchangeTable table, ExchangeField field, Func<int, bool> looksLike)
    {
        if (matches.Any(m => m.Field == field))
        {
            return;
        }

        var candidates = Enumerable.Range(0, matches.Length).Where(i => matches[i].Field is null && looksLike(i)).ToList();
        if (candidates.Count == 1)
        {
            matches[candidates[0]] = new ColumnMatch(field, MatchOrigin.Formato);
        }
    }

    private static bool LooksLikeDates(IEnumerable<string> values, DateOnly today)
    {
        var filled = values.Where(v => v.Length > 0).ToList();
        return filled.Count > 0 && ExchangeDates.Candidates(filled, today).Count > 0;
    }

    private static bool LooksLikeSex(IEnumerable<string> values)
    {
        var keys = values.Select(ExchangeText.Normalize).Where(k => k.Length > 0).Distinct().ToList();
        return keys.Count is > 0 and <= 4 && keys.All(k => ExchangeSex.Suggest(k, keys) is not null);
    }
}

/// <summary>Un valor distinto de la columna de sexo, con su clave normalizada y el sexo documentado que le corresponde (null si falta
/// asignarlo).</summary>
public sealed record SexValue(string Value, string Key, DocumentedSexCode? Code);
