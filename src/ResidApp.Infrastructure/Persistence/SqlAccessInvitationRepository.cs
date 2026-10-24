using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// dbo.invitaciones_acceso (0063). Emitir y anular van en una transacción que vuelve a comprobar el ámbito de quien emite (como
/// SqlProfessionalAccountRepository o SqlPlatformCenterRepository), bloquea la cuenta (UPDLOCK: dos emisiones a la vez se ordenan) y deja
/// su evento en dbo.eventos_auditoria. Las mismas reglas que suspender una cuenta: no la propia, y solo cuentas cuyos perfiles activos
/// son todos de este centro. Las búsquedas por clave dentro de la transacción llevan FORCESEEK (lección de 0055/0056).
/// </summary>
public sealed class SqlAccessInvitationRepository(SqlConnectionFactory connections) : IAccessInvitationRepository
{
    private const string SelectAccount = """
        SELECT account.id AS Id, account.sujeto_externo AS Subject, account.nombre_visible AS DisplayName,
               CAST(CASE WHEN account.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ambitos_perfil other WHERE other.cuenta_id = account.id
                                        AND other.centro_id <> @CenterId AND other.estado = 'ACTIVE') THEN 1 ELSE 0 END AS BIT) AS OtherCenters,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.credenciales credential WHERE credential.cuenta_id = account.id
                                        AND credential.hash_contrasena IS NOT NULL) THEN 1 ELSE 0 END AS BIT) AS HasPassword,
               center.nombre_visible AS CenterName
          FROM dbo.cuentas account WITH (UPDLOCK, ROWLOCK)
          JOIN dbo.centros center ON center.id = @CenterId
        """;

