using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ResidApp.Shared;

namespace ResidApp.Application.Intercambio;

/// <summary>Qué se importa o se exporta (ADR 0010): solo datos administrativos, nunca contenido clínico.</summary>
public enum ExchangeEntity
{
    [Code("STRUCTURE")] [Display(Name = "Estructura del centro")] Estructura,
    [Code("RESIDENTS")] [Display(Name = "Residentes")] Residentes,
}

public enum ExchangeDirection
{
    [Code("IMPORT")] Importar,
    [Code("EXPORT")] Exportar,
}

/// <summary>Los campos de ResidApp a los que puede ir una columna de un fichero de otra aplicación.</summary>
public enum ExchangeField
{
    [Code("UNIT")] [Display(Name = "Unidad")] Unidad,
    [Code("UNIT_CODE")] [Display(Name = "Código de unidad")] CodigoUnidad,
    [Code("BUILDING")] [Display(Name = "Edificio")] Edificio,
    [Code("FLOOR")] [Display(Name = "Planta")] Planta,
    [Code("ROOM")] [Display(Name = "Habitación")] Habitacion,
    [Code("PLACE")] [Display(Name = "Plaza (cama)")] Plaza,
    [Code("NAME")] [Display(Name = "Nombre")] Nombre,
    [Code("SURNAMES")] [Display(Name = "Apellidos")] Apellidos,
    [Code("BIRTH_DATE")] [Display(Name = "Fecha de nacimiento")] FechaNacimiento,
    [Code("SEX")] [Display(Name = "Sexo")] Sexo,
    [Code("INTERNAL_REFERENCE")] [Display(Name = "Referencia interna")] ReferenciaInterna,
}

/// <summary>Cómo se escriben las fechas del fichero. Al importar, día/mes/año y mes/día/año aceptan además año-mes-día (1940-12-31),
/// que no es ambiguo y es como llegan las fechas de las celdas de Excel.</summary>
public enum ExchangeDateFormat
{
    [Code("DMY")] [Display(Name = "Día/mes/año (31/12/1940)")] DiaMesAnio,
    [Code("MDY")] [Display(Name = "Mes/día/año (12/31/1940, formato de EE. UU.)")] MesDiaAnio,
    [Code("YMD")] [Display(Name = "Año-mes-día (1940-12-31)")] AnioMesDia,
}

/// <summary>Un campo de una entidad. Joinable: varias columnas pueden ir a él y se unen con un espacio (nombre y apellidos
/// en columnas separadas). Synonyms son nombres de columna ya normalizados (<see cref="ExchangeText.Normalize"/>).</summary>
public sealed record ExchangeFieldSpec(ExchangeField Field, bool Required, bool Joinable, IReadOnlyList<string> Synonyms);

/// <summary>
/// ADR 0010: los campos de cada entidad y los nombres de columna con que suelen llegar de otras aplicaciones. El emparejamiento es
/// determinista (sin IA): si un ERP usa un nombre que no está aquí, se elige a mano y se guarda en una plantilla, y si se repite en
/// varios centros se añade a esta lista.
/// </summary>
public static class ExchangeCatalog
{
    private static readonly string[] UnitSynonyms =
        ["unidad", "nombre unidad", "nombre de la unidad", "unidad asistencial", "unidad de convivencia", "modulo", "ala", "area", "sector", "zona"];

    private static readonly string[] BuildingSynonyms = ["edificio", "bloque", "pabellon", "torre", "inmueble"];

    private static readonly string[] FloorSynonyms = ["planta", "piso", "nivel"];

    private static readonly string[] RoomSynonyms =
        ["habitacion", "hab", "habit", "n habitacion", "no habitacion", "num habitacion", "numero habitacion", "numero de habitacion", "cuarto", "dormitorio"];

    private static readonly string[] PlaceSynonyms =
        ["plaza", "cama", "plaza cama", "n cama", "no cama", "num cama", "numero cama", "numero de cama", "n plaza", "numero plaza"];

    private static readonly IReadOnlyList<ExchangeFieldSpec> Structure =
    [
        new(ExchangeField.Unidad, Required: true, Joinable: false, UnitSynonyms),
        new(ExchangeField.CodigoUnidad, Required: false, Joinable: false,
            ["codigo unidad", "codigo de unidad", "codigo de la unidad", "cod unidad", "id unidad", "codigo"]),
        new(ExchangeField.Edificio, Required: false, Joinable: false, BuildingSynonyms),
        new(ExchangeField.Planta, Required: false, Joinable: false, FloorSynonyms),
        new(ExchangeField.Habitacion, Required: false, Joinable: false, RoomSynonyms),
        new(ExchangeField.Plaza, Required: false, Joinable: false, PlaceSynonyms),
    ];

