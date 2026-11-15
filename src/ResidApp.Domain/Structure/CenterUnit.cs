using ResidApp.Shared;

namespace ResidApp.Domain.Structure;

/// <summary>ADM-05 (script 0025): los datos de una unidad nueva. Code es el código estable de la unidad en el centro (no
/// cambia); Name, el nombre visible (se puede renombrar). CodeGenerated es que la aplicación derivó el código del nombre
/// (en vez de recibirlo escrito): si ya existe, el repositorio busca el primero libre con -2, -3… y, en un reenvío, el código
/// no forma parte de lo que se compara.</summary>
public sealed record CenterUnitData(string Code, string Name, bool CodeGenerated = false);

/// <summary>
/// ADM-05: reglas de las unidades del centro. El código lleva de 2 a 64 caracteres (letras sin acentos, dígitos y «-» o
/// «_») y se guarda en mayúsculas; el nombre, de 1 a 200 tras recortar.
/// </summary>
public static class CenterUnit
{
    public const int MinCodeLength = 2;
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 200;
    public const string InvalidCode = "UNIT_INVALID";

    public static CenterUnitData Validate(string? code, string? name)
    {
        var codeText = code?.Trim();
        if (string.IsNullOrEmpty(codeText) || codeText.Length is < MinCodeLength or > MaxCodeLength
            || !codeText.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_'))
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new CenterUnitData(codeText.ToUpperInvariant(), ValidateName(name));
    }

    /// <summary>Los códigos que se prueban para un código generado: el propio, «-2», «-3»… hasta «-99». La base lleva como
    /// mucho 60 caracteres (ExchangeText.UnitCode), así que con el sufijo cabe en MaxCodeLength.</summary>
    public static IEnumerable<string> CodeCandidates(string baseCode)
    {
        yield return baseCode;
        for (var n = 2; n <= 99; n++)
        {
            yield return $"{baseCode}-{n}";
        }
    }

    public static string ValidateName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength
            ? throw new DomainValidationException(InvalidCode)
            : text;
    }
}
