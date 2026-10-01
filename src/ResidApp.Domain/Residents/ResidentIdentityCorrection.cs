using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>RES-01: la identidad administrativa del residente que Administración puede corregir.</summary>
public sealed record ResidentIdentity(string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSex)
{
    /// <summary>Si dos identidades son la misma. El nombre se compara tal cual (mayúsculas y acentos incluidos), porque
    /// corregir «maria» a «María» es un cambio.</summary>
    public bool SameAs(ResidentIdentity other) =>
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && BirthDate == other.BirthDate && DocumentedSex == other.DocumentedSex;
}

/// <summary>
/// ADM-03 (script 0021): validación de una corrección de identidad. El nombre y la fecha de nacimiento siguen las
/// reglas del alta (Resident): nombre no vacío y fecha no futura; el sexo, el catálogo cerrado. El motivo es
/// obligatorio. Que la corrección cambie algo se comprueba contra la identidad vigente, en la misma transacción que la
/// guarda.
/// </summary>
public static class ResidentIdentityCorrection
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxReasonLength = 1000;
    public const string InvalidCode = "RESIDENT_IDENTITY_CORRECTION_INVALID";

    public static (ResidentIdentity Identity, string Reason) Validate(
        string? displayName, DateOnly birthDate, DocumentedSexCode documentedSex, string? reason, DateOnly today)
    {
        var name = displayName?.Trim();
        var reasonText = reason?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxDisplayNameLength || birthDate > today
            || !Enum.IsDefined(documentedSex) || string.IsNullOrEmpty(reasonText) || reasonText.Length > MaxReasonLength)
        {
            throw new DomainValidationException(InvalidCode);
        }

        return (new ResidentIdentity(name, birthDate, documentedSex), reasonText);
    }
}
