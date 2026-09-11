using ResidApp.Shared;

namespace ResidApp.Domain.Baseline.Answers;

/// <summary>
/// Helpers compartidos que traducen assertMultiChoice/assertExclusive/assertOpenText/assertArrayOpenText
/// de lib/domain/baseline/validation.ts. Los 9 records de respuesta por área (Answers/) los invocan en el
/// cuerpo de su constructor: son reglas CRUZADAS entre dos o más propiedades (p. ej. "el texto libre solo
/// es válido si la opción OTRA está seleccionada"), así que no encajan en la validación de una sola
/// propiedad vía 'field' — esa se reserva para invariantes de una única propiedad (ver Resident.cs).
/// </summary>
internal static class BaselineValidation
{
    public static void AssertMultiChoice<TEnum>(IReadOnlyList<TEnum> values) where TEnum : struct, Enum
    {
        if (values.Count == 0 || values.Distinct().Count() != values.Count)
        {
            throw new DomainValidationException("BASELINE_MULTI_VALUE_INVALID");
        }
    }

    public static void AssertExclusive<TEnum>(IReadOnlyList<TEnum> values, params ReadOnlySpan<TEnum> exclusiveCodes) where TEnum : struct, Enum
    {
        if (values.Count <= 1)
        {
            return;
        }
        foreach (var code in exclusiveCodes)
        {
            if (values.Contains(code))
            {
                throw new DomainValidationException("BASELINE_MULTI_VALUE_EXCLUSIVE");
            }
        }
    }

    public static void AssertOpenText(bool optionSelected, string? text)
    {
        if (optionSelected)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new DomainValidationException("BASELINE_OPEN_TEXT_REQUIRED");
            }
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            throw new DomainValidationException("BASELINE_OPEN_TEXT_WITHOUT_OPTION");
        }
    }

    public static void AssertArrayOpenText<TEnum>(IReadOnlyList<TEnum> selected, TEnum openCode, string? text) where TEnum : struct, Enum =>
        AssertOpenText(selected.Contains(openCode), text);

    public static void AssertNonEmptyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("BASELINE_OPEN_TEXT_REQUIRED");
        }
    }
}