    private static readonly IReadOnlyList<ExchangeFieldSpec> Residents =
    [
        new(ExchangeField.Nombre, Required: true, Joinable: true,
            ["nombre", "nombre completo", "nombre y apellidos", "apellidos y nombre", "nombre apellidos", "apellidos nombre", "nombre del residente",
             "nombre residente", "residente", "usuario", "nombre usuario", "paciente", "nom"]),
        new(ExchangeField.Apellidos, Required: false, Joinable: true,
            ["apellidos", "apellido", "primer apellido", "segundo apellido", "apellido 1", "apellido 2", "apellido1", "apellido2", "1er apellido",
             "2o apellido"]),
        new(ExchangeField.FechaNacimiento, Required: true, Joinable: false,
            ["fecha nacimiento", "fecha de nacimiento", "f nacimiento", "f nac", "fnac", "fecha nac", "fec nac", "f de nacimiento", "nacimiento",
             "fecha nto", "data naixement", "birth date"]),
        new(ExchangeField.Sexo, Required: false, Joinable: false, ["sexo", "sexo documentado", "genero", "sex", "gender"]),
        new(ExchangeField.Unidad, Required: true, Joinable: false, UnitSynonyms),
        new(ExchangeField.Habitacion, Required: false, Joinable: false, RoomSynonyms),
        new(ExchangeField.Plaza, Required: false, Joinable: false, PlaceSynonyms),
        new(ExchangeField.ReferenciaInterna, Required: false, Joinable: false,
            ["referencia", "referencia interna", "ref", "id", "codigo", "codigo residente", "cod residente", "id residente", "codigo usuario",
             "id usuario", "expediente", "n expediente", "no expediente", "num expediente", "numero expediente", "numero de expediente"]),
    ];

    /// <summary>Los campos que se pueden importar en la entidad, en el orden de la plantilla de ResidApp.</summary>
    public static IReadOnlyList<ExchangeFieldSpec> Fields(ExchangeEntity entity) => entity == ExchangeEntity.Estructura ? Structure : Residents;

    /// <summary>Las columnas de la exportación (y de la plantilla para rellenar) de ResidApp. En residentes no hay «Apellidos»:
    /// ResidApp guarda el nombre completo en un solo campo.</summary>
    public static IReadOnlyList<ExchangeField> ExportFields(ExchangeEntity entity) => entity == ExchangeEntity.Estructura
        ? [ExchangeField.Unidad, ExchangeField.CodigoUnidad, ExchangeField.Edificio, ExchangeField.Planta, ExchangeField.Habitacion, ExchangeField.Plaza]
        : [ExchangeField.Nombre, ExchangeField.FechaNacimiento, ExchangeField.Sexo, ExchangeField.Unidad, ExchangeField.Habitacion, ExchangeField.Plaza,
           ExchangeField.ReferenciaInterna];

    public static string Label(Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();

    /// <summary>La firma de las cabeceras de un fichero: identifica «el mismo fichero de la misma aplicación» para aplicar su plantilla.</summary>
    public static string Signature(IEnumerable<string> headers) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', headers.Select(ExchangeText.Normalize)))));
}

/// <summary>Normalización de textos para comparar nombres de columnas, unidades, habitaciones y valores de catálogo.</summary>
public static class ExchangeText
{
    /// <summary>Minúsculas, sin acentos (también «ñ» → «n», «º» → «o»), con todo lo que no es letra o cifra convertido en un espacio y
    /// los espacios colapsados: «F. Nac.» → «f nac», «Nº Habitación» → «no habitacion».</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text.Normalize(NormalizationForm.FormKD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(c);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Un código de unidad a partir de su nombre (CenterUnit: de 2 a 64 caracteres, letras sin acentos, cifras y «-»):
    /// «Unidad Psicogeriátrica 2» → «UNIDAD-PSICOGERIATRICA-2».</summary>
    public static string UnitCode(string name)
    {
        var code = new StringBuilder();
        foreach (var c in Normalize(name))
        {
            code.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? char.ToUpperInvariant(c) : '-');
        }

        var text = string.Join('-', code.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        text = text.Length > 60 ? text[..60].TrimEnd('-') : text;
        return text.Length >= 2 ? text : $"U-{(text.Length == 0 ? "1" : text)}";
    }
}