    public async Task<IssuedAccessInvitation> IssueAsync(
        AccountAdministrationAccess access, string subject, string tokenHash, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var account = await LockManagedAccountAsync(connection, transaction, access, subject, ct);
        if (!account.Active)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        var issued = await InsertAsync(connection, transaction, account, access.CenterId, access.AccountId, "ADMINISTRACION", tokenHash, now, ct);
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT", account.Id,
            issued.Kind == AccessInvitationKind.Alta ? "ACCOUNT_INVITATION_ISSUE" : "ACCOUNT_RECOVERY_ISSUE", Utc(now), ct: ct);
        transaction.Commit();
        return issued;
    }

    public async Task<IssuedAccessInvitation> IssueForAdministratorAsync(
        PlatformAccess access, CenterId centerId, Guid administrationScopeId, string tokenHash, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, transaction, access, ct);
        await SqlAdministrationScope.EnsureAdministrationScopeAsync(connection, transaction, centerId, administrationScopeId, ct);
        var account = await connection.QuerySingleAsync<AccountRow>(new CommandDefinition(SelectAccount + """
             WHERE account.id = (SELECT scope.cuenta_id FROM dbo.ambitos_perfil scope WHERE scope.id = @ScopeId)
            """, new { CenterId = centerId.Value, ScopeId = administrationScopeId }, transaction, cancellationToken: ct));
        if (!account.Active || account.OtherCenters)
        {
            throw new DomainValidationException(account.Active ? "ACCOUNT_OTHER_CENTERS" : "ACCOUNT_CHANGE_CONFLICT");
        }

        var issued = await InsertAsync(connection, transaction, account, centerId, access.AccountId, "PLATAFORMA", tokenHash, now, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @ActorId, 'PLATAFORMA', @CenterId, 'ACCOUNT', @AccountId, @Action, @OccurredAt)
            """, new
        {
            ActorId = access.AccountId.Value, CenterId = centerId.Value, AccountId = account.Id, OccurredAt = Utc(now),
            Action = issued.Kind == AccessInvitationKind.Alta ? "ACCOUNT_INVITATION_ISSUE" : "ACCOUNT_RECOVERY_ISSUE",
        }, transaction, cancellationToken: ct));
        transaction.Commit();
        return issued;
    }

    public async Task<IssuedAccessInvitation> IssueForMultiCenterAsync(
        PlatformAccess access, AccountId accountId, string tokenHash, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlPlatformCenterRepository.EnsureOperatorAsync(connection, transaction, access, ct);
        if (accountId == access.AccountId)
        {
            throw new DomainValidationException("ACCOUNT_SELF_CHANGE");
        }

        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(SelectAccount + """
             WHERE account.id = @AccountId
               AND (SELECT COUNT(DISTINCT profile.centro_id) FROM dbo.ambitos_perfil profile
                     WHERE profile.cuenta_id = account.id AND profile.estado = 'ACTIVE') >= 2
            """, new { CenterId = PlatformCenter.Id, AccountId = accountId.Value }, transaction, cancellationToken: ct))
            ?? throw new DomainValidationException("ACCOUNT_INVALID");
        if (!account.Active)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        var issued = await InsertAsync(connection, transaction, account, CenterId.From(PlatformCenter.Id), access.AccountId, "PLATAFORMA", tokenHash, now, ct);
        // Cada centro en el que trabaja la cuenta lo ve en su auditoría.
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            SELECT NEWID(), @ActorId, 'PLATAFORMA', scope.centro_id, 'ACCOUNT', @AccountId, @Action, @OccurredAt
              FROM (SELECT DISTINCT centro_id FROM dbo.ambitos_perfil WHERE cuenta_id = @AccountId AND estado = 'ACTIVE') scope
            """, new
        {
            ActorId = access.AccountId.Value, AccountId = account.Id, OccurredAt = Utc(now),
            Action = issued.Kind == AccessInvitationKind.Alta ? "ACCOUNT_INVITATION_ISSUE" : "ACCOUNT_RECOVERY_ISSUE",
        }, transaction, cancellationToken: ct));
        transaction.Commit();
        return issued;
    }

    public async Task CancelAsync(AccountAdministrationAccess access, string subject, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var account = await LockManagedAccountAsync(connection, transaction, access, subject, ct);
        var cancelled = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE invitation SET anulada_en = @Now
              FROM dbo.invitaciones_acceso invitation WITH (FORCESEEK)
             WHERE invitation.cuenta_id = @AccountId AND invitation.usada_en IS NULL AND invitation.anulada_en IS NULL
            """, new { AccountId = account.Id, Now = now }, transaction, cancellationToken: ct));
        if (cancelled == 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT", account.Id, "ACCOUNT_INVITATION_CANCEL", Utc(now), ct: ct);
        transaction.Commit();
    }

    public async Task<AccessInvitationStatus> FindStatusAsync(
        AccountAdministrationAccess access, string subject, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        return await connection.QuerySingleOrDefaultAsync<AccessInvitationStatus>(new CommandDefinition("""
            SELECT CAST(CASE WHEN credential.hash_contrasena IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasPassword,
                   (SELECT invitation.caduca_en FROM dbo.invitaciones_acceso invitation
                     WHERE invitation.cuenta_id = account.id AND invitation.centro_id = @CenterId
                       AND invitation.usada_en IS NULL AND invitation.anulada_en IS NULL AND invitation.caduca_en > @Now) AS LiveExpiresAt
              FROM dbo.cuentas account
              LEFT JOIN dbo.credenciales credential ON credential.cuenta_id = account.id
             WHERE account.sujeto_externo = @Subject
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId)
            """, new { Subject = subject, CenterId = access.CenterId.Value, Now = now }, cancellationToken: ct))
            ?? throw new AccessDeniedException();
    }

    public async Task<LiveAccessInvitation?> FindLiveAsync(string tokenHash, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<LiveRow>(new CommandDefinition("""
            SELECT invitation.id AS Id, invitation.cuenta_id AS AccountId, invitation.tipo AS KindCode, invitation.caduca_en AS ExpiresAt
              FROM dbo.invitaciones_acceso invitation
              JOIN dbo.cuentas account ON account.id = invitation.cuenta_id AND account.estado = 'ACTIVE'
             WHERE invitation.resumen_token = @TokenHash
               AND invitation.usada_en IS NULL AND invitation.anulada_en IS NULL AND invitation.caduca_en > @Now
            """, new { TokenHash = tokenHash, Now = now }, cancellationToken: ct));
        return row is null
            ? null
            : new LiveAccessInvitation(row.Id, AccountId.From(row.AccountId), EnumCode.ParseCode<AccessInvitationKind>(row.KindCode), row.ExpiresAt);
    }

    public async Task<bool> ConsumeAsync(Guid invitationId, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE invitation SET usada_en = @Now
              FROM dbo.invitaciones_acceso invitation WITH (FORCESEEK)
              JOIN dbo.cuentas account ON account.id = invitation.cuenta_id AND account.estado = 'ACTIVE'
             WHERE invitation.id = @Id AND invitation.usada_en IS NULL AND invitation.anulada_en IS NULL AND invitation.caduca_en > @Now
            """, new { Id = invitationId, Now = now }, cancellationToken: ct)) == 1;
    }

    /// <summary>La cuenta gestionada, bloqueada: con algún perfil en el centro (si no, ajena, sin decir si existe), no la propia y sin
    /// perfiles activos en otros centros.</summary>
    private static async Task<AccountRow> LockManagedAccountAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, string subject, CancellationToken ct)
    {
        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(SelectAccount + """
             WHERE account.sujeto_externo = @Subject
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId)
            """, new { Subject = subject, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        if (account.Id == access.AccountId.Value)
        {
            throw new DomainValidationException("ACCOUNT_SELF_CHANGE");
        }
        return account.OtherCenters ? throw new DomainValidationException("ACCOUNT_OTHER_CENTERS") : account;
    }

    /// <summary>Anula la invitación viva de la cuenta, si la hay, e inserta la nueva: de recuperación si ya tiene contraseña.</summary>
    private static async Task<IssuedAccessInvitation> InsertAsync(
        SqlConnection connection, SqlTransaction transaction, AccountRow account, CenterId centerId, AccountId issuerId, string issuerProfile,
        string tokenHash, DateTime now, CancellationToken ct)
    {
        var kind = account.HasPassword ? AccessInvitationKind.Recuperacion : AccessInvitationKind.Alta;
        var expiresAt = now + AccessInvitation.Validity;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE invitation SET anulada_en = @Now
              FROM dbo.invitaciones_acceso invitation WITH (FORCESEEK)
             WHERE invitation.cuenta_id = @AccountId AND invitation.usada_en IS NULL AND invitation.anulada_en IS NULL;
            INSERT INTO dbo.invitaciones_acceso
                (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor, caduca_en)
            VALUES (NEWID(), @AccountId, @CenterId, @Kind, @TokenHash, @Now, @IssuerId, @IssuerProfile, @ExpiresAt);
            """, new
        {
            AccountId = account.Id, CenterId = centerId.Value, Kind = kind.ToCode(), TokenHash = tokenHash, Now = now,
            IssuerId = issuerId.Value, IssuerProfile = issuerProfile, ExpiresAt = expiresAt,
        }, transaction, cancellationToken: ct));
        return new IssuedAccessInvitation(kind, account.Subject, account.DisplayName, account.CenterName, expiresAt);
    }

    private static DateTimeOffset Utc(DateTime now) => new(DateTime.SpecifyKind(now, DateTimeKind.Utc));

    private sealed record AccountRow(Guid Id, string Subject, string? DisplayName, bool Active, bool OtherCenters, bool HasPassword, string CenterName);

    private sealed record LiveRow(Guid Id, Guid AccountId, string KindCode, DateTime ExpiresAt);
}
