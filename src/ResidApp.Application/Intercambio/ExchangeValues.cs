using System.Globalization;
using ResidApp.Domain.Residents;

namespace ResidApp.Application.Intercambio;

/// <summary>Lectura de fechas del fichero (ADR 0010). Se queda con la parte de la fecha (descarta una hora detrás) y acepta «/», «-» o
/// «.» como separador. El año de dos cifras es del siglo que no deja la fecha en el futuro: «05/03/40» es 1940.</summary>
public static class ExchangeDates
{
    public static DateOnly? Parse(string? value, ExchangeDateFormat format, DateOnly today)
    {
        var token = value?.Trim().Split(' ', 'T')[0] ?? "";
        var parts = token.Split('/', '-', '.');
        if (parts.Length != 3 || parts.Any(p => p.Length is 0 or > 4 || !p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        int year, month, day;
        if (parts[0].Length == 4)
        {
            (year, month, day) = (numbers[0], numbers[1], numbers[2]);
        }
        else if (format != ExchangeDateFormat.AnioMesDia && parts[2].Length is 2 or 4 && parts[0].Length <= 2 && parts[1].Length <= 2)
        {
            year = numbers[2];
            (day, month) = format == ExchangeDateFormat.DiaMesAnio ? (numbers[0], numbers[1]) : (numbers[1], numbers[0]);
            if (parts[2].Length == 2)
            {
                year += 2000;
                if (year > today.Year)
                {
                    year -= 100;
                }
            }
        }
        else
        {
            return null;
        }

        return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;
    }

    /// <summary>Los formatos con los que se leen todas las fechas no vacías, en orden de preferencia (primero el español).</summary>
    public static IReadOnlyList<ExchangeDateFormat> Candidates(IEnumerable<string> values, DateOnly today)
    {
        var filled = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return filled.Count == 0
            ? Enum.GetValues<ExchangeDateFormat>()
            : Enum.GetValues<ExchangeDateFormat>().Where(f => filled.All(v => Parse(v, f, today) is not null)).ToList();
    }

    /// <summary>Hay alguna fecha que da un día distinto leída como día/mes/año que como mes/día/año (todas con el día hasta 12).</summary>
    public static bool Ambiguous(IEnumerable<string> values, DateOnly today) =>
        values.Any(v => Parse(v, ExchangeDateFormat.DiaMesAnio, today) is { } dmy
                        && Parse(v, ExchangeDateFormat.MesDiaAnio, today) is { } mdy && dmy != mdy);

    public static string Format(DateOnly date, ExchangeDateFormat format) => date.ToString(format switch
    {
        ExchangeDateFormat.MesDiaAnio => "MM/dd/yyyy",
        ExchangeDateFormat.AnioMesDia => "yyyy-MM-dd",
        _ => "dd/MM/yyyy",
    }, CultureInfo.InvariantCulture);
}

/// <summary>El sexo documentado a partir de los valores de la columna del fichero. Nunca se infiere de otro dato (DocumentedSexCode):
/// un valor que no se reconoce lo asigna a mano quien importa, y una celda vacía es «No consta».</summary>
public static class ExchangeSex
{
    private static readonly Dictionary<string, DocumentedSexCode> Known = new()
    {
        ["h"] = DocumentedSexCode.Hombre, ["hombre"] = DocumentedSexCode.Hombre, ["v"] = DocumentedSexCode.Hombre,
        ["varon"] = DocumentedSexCode.Hombre, ["masculino"] = DocumentedSexCode.Hombre, ["masc"] = DocumentedSexCode.Hombre,
        ["male"] = DocumentedSexCode.Hombre,
        ["mujer"] = DocumentedSexCode.Mujer, ["f"] = DocumentedSexCode.Mujer, ["femenino"] = DocumentedSexCode.Mujer,
        ["fem"] = DocumentedSexCode.Mujer, ["female"] = DocumentedSexCode.Mujer,
        ["otro"] = DocumentedSexCode.OtraCategoriaDocumentada, ["otra"] = DocumentedSexCode.OtraCategoriaDocumentada,
        ["x"] = DocumentedSexCode.OtraCategoriaDocumentada, ["no binario"] = DocumentedSexCode.OtraCategoriaDocumentada,
        ["other"] = DocumentedSexCode.OtraCategoriaDocumentada,
        ["no consta"] = DocumentedSexCode.NoConsta, ["nc"] = DocumentedSexCode.NoConsta, ["nd"] = DocumentedSexCode.NoConsta,
        ["desconocido"] = DocumentedSexCode.NoConsta, ["sin dato"] = DocumentedSexCode.NoConsta, ["sin datos"] = DocumentedSexCode.NoConsta,
        ["unknown"] = DocumentedSexCode.NoConsta,
    };

    /// <summary>El valor reconocido para una clave normalizada, o null. «M» es Mujer (H/M, V/M) salvo que la columna use también «F»:
    /// entonces es Masculino (M/F).</summary>
    public static DocumentedSexCode? Suggest(string key, IReadOnlyCollection<string> columnKeys) =>
        key == "m"
            ? columnKeys.Contains("f") ? DocumentedSexCode.Hombre : DocumentedSexCode.Mujer
            : Known.TryGetValue(key, out var code) ? code : null;
}
