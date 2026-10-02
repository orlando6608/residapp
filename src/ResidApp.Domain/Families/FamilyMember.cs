using ResidApp.Shared;

namespace ResidApp.Domain.Families;

/// <summary>ADM-03 (script 0022): los datos de un familiar vinculado a un residente. La relación es texto libre (p. ej.
/// «Hija»), porque no hay un catálogo de parentescos decidido.</summary>
public sealed record FamilyMemberData(string DisplayName, string Relationship, string Phone, string? Email)
{
    /// <summary>Si los datos son los mismos, comparados tal cual (mayúsculas y acentos incluidos).</summary>
    public bool SameAs(FamilyMemberData other) =>
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
        && string.Equals(Relationship, other.Relationship, StringComparison.Ordinal)
        && string.Equals(Phone, other.Phone, StringComparison.Ordinal)
        && string.Equals(Email, other.Email, StringComparison.Ordinal);

    /// <summary>Huella de los datos tal como están: la edición la lleva en el formulario y el servidor la compara con la actual para
    /// detectar que otra persona cambió al familiar entretanto. Distinta para cualquier cambio, mayúsculas y acentos incluidos.</summary>
    public string Version => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join('\u001F', DisplayName, Relationship, Phone, Email ?? string.Empty))));
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

    public static FamilyMemberData Validate(string? displayName, string? relationship, string? phone, string? email)
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

        return new FamilyMemberData(name, relation, phoneText!, emailText);
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
