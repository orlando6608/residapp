using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// dbo.vinculaciones_cuenta (0065). Emitir, anular y resolver van en una transacción que vuelve a comprobar el ámbito de quien actúa
/// (como SqlAccessInvitationRepository) y deja su evento en dbo.eventos_auditoria; canjear va en otra que bloquea la vinculación, así
/// que dos canjes a la vez se ordenan y solo gana uno. Confirmar reutiliza lo de conceder un perfil (el mismo INSERT y las mismas
/// comprobaciones de las unidades). Las búsquedas por clave dentro de la transacción llevan FORCESEEK (lección de 0055/0056).
/// </summary>
public sealed class SqlAccountLinkRepository(SqlConnectionFactory connections) : IAccountLinkRepository
{
    public async Task<IssuedAccountLink> IssueAsync(
        AccountAdministrationAccess access, SystemProfile profile, IReadOnlyList<UnitId> units, string tokenHash, DateTime now,
        CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        if (!ProfessionalAccount.IsGrantable(profile))
        {
            throw new DomainValidationException("PROFILE_SCOPE_INVALID");
        }

        await SqlProfessionalAccountRepository.EnsureAdministratorUnitsAsync(connection, transaction, access, units, ct);
        var id = Guid.NewGuid();
        var expiresAt = now + AccessInvitation.Validity;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.vinculaciones_cuenta (id, centro_id, perfil_codigo, resumen_token, emitida_en, emitida_por_cuenta_id, caduca_en)
            VALUES (@Id, @CenterId, @ProfileCode, @TokenHash, @Now, @IssuerId, @ExpiresAt)
            """, new
        {
            Id = id, CenterId = access.CenterId.Value, ProfileCode = profile.ToCode(), TokenHash = tokenHash, Now = now,
            IssuerId = access.AccountId.Value, ExpiresAt = expiresAt,
        }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.vinculaciones_cuenta_unidades (vinculacion_id, centro_id, unidad_id) VALUES (@Id, @CenterId, @UnitId)
            """, units.Select(unit => new { Id = id, CenterId = access.CenterId.Value, UnitId = unit.Value }).Distinct().ToList(),
            transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT_LINK", id, "ACCOUNT_LINK_ISSUE", Utc(now), ct: ct);
        var centerName = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT nombre_visible FROM dbo.centros WHERE id = @CenterId", new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        var unitNames = await UnitNamesAsync(connection, transaction, [id], ct);
        transaction.Commit();
        return new IssuedAccountLink(centerName ?? string.Empty, profile, unitNames.GetValueOrDefault(id) ?? [], expiresAt);
    }

    public async Task<IReadOnlyList<AccountLinkSummary>> ListOpenAsync(AccountAdministrationAccess access, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = (await connection.QueryAsync<OpenRow>(new CommandDefinition("""
            SELECT link.id AS Id, link.perfil_codigo AS ProfileCode, link.emitida_en AS IssuedAt, link.caduca_en AS ExpiresAt,
                   redeemer.sujeto_externo AS RedeemedBySubject, redeemer.nombre_visible AS RedeemedByName, link.canjeada_en AS RedeemedAt
              FROM dbo.vinculaciones_cuenta link
              LEFT JOIN dbo.cuentas redeemer ON redeemer.id = link.canjeada_por_cuenta_id
             WHERE link.centro_id = @CenterId AND link.anulada_en IS NULL AND link.resuelta_en IS NULL
               AND (link.canjeada_en IS NOT NULL OR link.caduca_en > @Now)
             ORDER BY link.emitida_en DESC
            """, new { CenterId = access.CenterId.Value, Now = now }, cancellationToken: ct))).ToList();
        var units = await UnitNamesAsync(connection, null, rows.Select(row => row.Id).ToList(), ct);
        return rows.Select(row => new AccountLinkSummary(
            row.Id, EnumCode.ParseCode<SystemProfile>(row.ProfileCode), units.GetValueOrDefault(row.Id) ?? [], row.IssuedAt, row.ExpiresAt,
            row.RedeemedBySubject, row.RedeemedByName, row.RedeemedAt)).ToList();
    }

