using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-12/ADM-13 (0023): escrituras sobre cuentas profesionales, sus perfiles, unidades, residentes de Auxiliar y permisos
/// configurables (0024, del catálogo de ProfilePermissions). Cada una va en una transacción que:
///   1. vuelve a comprobar que quien gestiona tiene su ámbito de Administración vigente (cuenta y centro activos), como
///      SqlReferenceRangeRepository comprueba su permiso;
///   2. bloquea la cuenta afectada (UPDLOCK), lo que ordena los cambios simultáneos sobre una misma cuenta; la propia
///      cuenta de quien gestiona no se puede cambiar, y una cuenta sin perfiles en el centro se trata como ajena;
///   3. escribe y deja su evento en dbo.eventos_auditoria, sin datos.
/// Las concesiones solo se revocan (triggers de 0002). Las unidades que se conceden o revocan deben estar en el ámbito de
/// quien gestiona; los residentes que se asignan, también (AssignableResidentsSelect).
/// </summary>
public sealed class SqlProfessionalAccountRepository(SqlConnectionFactory connections) : IProfessionalAccountRepository
{
    private const string InsertAudit = """
        INSERT INTO dbo.eventos_auditoria
            (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
        VALUES (NEWID(), @ActorId, 'ADMINISTRACION', @CenterId, @UnitId, @ResidentId, @ResourceType, @ResourceId, @Action, @OccurredAt);
        """;

    private const string InsertProfile = """
        INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
        VALUES (@ProfileScopeId, @AccountId, @CenterId, @ProfileCode, 'ACTIVE', @OccurredAt, @ActorId);
        """;

    private const string InsertUnit = """
        INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
        VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @OccurredAt, @ActorId);
        """;

