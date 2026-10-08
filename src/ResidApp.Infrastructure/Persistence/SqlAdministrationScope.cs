using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Administración «principal» del centro (script 0047; CJ, 2026-10-07). La marca es el último cambio de
/// dbo.administraciones_principales_cambios de un ámbito de Administración. Las escrituras comprueban de nuevo, dentro de su transacción,
/// que quien actúa es una Administración vigente y principal, y se auditan sin datos (ADMIN_PRINCIPAL_SET, ADMIN_SCOPE_UNIT_ADD).
/// </summary>
public sealed class SqlAdministrationScope(SqlConnectionFactory connections) : IAdministrationScopeDirectory, IAdministrationScopeRepository
{
    public async Task<bool> IsPrincipalAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await IsPrincipalAsync(connection, null, access.ProfileScopeId, ct);
    }

    public async Task<IReadOnlyList<CenterUnitScope>> ListCenterUnitsAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await ListUnitsAsync(connection, null, access.CenterId, access.ProfileScopeId, ct);
    }

    public async Task AddUnitToOwnScopeAsync(AccountAdministrationAccess access, UnitId unitId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        await LockScopeAsync(connection, transaction, access.ProfileScopeId, ct);
        if (!await IsPrincipalAsync(connection, transaction, access.ProfileScopeId, ct))
        {
            throw new DomainValidationException("ADMIN_PRINCIPAL_NOT_AUTHORIZED");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await AddUnitAsync(connection, transaction, access.CenterId, access.ProfileScopeId, unitId, access.AccountId, "ADMINISTRACION", occurredAt, ct);
        transaction.Commit();
    }

    public async Task SetPrincipalAsync(
        AccountAdministrationAccess access, Guid targetProfileScopeId, bool principal, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        // El bloqueo del centro ordena los cambios simultáneos de marca, para que «al menos una» sea fiable.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM dbo.centros WITH (UPDLOCK, ROWLOCK) WHERE id = @CenterId", new { CenterId = access.CenterId.Value },
            transaction, cancellationToken: ct));
        if (!await IsPrincipalAsync(connection, transaction, access.ProfileScopeId, ct))
        {
            throw new DomainValidationException("ADMIN_PRINCIPAL_NOT_AUTHORIZED");
        }

        await EnsureAdministrationScopeAsync(connection, transaction, access.CenterId, targetProfileScopeId, ct);
        if (!principal && await CountPrincipalsAsync(connection, transaction, access.CenterId, ct) <= 1
            && await IsPrincipalAsync(connection, transaction, targetProfileScopeId, ct))
        {
            throw new DomainValidationException("ADMIN_PRINCIPAL_LAST");
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await AppendPrincipalChangeAsync(connection, transaction, access.CenterId, targetProfileScopeId, principal, access.AccountId, "ADMINISTRACION", occurredAt, ct);
        transaction.Commit();
    }

    /// <summary>Si el último cambio de marca del ámbito es «principal».</summary>
    internal static async Task<bool> IsPrincipalAsync(SqlConnection connection, SqlTransaction? transaction, Guid profileScopeId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<bool?>(new CommandDefinition("""
            SELECT TOP (1) principal FROM dbo.administraciones_principales_cambios WHERE ambito_perfil_id = @ProfileScopeId ORDER BY numero DESC
            """, new { ProfileScopeId = profileScopeId }, transaction, cancellationToken: ct)) ?? false;

    /// <summary>Los ámbitos de Administración vigentes del centro cuya marca vigente es «principal».</summary>
    internal static Task<int> CountPrincipalsAsync(SqlConnection connection, SqlTransaction transaction, CenterId centerId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.ambitos_perfil profile
             WHERE profile.centro_id = @CenterId AND profile.perfil_codigo = 'ADMINISTRACION' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
               AND (SELECT TOP (1) change.principal FROM dbo.administraciones_principales_cambios change
                     WHERE change.ambito_perfil_id = profile.id ORDER BY change.numero DESC) = 1
            """, new { CenterId = centerId.Value }, transaction, cancellationToken: ct));

    internal static async Task<IReadOnlyList<CenterUnitScope>> ListUnitsAsync(
        SqlConnection connection, SqlTransaction? transaction, CenterId centerId, Guid profileScopeId, CancellationToken ct) =>
        (await connection.QueryAsync<UnitRow>(new CommandDefinition("""
            SELECT unit.id AS UnitId, unit.nombre_visible AS Name,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                                           WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.unidad_id = unit.id
                                             AND unit_scope.centro_id = unit.centro_id AND unit_scope.revocado_en IS NULL)
                        THEN 1 ELSE 0 END AS BIT) AS InScope
              FROM dbo.unidades unit
             WHERE unit.centro_id = @CenterId AND unit.estado = 'ACTIVE'
             ORDER BY unit.nombre_visible
            """, new { CenterId = centerId.Value, ProfileScopeId = profileScopeId }, transaction, cancellationToken: ct)))
        .Select(u => new CenterUnitScope(UnitId.From(u.UnitId), u.Name, u.InScope)).ToList();

    /// <summary>Añade una unidad activa del centro al ámbito de Administración indicado y lo audita con el perfil de quien actúa
    /// (ADMINISTRACION o PLATAFORMA).</summary>
    internal static async Task AddUnitAsync(
        SqlConnection connection, SqlTransaction transaction, CenterId centerId, Guid targetProfileScopeId, UnitId unitId, AccountId actor,
        string actorProfile, DateTimeOffset occurredAt, CancellationToken ct)
    {
        await EnsureAdministrationScopeAsync(connection, transaction, centerId, targetProfileScopeId, ct);
        var parameters = new
        {
            ProfileScopeId = targetProfileScopeId, CenterId = centerId.Value, UnitId = unitId.Value, ActorId = actor.Value, Profile = actorProfile,
            OccurredAt = occurredAt,
        };
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM dbo.unidades WHERE id = @UnitId AND centro_id = @CenterId AND estado = 'ACTIVE'",
                parameters, transaction, cancellationToken: ct)) == 0)
        {
            throw new DomainValidationException("PROFILE_SCOPE_INVALID");
        }

        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.ambitos_perfil_unidad
                 WHERE ambito_perfil_id = @ProfileScopeId AND unidad_id = @UnitId AND revocado_en IS NULL
                """, parameters, transaction, cancellationToken: ct)) > 0)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, @OccurredAt, @ActorId);

            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @ActorId, @Profile, @CenterId, @UnitId, NULL, 'PROFILE_SCOPE', @ProfileScopeId, 'ADMIN_SCOPE_UNIT_ADD', @OccurredAt);
            """, parameters, transaction, cancellationToken: ct));
    }

    /// <summary>Escribe el siguiente cambio de marca del ámbito. Si ya tiene esa marca, ACCOUNT_CHANGE_CONFLICT.</summary>
    internal static async Task AppendPrincipalChangeAsync(
        SqlConnection connection, SqlTransaction transaction, CenterId centerId, Guid targetProfileScopeId, bool principal, AccountId actor,
        string actorProfile, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var last = await connection.QuerySingleOrDefaultAsync<(int Number, bool Principal)?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, principal AS Principal
              FROM dbo.administraciones_principales_cambios WITH (UPDLOCK, HOLDLOCK)
             WHERE ambito_perfil_id = @ProfileScopeId ORDER BY numero DESC
            """, new { ProfileScopeId = targetProfileScopeId }, transaction, cancellationToken: ct));
        if ((last?.Principal ?? false) == principal)
        {
            throw new DomainValidationException("ACCOUNT_CHANGE_CONFLICT");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.administraciones_principales_cambios
                (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @CenterId, @ProfileScopeId, @Number, @Principal, @ActorId, @Profile, @OccurredAt);

            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @ActorId, @Profile, @CenterId, NULL, NULL, 'PROFILE_SCOPE', @ProfileScopeId, 'ADMIN_PRINCIPAL_SET', @OccurredAt);
            """, new
        {
            CenterId = centerId.Value, ProfileScopeId = targetProfileScopeId, Number = (last?.Number ?? 0) + 1, Principal = principal,
            ActorId = actor.Value, Profile = actorProfile, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
    }

    /// <summary>El ámbito indicado es de Administración, vigente y del centro; si no, acceso denegado (sin decir si existe).</summary>
    internal static async Task EnsureAdministrationScopeAsync(
        SqlConnection connection, SqlTransaction transaction, CenterId centerId, Guid profileScopeId, CancellationToken ct)
    {
        if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.ambitos_perfil WITH (UPDLOCK, ROWLOCK)
                 WHERE id = @ProfileScopeId AND centro_id = @CenterId AND perfil_codigo = 'ADMINISTRACION' AND estado = 'ACTIVE' AND revocado_en IS NULL
                """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, transaction, cancellationToken: ct)) == 0)
        {
            throw new AccessDeniedException();
        }
    }

    private static Task LockScopeAsync(SqlConnection connection, SqlTransaction transaction, Guid profileScopeId, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM dbo.ambitos_perfil WITH (UPDLOCK, ROWLOCK) WHERE id = @ProfileScopeId", new { ProfileScopeId = profileScopeId },
            transaction, cancellationToken: ct));

    private sealed record UnitRow(Guid UnitId, string Name, bool InScope);
}