    public async Task CancelAsync(AccountAdministrationAccess access, Guid linkId, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var cancelled = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE link SET anulada_en = @Now
              FROM dbo.vinculaciones_cuenta link WITH (FORCESEEK)
             WHERE link.id = @Id AND link.centro_id = @CenterId
               AND link.canjeada_en IS NULL AND link.anulada_en IS NULL AND link.resuelta_en IS NULL
            """, new { Id = linkId, CenterId = access.CenterId.Value, Now = now }, transaction, cancellationToken: ct));
        if (cancelled == 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT_LINK", linkId, "ACCOUNT_LINK_CANCEL", Utc(now), ct: ct);
        transaction.Commit();
    }

    public async Task<LiveAccountLink?> FindLiveAsync(string tokenHash, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<LiveRow>(new CommandDefinition("""
            SELECT link.id AS Id, center.nombre_visible AS CenterName, link.perfil_codigo AS ProfileCode, link.caduca_en AS ExpiresAt
              FROM dbo.vinculaciones_cuenta link
              JOIN dbo.centros center ON center.id = link.centro_id AND center.estado = 'ACTIVE'
             WHERE link.resumen_token = @TokenHash
               AND link.anulada_en IS NULL AND link.canjeada_en IS NULL AND link.caduca_en > @Now
            """, new { TokenHash = tokenHash, Now = now }, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        var units = await UnitNamesAsync(connection, null, [row.Id], ct);
        return new LiveAccountLink(row.Id, row.CenterName, EnumCode.ParseCode<SystemProfile>(row.ProfileCode), units.GetValueOrDefault(row.Id) ?? [], row.ExpiresAt);
    }

    public async Task RedeemAsync(AccountId accountId, Guid linkId, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await connection.QuerySingleOrDefaultAsync<LockedLinkRow>(new CommandDefinition("""
            SELECT link.centro_id AS CenterId, link.perfil_codigo AS ProfileCode, link.emitida_por_cuenta_id AS IssuerId
              FROM dbo.vinculaciones_cuenta link WITH (UPDLOCK, ROWLOCK, FORCESEEK)
             WHERE link.id = @Id AND link.anulada_en IS NULL AND link.canjeada_en IS NULL AND link.caduca_en > @Now
            """, new { Id = linkId, Now = now }, transaction, cancellationToken: ct))
            ?? throw new DomainValidationException("ACCOUNT_LINK_INVALID");
        await EnsureCanBeLinkedAsync(connection, transaction, accountId.Value, link, ct);
        if (link.IssuerId == accountId.Value)
        {
            throw new DomainValidationException("ACCOUNT_LINK_INVALID");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE link SET canjeada_en = @Now, canjeada_por_cuenta_id = @AccountId
              FROM dbo.vinculaciones_cuenta link WITH (FORCESEEK)
             WHERE link.id = @Id AND link.canjeada_en IS NULL
            """, new { Id = linkId, AccountId = accountId.Value, Now = now }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    public async Task<Guid?> ResolveAsync(AccountAdministrationAccess access, Guid linkId, bool confirm, DateTime now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var link = await connection.QuerySingleOrDefaultAsync<PendingRow>(new CommandDefinition("""
            SELECT link.perfil_codigo AS ProfileCode, link.canjeada_por_cuenta_id AS RedeemerId,
                   CAST(CASE WHEN link.canjeada_en IS NOT NULL AND link.resuelta_en IS NULL AND link.anulada_en IS NULL THEN 1 ELSE 0 END AS BIT) AS Pending
              FROM dbo.vinculaciones_cuenta link WITH (UPDLOCK, ROWLOCK, FORCESEEK)
             WHERE link.id = @Id AND link.centro_id = @CenterId
            """, new { Id = linkId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        if (!link.Pending || link.RedeemerId is not { } redeemerId)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        var occurredAt = Utc(now);
        Guid? scopeId = null;
        if (confirm)
        {
            await EnsureCanBeLinkedAsync(connection, transaction, redeemerId, new LockedLinkRow(access.CenterId.Value, link.ProfileCode, Guid.Empty), ct);
            var units = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT unidad_id FROM dbo.vinculaciones_cuenta_unidades WHERE vinculacion_id = @Id", new { Id = linkId }, transaction, cancellationToken: ct)))
                .Select(UnitId.From).ToList();
            await SqlProfessionalAccountRepository.EnsureAdministratorUnitsAsync(connection, transaction, access, units, ct);
            scopeId = Guid.NewGuid();
            try
            {
                await connection.ExecuteAsync(new CommandDefinition($"""
                    {SqlProfessionalAccountRepository.InsertProfile}

                    {SqlProfessionalAccountRepository.InsertAudit}
                    """, new
                {
                    ProfileScopeId = scopeId, AccountId = redeemerId, ProfileCode = link.ProfileCode, ActorId = access.AccountId.Value,
                    CenterId = access.CenterId.Value, UnitId = (Guid?)null, ResidentId = (Guid?)null, ResourceType = "PROFILE_SCOPE",
                    ResourceId = scopeId, Action = "PROFILE_SCOPE_GRANT", OccurredAt = occurredAt,
                }, transaction, cancellationToken: ct));
            }
            catch (SqlException error) when (error.Number is 2601 or 2627)
            {
                // UX_ps_active: la cuenta ya tiene ese perfil vigente en el centro.
                throw new DomainValidationException("PROFILE_SCOPE_DUPLICATE");
            }

            await SqlProfessionalAccountRepository.InsertUnitsAsync(connection, transaction, access, scopeId.Value, units, occurredAt, ct);
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE link SET resuelta_en = @Now, resuelta_por_cuenta_id = @ActorId, resolucion = @Resolution, ambito_perfil_id = @ScopeId
              FROM dbo.vinculaciones_cuenta link WITH (FORCESEEK)
             WHERE link.id = @Id
            """, new
        {
            Id = linkId, Now = now, ActorId = access.AccountId.Value, Resolution = confirm ? "CONFIRMADA" : "RECHAZADA", ScopeId = scopeId,
        }, transaction, cancellationToken: ct));
        // El recurso es la cuenta vinculada, como en el resto de eventos de cuentas: la auditoría enseña a quién afecta.
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, "ACCOUNT", redeemerId,
            confirm ? "ACCOUNT_LINK_CONFIRM" : "ACCOUNT_LINK_REJECT", occurredAt, ct: ct);
        transaction.Commit();
        return scopeId;
    }

    /// <summary>Lo que se exige a la cuenta que se vincula, al canjear y otra vez al confirmar: activa, con segundo factor (el personal lo
    /// necesita para entrar y es lo que verifica a la persona) y sin ese mismo perfil ya vigente en el centro.</summary>
    private static async Task EnsureCanBeLinkedAsync(
        SqlConnection connection, SqlTransaction transaction, Guid accountId, LockedLinkRow link, CancellationToken ct)
    {
        var account = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition("""
            SELECT CAST(CASE WHEN account.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active,
                   CAST(CASE WHEN credential.segundo_factor_activo = 1 THEN 1 ELSE 0 END AS BIT) AS TwoFactor,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile
                                           WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId
                                             AND profile.perfil_codigo = @ProfileCode AND profile.estado = 'ACTIVE') THEN 1 ELSE 0 END AS BIT) AS HasProfile
              FROM dbo.cuentas account WITH (UPDLOCK, ROWLOCK)
              LEFT JOIN dbo.credenciales credential ON credential.cuenta_id = account.id
             WHERE account.id = @AccountId
            """, new { AccountId = accountId, link.CenterId, link.ProfileCode }, transaction, cancellationToken: ct));
        if (account is not { Active: true })
        {
            throw new DomainValidationException("ACCOUNT_LINK_INVALID");
        }
        if (!account.TwoFactor)
        {
            throw new DomainValidationException("ACCOUNT_LINK_SECOND_FACTOR");
        }
        if (account.HasProfile)
        {
            throw new DomainValidationException("PROFILE_SCOPE_DUPLICATE");
        }
    }

    private static async Task<Dictionary<Guid, IReadOnlyList<string>>> UnitNamesAsync(
        SqlConnection connection, SqlTransaction? transaction, IReadOnlyList<Guid> linkIds, CancellationToken ct)
    {
        if (linkIds.Count == 0)
        {
            return [];
        }

        var rows = await connection.QueryAsync<(Guid LinkId, string Name)>(new CommandDefinition("""
            SELECT link_unit.vinculacion_id AS LinkId, unit.nombre_visible AS Name
              FROM dbo.vinculaciones_cuenta_unidades link_unit
              JOIN dbo.unidades unit ON unit.centro_id = link_unit.centro_id AND unit.id = link_unit.unidad_id
             WHERE link_unit.vinculacion_id IN @LinkIds
             ORDER BY unit.nombre_visible
            """, new { LinkIds = linkIds }, transaction, cancellationToken: ct));
        return rows.GroupBy(row => row.LinkId).ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(row => row.Name).ToList());
    }

    private static DateTimeOffset Utc(DateTime now) => new(DateTime.SpecifyKind(now, DateTimeKind.Utc));

    private sealed record OpenRow(
        Guid Id, string ProfileCode, DateTime IssuedAt, DateTime ExpiresAt, string? RedeemedBySubject, string? RedeemedByName, DateTime? RedeemedAt);

    private sealed record LiveRow(Guid Id, string CenterName, string ProfileCode, DateTime ExpiresAt);

    private sealed record LockedLinkRow(Guid CenterId, string ProfileCode, Guid IssuerId);

    private sealed record PendingRow(string ProfileCode, Guid? RedeemerId, bool Pending);

    private sealed record AccountRow(bool Active, bool TwoFactor, bool HasProfile);
}
