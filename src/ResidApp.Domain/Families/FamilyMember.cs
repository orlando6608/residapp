using ResidApp.Shared;

namespace ResidApp.Domain.Families;

/// <summary>ADM-03 (script 0022): los datos de un familiar vinculado a un residente. La relación es texto libre (p. ej.
/// «Hija»), porque no hay un catálogo de parentescos decidido.</summary>
public sealed record FamilyMemberData(
    string DisplayName, string Relationship, string Phone, string? Email, bool IsReferent = false, bool IsLegalGuardian = false)
{
    /// <summary>Si los datos son los mismos, comparados tal cual (mayúsculas y acentos incluidos).</summary>
    public bool SameAs(FamilyMemberData other) =>
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && string.Equals(Relationship, other.Relationship, StringComparison.Ordinal)
        && string.Equals(Phone, other.Phone, StringComparison.Ordinal)
        && string.Equals(Email, other.Email, StringComparison.Ordinal)
        && IsReferent == other.IsReferent && IsLegalGuardian == other.IsLegalGuardian;

    /// <summary>Huella de los datos tal como están: la edición la lleva en el formulario y el servidor la compara con la actual para
    /// detectar que otra persona cambió al familiar entretanto. Distinta para cualquier cambio, mayúsculas y acentos incluidos.</summary>
    public string Version => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join('\u001F', DisplayName, Relationship, Phone, Email ?? string.Empty, IsReferent, IsLegalGuardian))));
}

/// <summary>Un familiar que se registra al dar de alta al residente (CJ, 2026-10-07): sus datos, si es referente o tutor legal y si es el
/// contacto prioritario (contacto urgente; puede haber varios, script 0044).</summary>
public sealed record NewResidentFamilyMember(FamilyMemberData Data, bool IsPriorityContact);

/// <summary>Un familiar tal como llega del formulario de alta, sin validar. Una fila sin ningún dato ni marca se ignora.</summary>
public sealed record NewResidentFamilyInput(
    string? DisplayName, string? Relationship, string? Phone, string? Email, bool IsReferent, bool IsLegalGuardian, bool IsPriorityContact)
{
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(DisplayName) && string.IsNullOrWhiteSpace(Relationship) && string.IsNullOrWhiteSpace(Phone)
        && string.IsNullOrWhiteSpace(Email) && !IsReferent && !IsLegalGuardian && !IsPriorityContact;
}

public static class ResidentFamilyAtAdmission
{
    public const int MaxMembers = 5;

    /// <summary>Valida las filas con las reglas de siempre de un familiar (nombre, relación y teléfono obligatorios). Las filas en blanco se
    /// ignoran; una fila a medias, más de cinco o ningún contacto prioritario son FAMILY_MEMBER_INVALID. Sin filas (null: un alta que no pasa
    /// por el formulario) no se exige nada.</summary>
    public static IReadOnlyList<NewResidentFamilyMember> Validate(IReadOnlyList<NewResidentFamilyInput>? rows)
    {
        var filled = (rows ?? []).Where(r => !r.IsBlank).ToList();
        // Desde el formulario de alta (rows no es null) tiene que haber al menos un contacto prioritario (CJ, 2026-10-07).
        if (filled.Count > MaxMembers || (rows is not null && !filled.Any(r => r.IsPriorityContact)))
        {
            throw new DomainValidationException(FamilyMember.InvalidCode);
        }

        return filled.Select(r => new NewResidentFamilyMember(
            FamilyMember.Validate(r.DisplayName, r.Relationship, r.Phone, r.Email, r.IsReferent, r.IsLegalGuardian), r.IsPriorityContact)).ToList();
    }
}

/// <summary>
/// ADM-03: validación de los datos de un familiar. Nombre, relación y teléfono son obligatorios y el correo, opcional.
/// El teléfono admite dígitos, espacios, «+», guiones, puntos y paréntesis, con entre 6 y 15 dígitos; el correo, una
/// sola «@» que no esté al principio ni al final y ningún espacio. No se comprueba que el teléfono o el correo existan.
/// </summary>
public static class FamilyMember
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxRelationshipLength = 100;
    public const int MaxPhoneLength = 32;
    public const int MaxEmailLength = 254;
    public const string InvalidCode = "FAMILY_MEMBER_INVALID";

    public static FamilyMemberData Validate(
        string? displayName, string? relationship, string? phone, string? email, bool isReferent = false, bool isLegalGuardian = false)
    {
        var name = displayName?.Trim();
        var relation = relationship?.Trim();
        var phoneText = phone?.Trim();
        var emailText = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxDisplayNameLength
            || string.IsNullOrEmpty(relation) || relation.Length > MaxRelationshipLength
            || !IsPhone(phoneText) || (emailText is not null && !IsEmail(emailText)))
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new FamilyMemberData(name, relation, phoneText!, emailText, isReferent, isLegalGuardian);
    }

    public const int MaxUnlinkReasonLength = 500;

    /// <summary>El motivo de desvincular a un familiar de un residente (obligatorio), ya sin espacios sobrantes.</summary>
    public static string ValidateUnlinkReason(string? reason)
    {
        var text = reason?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxUnlinkReasonLength ? throw new DomainValidationException(InvalidCode) : text;
    }

    /// <summary>La relación de un familiar con un residente (texto libre, obligatorio), ya sin espacios sobrantes.</summary>
    public static string ValidateRelationship(string? relationship)
    {
        var relation = relationship?.Trim();
        return string.IsNullOrEmpty(relation) || relation.Length > MaxRelationshipLength
            ? throw new DomainValidationException(InvalidCode)
            : relation;
    }

    private static bool IsPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length > MaxPhoneLength || !phone.All(c => char.IsAsciiDigit(c) || " +-.()".Contains(c)))
        {
            return false;
        }

        var digits = phone.Count(char.IsAsciiDigit);
        return digits is >= 6 and <= 15;
    }

    private static bool IsEmail(string email)
    {
        var at = email.IndexOf('@');
        return email.Length <= MaxEmailLength && !email.Any(char.IsWhiteSpace)
            && at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1;
    }
}