    public async Task<AccountId> CreateAsync(
        AccountAdministrationAccess access, Guid operationId, ProfessionalAccountData data, SystemProfile profile,
        IReadOnlyList<UnitId> units, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        if (await FindCreatedAsync(connection, transaction, access, operationId, data, ct) is { } created)
        {
            return created;
        }

        await EnsureAdministratorUnitsAsync(connection, transaction, access, units, ct);
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.cuentas WHERE sujeto_externo = @Subject", new { data.Subject }, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("ACCOUNT_SUBJECT_TAKEN");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var profileScopeId = Guid.NewGuid();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en)
                VALUES (@AccountId, @Subject, @DisplayName, 'ACTIVE', @OccurredAt);

                {InsertProfile}

                {InsertAudit}
                """, new
            {
                AccountId = operationId, data.Subject, data.DisplayName, ProfileScopeId = profileScopeId, ProfileCode = profile.ToCode(),
                ActorId = access.AccountId.Value, CenterId = access.CenterId.Value, UnitId = (Guid?)null, ResidentId = (Guid?)null,
                ResourceType = "ACCOUNT", ResourceId = operationId, Action = "ACCOUNT_CREATE", OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // Un reenvío simultáneo del mismo formulario ya la creó, u otra cuenta se ha llevado el identificador.
            transaction.Rollback();
            using var retry = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            return await FindCreatedAsync(connection, retry, access, operationId, data, ct)
                ?? throw new DomainValidationException("ACCOUNT_SUBJECT_TAKEN");
        }

        await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_SCOPE_GRANT", occurredAt, ct: ct);
        await InsertUnitsAsync(connection, transaction, access, profileScopeId, units, occurredAt, ct);
        transaction.Commit();
        return AccountId.From(operationId);
    }

    public async Task RenameAsync(AccountAdministrationAccess access, AccountId accountId, string displayName, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        var account = await LockAccountAsync(connection, transaction, access, accountId, ct);
        if (string.Equals(account.DisplayName, displayName, StringComparison.Ordinal))
        {
            throw new DomainValidationException(ProfessionalAccount.InvalidCode);
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.cuentas SET nombre_visible = @DisplayName WHERE id = @AccountId",
            new { DisplayName = displayName, AccountId = accountId.Value }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, "ACCOUNT", accountId.Value, "ACCOUNT_RENAME", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task ChangeStatusAsync(AccountAdministrationAccess access, AccountId accountId, AccountStatus status, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        var account = await LockAccountAsync(connection, transaction, access, accountId, ct);
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @AccountId AND centro_id <> @CenterId AND estado = 'ACTIVE'
                """, new { AccountId = accountId.Value, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct)) > 0)
        {
            // ADR 0005: retirar un centro no suspende la cuenta global; aquí solo se revocan los perfiles de este centro.
            throw new DomainValidationException("ACCOUNT_OTHER_CENTERS");
        }

        if (EnumCode.ParseCode<AccountStatus>(account.StatusCode) == status)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.cuentas SET estado = @StatusCode WHERE id = @AccountId",
            new { StatusCode = status.ToCode(), AccountId = accountId.Value }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, "ACCOUNT", accountId.Value,
            status == AccountStatus.Suspended ? "ACCOUNT_SUSPEND" : "ACCOUNT_ACTIVATE", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task<Guid> GrantProfileAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid operationId, SystemProfile profile, IReadOnlyList<UnitId> units,
        CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        var previous = await connection.QuerySingleOrDefaultAsync<PreviousProfileRow>(new CommandDefinition("""
            SELECT cuenta_id AS AccountId, centro_id AS CenterId, perfil_codigo AS ProfileCode FROM dbo.ambitos_perfil WHERE id = @Id
            """, new { Id = operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            return previous.AccountId == accountId.Value && previous.CenterId == access.CenterId.Value && previous.ProfileCode == profile.ToCode()
                ? operationId
                : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
        }

        await EnsureAdministratorUnitsAsync(connection, transaction, access, units, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                {InsertProfile}

                {InsertAudit}
                """, new
            {
                ProfileScopeId = operationId, AccountId = accountId.Value, ProfileCode = profile.ToCode(), ActorId = access.AccountId.Value,
                CenterId = access.CenterId.Value, UnitId = (Guid?)null, ResidentId = (Guid?)null, ResourceType = "PROFILE_SCOPE",
                ResourceId = operationId, Action = "PROFILE_SCOPE_GRANT", OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // UX_ps_active: la cuenta ya tiene ese perfil vigente en el centro.
            throw new DomainValidationException("PROFILE_SCOPE_DUPLICATE");
        }

        await InsertUnitsAsync(connection, transaction, access, operationId, units, occurredAt, ct);
        transaction.Commit();
        return operationId;
    }

    public async Task RevokeProfileAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        await FindActiveProfileAsync(connection, transaction, access, accountId, profileScopeId, ct);

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = @OccurredAt, revocado_por_cuenta_id = @ActorId
             WHERE id = @ProfileScopeId AND centro_id = @CenterId AND estado = 'ACTIVE'
            """, new { ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value, OccurredAt = occurredAt, ActorId = access.AccountId.Value },
            transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_SCOPE_REVOKE", occurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task GrantUnitAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, UnitId unitId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        await FindActiveProfileAsync(connection, transaction, access, accountId, profileScopeId, ct);
        await EnsureAdministratorUnitsAsync(connection, transaction, access, [unitId], ct);
        if ((await ActiveUnitsAsync(connection, transaction, access, profileScopeId, ct)).Contains(unitId.Value))
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await InsertUnitsAsync(connection, transaction, access, profileScopeId, [unitId], DateTimeOffset.UtcNow, ct);
        transaction.Commit();
    }

    public async Task RevokeUnitAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, UnitId unitId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        await FindActiveProfileAsync(connection, transaction, access, accountId, profileScopeId, ct);
        await EnsureAdministratorUnitsAsync(connection, transaction, access, [unitId], ct);
        var active = await ActiveUnitsAsync(connection, transaction, access, profileScopeId, ct);
        if (!active.Contains(unitId.Value))
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        if (active.Count == 1)
        {
            // Un perfil vigente sin unidades no autoriza nada: se revoca el perfil.
            throw new DomainValidationException("PROFILE_UNIT_LAST");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ambitos_perfil_unidad SET revocado_en = @OccurredAt, revocado_por_cuenta_id = @ActorId
             WHERE ambito_perfil_id = @ProfileScopeId AND centro_id = @CenterId AND unidad_id = @UnitId AND revocado_en IS NULL
            """, new
        {
            ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value, UnitId = unitId.Value, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_UNIT_REVOKE", occurredAt, unitId.Value, ct: ct);
        transaction.Commit();
    }

    public async Task GrantResidentAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        await FindAuxiliarProfileAsync(connection, transaction, access, accountId, profileScopeId, ct);
        var parameters = new
        {
            TargetScopeId = profileScopeId, access.ProfileScopeId, CenterId = access.CenterId.Value, ResidentId = residentId.Value,
        };
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.ambitos_perfil_residente
                 WHERE ambito_perfil_id = @TargetScopeId AND centro_id = @CenterId AND residente_id = @ResidentId AND revocado_en IS NULL
                """, parameters, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        var unit = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            $"SELECT assignable.UnitId FROM ({SqlProfessionalAccountDirectory.AssignableResidentsSelect}) assignable WHERE assignable.ResidentId = @ResidentId",
            parameters, transaction, cancellationToken: ct))
            ?? throw new DomainValidationException("PROFILE_SCOPE_INVALID");

        var occurredAt = DateTimeOffset.UtcNow;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ResourceId, @CenterId, @ResidentId, @OccurredAt, @ActorId);

                {InsertAudit}
                """, new
            {
                ActorId = access.AccountId.Value, CenterId = access.CenterId.Value, UnitId = unit, ResidentId = residentId.Value,
                ResourceType = "PROFILE_SCOPE", ResourceId = profileScopeId, Action = "PROFILE_RESIDENT_GRANT", OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        transaction.Commit();
    }

    public async Task RevokeResidentAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        await FindAuxiliarProfileAsync(connection, transaction, access, accountId, profileScopeId, ct);

        // Retirar el último residente no amplía el acceso: el perfil Auxiliar se queda sin nadie (ADR 0004).
        var occurredAt = DateTimeOffset.UtcNow;
        var revoked = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ambitos_perfil_residente SET revocado_en = @OccurredAt, revocado_por_cuenta_id = @ActorId
             WHERE ambito_perfil_id = @ProfileScopeId AND centro_id = @CenterId AND residente_id = @ResidentId AND revocado_en IS NULL
            """, new
        {
            ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value, ResidentId = residentId.Value, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        if (revoked == 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_RESIDENT_REVOKE", occurredAt,
            residentId: residentId.Value, ct: ct);
        transaction.Commit();
    }

    public async Task GrantPermissionAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, string permissionCode, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await FindPermissionProfileAsync(connection, transaction, access, accountId, profileScopeId, permissionCode, ct);
        var parameters = new
        {
            ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value, Code = permissionCode, OccurredAt = DateTimeOffset.UtcNow,
            ActorId = access.AccountId.Value,
        };
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.permisos_perfil
                 WHERE ambito_perfil_id = @ProfileScopeId AND centro_id = @CenterId AND permiso_codigo = @Code AND revocado_en IS NULL
                """, parameters, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @ProfileScopeId, @CenterId, @Code, @OccurredAt, @ActorId);
                """, parameters, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_PERMISSION_GRANT", parameters.OccurredAt, ct: ct);
        transaction.Commit();
    }

    public async Task RevokePermissionAsync(
        AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId, string permissionCode, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await FindPermissionProfileAsync(connection, transaction, access, accountId, profileScopeId, permissionCode, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        var revoked = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.permisos_perfil SET revocado_en = @OccurredAt, revocado_por_cuenta_id = @ActorId
             WHERE ambito_perfil_id = @ProfileScopeId AND centro_id = @CenterId AND permiso_codigo = @Code AND revocado_en IS NULL
            """, new
        {
            ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value, Code = permissionCode, OccurredAt = occurredAt,
            ActorId = access.AccountId.Value,
        }, transaction, cancellationToken: ct));
        if (revoked == 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_PERMISSION_REVOKE", occurredAt, ct: ct);
        transaction.Commit();
    }

    /// <summary>Los pasos comunes de conceder o revocar un permiso: quien gestiona, la cuenta bloqueada, el perfil vigente y
    /// el permiso del catálogo de ese perfil (si no, PROFILE_SCOPE_INVALID).</summary>
    private static async Task FindPermissionProfileAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId,
        string permissionCode, CancellationToken ct)
    {
        await EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockAccountAsync(connection, transaction, access, accountId, ct);
        var profile = await FindActiveProfileAsync(connection, transaction, access, accountId, profileScopeId, ct);
        if (!ProfilePermissions.For(profile).Contains(permissionCode))
        {
            throw new DomainValidationException("PROFILE_SCOPE_INVALID");
        }
    }

    /// <summary>Quien gestiona sigue teniendo su ámbito de Administración vigente, con la cuenta y el centro activos.</summary>
    internal static async Task EnsureAdministratorAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, CancellationToken ct)
    {
        var allowed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.id = @ProfileScopeId
                   AND profile.centro_id = @CenterId AND profile.perfil_codigo = 'ADMINISTRACION'
                   AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
             WHERE account.id = @AccountId AND account.estado = 'ACTIVE'
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value },
            transaction, cancellationToken: ct));
        if (allowed == 0)
        {
            throw new AccessDeniedException();
        }
    }

    /// <summary>Bloquea la cuenta gestionada. No puede ser la de quien gestiona, y tiene que tener algún perfil en el centro
    /// (si no, se trata como ajena, sin decir si existe).</summary>
    private static async Task<AccountRow> LockAccountAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, AccountId accountId, CancellationToken ct)
    {
        if (accountId == access.AccountId)
        {
            throw new DomainValidationException("ACCOUNT_SELF_CHANGE");
        }

        return await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition("""
            SELECT account.estado AS StatusCode, account.nombre_visible AS DisplayName
              FROM dbo.cuentas account WITH (UPDLOCK, ROWLOCK)
             WHERE account.id = @AccountId
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId)
            """, new { AccountId = accountId.Value, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
    }

    /// <summary>Un perfil vigente y concedible de la cuenta en el centro. Familiar no se gestiona desde aquí; un perfil ya
    /// revocado es un conflicto (otro cambio se ha adelantado).</summary>
    private static async Task<SystemProfile> FindActiveProfileAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition("""
            SELECT perfil_codigo AS ProfileCode, estado AS StatusCode
              FROM dbo.ambitos_perfil
             WHERE id = @ProfileScopeId AND cuenta_id = @AccountId AND centro_id = @CenterId
            """, new { ProfileScopeId = profileScopeId, AccountId = accountId.Value, CenterId = access.CenterId.Value },
            transaction, cancellationToken: ct))
            ?? throw new AccessDeniedException();
        var profile = EnumCode.ParseCode<SystemProfile>(row.ProfileCode);
        if (!ProfessionalAccount.IsGrantable(profile))
        {
            throw new DomainValidationException("PROFILE_SCOPE_INVALID");
        }

        return row.StatusCode == "ACTIVE" ? profile : throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
    }

    private static async Task FindAuxiliarProfileAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, AccountId accountId, Guid profileScopeId,
        CancellationToken ct)
    {
        if (await FindActiveProfileAsync(connection, transaction, access, accountId, profileScopeId, ct) != SystemProfile.Auxiliar)
        {
            throw new DomainValidationException("PROFILE_SCOPE_INVALID");
        }
    }

    /// <summary>Todas las unidades están concedidas, sin revocar, al ámbito de quien gestiona, y activas.</summary>
    private static async Task EnsureAdministratorUnitsAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, IReadOnlyList<UnitId> units,
        CancellationToken ct)
    {
        var requested = units.Select(u => u.Value).Distinct().ToList();
        var granted = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(DISTINCT unit.id)
              FROM dbo.ambitos_perfil_unidad unit_scope
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = unit_scope.centro_id AND unit.estado = 'ACTIVE'
             WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId
               AND unit_scope.revocado_en IS NULL AND unit.id IN @Units
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, Units = requested }, transaction, cancellationToken: ct));
        if (requested.Count == 0 || granted != requested.Count)
        {
            throw new DomainValidationException("PROFILE_SCOPE_INVALID");
        }
    }

    private static async Task<List<Guid>> ActiveUnitsAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid profileScopeId, CancellationToken ct) =>
        (await connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT unidad_id FROM dbo.ambitos_perfil_unidad
             WHERE ambito_perfil_id = @ProfileScopeId AND centro_id = @CenterId AND revocado_en IS NULL
            """, new { ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct))).ToList();

    private static async Task InsertUnitsAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid profileScopeId,
        IReadOnlyList<UnitId> units, DateTimeOffset occurredAt, CancellationToken ct)
    {
        foreach (var unitId in units)
        {
            await connection.ExecuteAsync(new CommandDefinition(InsertUnit, new
            {
                ProfileScopeId = profileScopeId, CenterId = access.CenterId.Value, UnitId = unitId.Value, OccurredAt = occurredAt,
                ActorId = access.AccountId.Value,
            }, transaction, cancellationToken: ct));
            await AuditAsync(connection, transaction, access, "PROFILE_SCOPE", profileScopeId, "PROFILE_UNIT_GRANT", occurredAt, unitId.Value, ct: ct);
        }
    }

    internal static Task AuditAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, string resourceType, Guid resourceId,
        string action, DateTimeOffset occurredAt, Guid? unitId = null, Guid? residentId = null, CancellationToken ct = default) =>
        connection.ExecuteAsync(new CommandDefinition(InsertAudit, new
        {
            ActorId = access.AccountId.Value, CenterId = access.CenterId.Value, UnitId = unitId, ResidentId = residentId,
            ResourceType = resourceType, ResourceId = resourceId, Action = action, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));

    /// <summary>La cuenta ya creada con este identificador de operación; null si no existe. Si existe pero con otro
    /// sujeto o sin perfiles en este centro, el identificador se ha reutilizado para otra petición.</summary>
    private static async Task<AccountId?> FindCreatedAsync(
        SqlConnection connection, SqlTransaction transaction, AccountAdministrationAccess access, Guid operationId,
        ProfessionalAccountData data, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<CreatedRow>(new CommandDefinition("""
            SELECT account.sujeto_externo AS Subject,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile
                                           WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId) THEN 1 ELSE 0 END AS BIT) AS InCenter
              FROM dbo.cuentas account
             WHERE account.id = @AccountId
            """, new { AccountId = operationId, CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        return row.InCenter && string.Equals(row.Subject, data.Subject, StringComparison.OrdinalIgnoreCase)
            ? AccountId.From(operationId)
            : throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
    }

    private sealed record AccountRow(string StatusCode, string? DisplayName);

    private sealed record ProfileRow(string ProfileCode, string StatusCode);

    private sealed record PreviousProfileRow(Guid AccountId, Guid CenterId, string ProfileCode);

    private sealed record CreatedRow(string Subject, bool InCenter);
}
