using System.Security.Cryptography;
using System.Text;
using ResidApp.Shared;

namespace ResidApp.Domain.Accounts;

/// <summary>Para qué es una invitación de acceso (dbo.invitaciones_acceso.tipo, script 0063): el primer acceso de una cuenta sin
/// contraseña, o la recuperación de una que ya la tenía.</summary>
public enum AccessInvitationKind
{
    [Code("ALTA")] Alta,
    [Code("RECUPERACION")] Recuperacion,
}

/// <summary>
/// Invitación de acceso de un solo uso (ADR 0007, incremento I2; decisión de Orlando del 2026-10-22: 72 horas). El token va en el enlace y
/// en el QR que se entrega a la persona; solo se enseña al emitirla y en la base solo queda su resumen.
/// </summary>
public static class AccessInvitation
{
    public static readonly TimeSpan Validity = TimeSpan.FromHours(72);

    /// <summary>32 bytes aleatorios en base64 apto para una dirección.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Resumen SHA-256 en hexadecimal (64 caracteres), lo único que se guarda.</summary>
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));
}
