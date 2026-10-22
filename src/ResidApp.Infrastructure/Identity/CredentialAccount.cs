namespace ResidApp.Infrastructure.Identity;

/// <summary>
/// El usuario de ASP.NET Core Identity (ADR 0007, incremento I1) es la cuenta: Id es cuentas.id y UserName, sujeto_externo (el
/// «Identificador de acceso»). Lo demás sale de dbo.credenciales (0062); una cuenta sin fila allí no tiene contraseña. Los perfiles, el
/// centro y los permisos siguen resolviéndose en SQL, nunca aquí.
/// </summary>
public sealed class CredentialAccount
{
    public required Guid Id { get; init; }

    public required string UserName { get; init; }

    public bool Active { get; init; }

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public string? AuthenticatorKey { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public int AccessFailedCount { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>Resúmenes SHA-256 de los códigos de recuperación sin usar, separados por «;».</summary>
    public string? RecoveryCodeHashes { get; set; }
}
