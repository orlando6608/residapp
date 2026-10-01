using ResidApp.Shared;

namespace ResidApp.Domain.Structure;

/// <summary>ADM-05 (script 0025): los datos de una unidad nueva. Code es el código estable de la unidad en el centro (no
/// cambia); Name, el nombre visible (se puede renombrar).</summary>
public sealed record CenterUnitData(string Code, string Name);

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

    public static string ValidateName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength
            ? throw new DomainValidationException(InvalidCode)
            : text;
    }
}
