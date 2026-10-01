using ResidApp.Shared;

namespace ResidApp.Domain.Accounts;

/// <summary>ADM-12: estado de una cuenta (0002). Una cuenta suspendida no opera aunque conserve sus perfiles.</summary>
public enum AccountStatus
{
    [Code("ACTIVE")] Active,
    [Code("SUSPENDED")] Suspended,
}

/// <summary>ADM-13 (script 0023): los datos de alta de una cuenta profesional. Subject es el sujeto externo, el
/// identificador con el que la persona entra (hoy el del inicio de sesión de desarrollo; mañana el del proveedor de
/// identidad).</summary>
public sealed record ProfessionalAccountData(string Subject, string DisplayName);

/// <summary>
/// ADM-12/ADM-13: reglas de las cuentas profesionales. El identificador lleva de 3 a 200 caracteres sin espacios (letras,
/// dígitos y «. _ - @»); el nombre, de 1 a 200 tras recortar. Familiar no se concede desde aquí: llegará con el Portal
/// Familiar y sus autorizaciones.
/// </summary>
public static class ProfessionalAccount
{
    public const int MinSubjectLength = 3;
    public const int MaxSubjectLength = 200;
    public const int MaxDisplayNameLength = 200;
    public const string InvalidCode = "ACCOUNT_INVALID";

    public static readonly IReadOnlyList<SystemProfile> GrantableProfiles =
    [
        SystemProfile.Auxiliar, SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Administracion,
        SystemProfile.DireccionClinica,
    ];

    public static ProfessionalAccountData Validate(string? subject, string? displayName)
    {
        var subjectText = subject?.Trim();
        if (string.IsNullOrEmpty(subjectText) || subjectText.Length is < MinSubjectLength or > MaxSubjectLength
            || !subjectText.All(c => char.IsLetterOrDigit(c) || "._-@".Contains(c)))
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new ProfessionalAccountData(subjectText, ValidateDisplayName(displayName));
    }

    public static string ValidateDisplayName(string? displayName)
    {
        var name = displayName?.Trim();
        return string.IsNullOrEmpty(name) || name.Length > MaxDisplayNameLength
            ? throw new DomainValidationException(InvalidCode)
            : name;
    }

    public static bool IsGrantable(SystemProfile profile) => GrantableProfiles.Contains(profile);
}
