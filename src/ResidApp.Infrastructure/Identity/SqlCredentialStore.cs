using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Identity;
using ResidApp.Infrastructure.Persistence;

namespace ResidApp.Infrastructure.Identity;

/// <summary>
/// Almacén de ASP.NET Core Identity sobre dbo.cuentas y dbo.credenciales (0062), con Dapper como el resto. Las cuentas las crea
/// Administración, así que aquí no se crean ni se borran: solo se guardan sus credenciales. Los intentos fallidos se suman y se ponen a
/// cero directamente en SQL, para que dos intentos a la vez no se pisen; lo demás se escribe en <see cref="UpdateAsync"/>. Los códigos de
/// recuperación se guardan como resúmenes y cada uno vale una vez.
/// </summary>
public sealed class SqlCredentialStore(SqlConnectionFactory connections, TimeProvider clock) :
    IUserPasswordStore<CredentialAccount>, IUserSecurityStampStore<CredentialAccount>, IUserLockoutStore<CredentialAccount>,
    IUserTwoFactorStore<CredentialAccount>, IUserAuthenticatorKeyStore<CredentialAccount>,
    IUserTwoFactorRecoveryCodeStore<CredentialAccount>, IUserPasskeyStore<CredentialAccount>
{
    private const string SelectAccount = """
        SELECT account.id AS Id, account.sujeto_externo AS UserName, CAST(CASE WHEN account.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
               credential.hash_contrasena AS PasswordHash, credential.sello_seguridad AS SecurityStamp,
               credential.clave_autenticador AS AuthenticatorKey, ISNULL(credential.segundo_factor_activo, 0) AS TwoFactorEnabled,
               ISNULL(credential.intentos_fallidos, 0) AS AccessFailedCount, credential.bloqueada_hasta AS LockoutEnd,
               credential.codigos_recuperacion AS RecoveryCodeHashes
          FROM dbo.cuentas account
          LEFT JOIN dbo.credenciales credential ON credential.cuenta_id = account.id
        """;

    public async Task<CredentialAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return null;
        }

        using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CredentialAccount>(new CommandDefinition(
            SelectAccount + " WHERE account.id = @id", new { id }, cancellationToken: cancellationToken));
    }

    /// <summary>Por sujeto_externo tal cual (ver <see cref="SubjectLookupNormalizer"/>), con la misma comparación que el resto de la app.</summary>
    public async Task<CredentialAccount?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CredentialAccount>(new CommandDefinition(
            SelectAccount + " WHERE account.sujeto_externo = @normalizedUserName", new { normalizedUserName }, cancellationToken: cancellationToken));
    }

    public async Task<IdentityResult> UpdateAsync(CredentialAccount user, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            MERGE dbo.credenciales WITH (HOLDLOCK) AS target
            USING (SELECT @Id AS cuenta_id) AS source ON target.cuenta_id = source.cuenta_id
            WHEN MATCHED THEN UPDATE SET
                hash_contrasena = @PasswordHash, sello_seguridad = @SecurityStamp, clave_autenticador = @AuthenticatorKey,
                segundo_factor_activo = @TwoFactorEnabled, bloqueada_hasta = @LockoutEnd, codigos_recuperacion = @RecoveryCodeHashes,
                actualizada_en = @Now
            WHEN NOT MATCHED THEN INSERT
                (cuenta_id, hash_contrasena, sello_seguridad, clave_autenticador, segundo_factor_activo, bloqueada_hasta, codigos_recuperacion,
                 actualizada_en)
                VALUES (@Id, @PasswordHash, @SecurityStamp, @AuthenticatorKey, @TwoFactorEnabled, @LockoutEnd, @RecoveryCodeHashes, @Now);
            """, new
        {
            user.Id, user.PasswordHash, user.SecurityStamp, user.AuthenticatorKey, user.TwoFactorEnabled, user.LockoutEnd,
            user.RecoveryCodeHashes, Now = clock.GetUtcNow().UtcDateTime,
        }, cancellationToken: cancellationToken));
        return IdentityResult.Success;
    }

    public Task<IdentityResult> CreateAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Las cuentas las crea Administración (ADM-13), no Identity.");

    public Task<IdentityResult> DeleteAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Las cuentas no se borran: se suspenden.");

    public Task<string> GetUserIdAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString("D"));

    public Task<string?> GetUserNameAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(CredentialAccount user, string? userName, CancellationToken cancellationToken) =>
        throw new NotSupportedException("sujeto_externo no cambia nunca (0023).");

    public Task<string?> GetNormalizedUserNameAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.UserName);

    public Task SetNormalizedUserNameAsync(CredentialAccount user, string? normalizedName, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task SetPasswordHashAsync(CredentialAccount user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);

    public Task SetSecurityStampAsync(CredentialAccount user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult(user.SecurityStamp);

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(CredentialAccount user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    /// <summary>Suma en SQL y devuelve lo que hay allí, aunque otro intento haya sumado a la vez. Sin fila de credenciales (sin contraseña)
    /// no hay nada que bloquear.</summary>
    public async Task<int> IncrementAccessFailedCountAsync(CredentialAccount user, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        user.AccessFailedCount = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            UPDATE dbo.credenciales SET intentos_fallidos = intentos_fallidos + 1
            OUTPUT inserted.intentos_fallidos
             WHERE cuenta_id = @Id
            """, new { user.Id }, cancellationToken: cancellationToken)) ?? 0;
        return user.AccessFailedCount;
    }

    public async Task ResetAccessFailedCountAsync(CredentialAccount user, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.credenciales SET intentos_fallidos = 0 WHERE cuenta_id = @Id AND intentos_fallidos <> 0",
            new { user.Id }, cancellationToken: cancellationToken));
        user.AccessFailedCount = 0;
    }

    public Task<int> GetAccessFailedCountAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AccessFailedCount);

    /// <summary>El bloqueo vale para todas las cuentas.</summary>
    public Task<bool> GetLockoutEnabledAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetLockoutEnabledAsync(CredentialAccount user, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetTwoFactorEnabledAsync(CredentialAccount user, bool enabled, CancellationToken cancellationToken)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.TwoFactorEnabled);

    public Task SetAuthenticatorKeyAsync(CredentialAccount user, string key, CancellationToken cancellationToken)
    {
        user.AuthenticatorKey = key;
        return Task.CompletedTask;
    }

    public Task<string?> GetAuthenticatorKeyAsync(CredentialAccount user, CancellationToken cancellationToken) =>
        Task.FromResult(user.AuthenticatorKey);

    public Task ReplaceCodesAsync(CredentialAccount user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        user.RecoveryCodeHashes = string.Join(';', recoveryCodes.Select(Hash));
        return Task.CompletedTask;
    }

    /// <summary>Quita el código de la lista si estaba; <see cref="UserManager{TUser}"/> guarda después con <see cref="UpdateAsync"/>.</summary>
    public Task<bool> RedeemCodeAsync(CredentialAccount user, string code, CancellationToken cancellationToken)
    {
        var hashes = Hashes(user).ToList();
        if (!hashes.Remove(Hash(code)))
        {
            return Task.FromResult(false);
        }

        user.RecoveryCodeHashes = string.Join(';', hashes);
        return Task.FromResult(true);
    }

    public Task<int> CountCodesAsync(CredentialAccount user, CancellationToken cancellationToken) => Task.FromResult(Hashes(user).Count());

    /// <summary>Si la credencial ya existe en esa cuenta se actualiza (contador de firmas, copias, nombre); si es de otra cuenta, la clave
    /// primaria lo rechaza: una passkey nunca cambia de dueño.</summary>
    public async Task AddOrUpdatePasskeyAsync(CredentialAccount user, UserPasskeyInfo passkey, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            MERGE dbo.passkeys WITH (HOLDLOCK) AS target
            USING (SELECT @CredentialId AS credencial_id, @AccountId AS cuenta_id) AS source
               ON target.credencial_id = source.credencial_id AND target.cuenta_id = source.cuenta_id
            WHEN MATCHED THEN UPDATE SET
                nombre = @Name, contador_firmas = @SignCount, verificada = @IsUserVerified, copiada = @IsBackedUp
            WHEN NOT MATCHED THEN INSERT
                (credencial_id, cuenta_id, clave_publica, nombre, creada_en, contador_firmas, transportes, verificada, copia_posible, copiada)
                VALUES (@CredentialId, @AccountId, @PublicKey, @Name, @CreatedAt, @SignCount, @Transports, @IsUserVerified, @IsBackupEligible,
                        @IsBackedUp);
            """, new
        {
            AccountId = user.Id, passkey.CredentialId, passkey.PublicKey, passkey.Name, CreatedAt = passkey.CreatedAt.UtcDateTime,
            SignCount = (long)passkey.SignCount, Transports = passkey.Transports is { Length: > 0 } transports ? string.Join(',', transports) : null,
            passkey.IsUserVerified, passkey.IsBackupEligible, passkey.IsBackedUp,
        }, cancellationToken: cancellationToken));
    }

    public async Task<IList<UserPasskeyInfo>> GetPasskeysAsync(CredentialAccount user, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PasskeyRow>(new CommandDefinition(
            PasskeyColumns + " WHERE cuenta_id = @Id ORDER BY creada_en", new { user.Id }, cancellationToken: cancellationToken));
        return rows.Select(row => row.ToInfo()).ToList();
    }

    public async Task<UserPasskeyInfo?> FindPasskeyAsync(CredentialAccount user, byte[] credentialId, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<PasskeyRow>(new CommandDefinition(
            PasskeyColumns + " WHERE cuenta_id = @Id AND credencial_id = @credentialId", new { user.Id, credentialId },
            cancellationToken: cancellationToken));
        return row?.ToInfo();
    }

    public async Task<CredentialAccount?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CredentialAccount>(new CommandDefinition(
            SelectAccount + " WHERE account.id = (SELECT cuenta_id FROM dbo.passkeys WHERE credencial_id = @credentialId)",
            new { credentialId }, cancellationToken: cancellationToken));
    }

    public async Task RemovePasskeyAsync(CredentialAccount user, byte[] credentialId, CancellationToken cancellationToken)
    {
        using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.passkeys WHERE cuenta_id = @Id AND credencial_id = @credentialId", new { user.Id, credentialId },
            cancellationToken: cancellationToken));
    }

    public void Dispose()
    {
    }

    private const string PasskeyColumns = """
        SELECT credencial_id AS CredentialId, clave_publica AS PublicKey, nombre AS Name, creada_en AS CreatedAt,
               contador_firmas AS SignCount, transportes AS Transports, verificada AS IsUserVerified,
               copia_posible AS IsBackupEligible, copiada AS IsBackedUp
          FROM dbo.passkeys
        """;

    private sealed class PasskeyRow
    {
        public required byte[] CredentialId { get; init; }
        public required byte[] PublicKey { get; init; }
        public string? Name { get; init; }
        public DateTime CreatedAt { get; init; }
        public long SignCount { get; init; }
        public string? Transports { get; init; }
        public bool IsUserVerified { get; init; }
        public bool IsBackupEligible { get; init; }
        public bool IsBackedUp { get; init; }

        /// <summary>No se guardan la atestación ni los datos del cliente del alta: solo sirven en el momento de registrarla.</summary>
        public UserPasskeyInfo ToInfo() => new(
            CredentialId, PublicKey, new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)), (uint)SignCount,
            Transports?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [], IsUserVerified, IsBackupEligible, IsBackedUp,
            [], [])
        {
            Name = Name,
        };
    }

    private static IEnumerable<string> Hashes(CredentialAccount user) =>
        (user.RecoveryCodeHashes ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Sin distinguir mayúsculas ni espacios alrededor: así se escriben a mano.</summary>
    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));
}

/// <summary>Identity compara nombres de usuario «normalizados» (en mayúsculas, por defecto). Aquí el nombre es sujeto_externo y se busca
/// tal cual se escribe, sin los espacios de alrededor.</summary>
public sealed class SubjectLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name?.Trim();

    public string? NormalizeEmail(string? email) => email?.Trim();
}
