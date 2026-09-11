using System.Data;
using Dapper;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Authorization;

/// <summary>
/// Traduce authorizationQuery/loadAuthorizationEvidence de
/// db/repositories/authorization-subject-repository.ts. En vez de construir un único JSON en T-SQL
/// (json_object en SQLite), aquí son dos consultas Dapper en la misma conexión: la fila de evidencia y,
/// aparte, sus permisos vigentes — más robusto entre versiones de motor y más fácil de leer.
/// </summary>
public sealed class SqlAuthorizationEvidenceProvider(SqlConnectionFactory connections) : IAuthorizationEvidenceProvider
{
    public async Task<AuthorizationEvidence?> LoadEvidenceAsync(
        string externalSubject, AuthorizationSelection selection, AuthorizationTarget target, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await LoadAsync(connection, null, externalSubject, selection, target, ct);
    }

    /// <summary>Expuesto internamente para que AuthorizationGuard pueda re-resolver la evidencia dentro
    /// de la misma transacción antes de escribir (guard TOCTOU).</summary>
    internal static async Task<AuthorizationEvidence?> LoadAsync(
        IDbConnection connection, IDbTransaction? transaction, string externalSubject,
        AuthorizationSelection selection, AuthorizationTarget target, CancellationToken ct)
    {
        var (sql, parameters) = BuildEvidenceQuery(externalSubject, selection, target);
        var row = await connection.QuerySingleOrDefaultAsync<EvidenceRow>(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        var permissions = (await connection.QueryAsync<AuthorizationPermission>(new CommandDefinition("""
            SELECT id AS Id, permission_code AS Code
              FROM dbo.profile_permissions
             WHERE profile_scope_id = @ProfileScopeId AND center_id = @CenterId AND revoked_at IS NULL
             ORDER BY permission_code, id
            """, new { row.ProfileScopeId, row.CenterId }, transaction, cancellationToken: ct))).ToList();

        return new AuthorizationEvidence(
            AccountId.From(row.AccountId), row.ProfileScopeId, EnumCode.ParseCode<SystemProfile>(row.Profile),
            CenterId.From(row.CenterId), UnitId.From(row.UnitId), row.UnitScopeId,
            row.ResidentId is { } residentId ? ResidentId.From(residentId) : null,
            row.LocationId, row.ResidentScopeId, permissions,
            row.DraftReason is { } reason ? EnumCode.ParseCode<BaselineReason>(reason) : null);
    }

    private static (string Sql, DynamicParameters Parameters) BuildEvidenceQuery(
        string externalSubject, AuthorizationSelection selection, AuthorizationTarget target)
    {
        var hasResident = target is not AuthorizationTarget.Create;

        var residentJoin = hasResident ? """
            JOIN dbo.residents resident ON resident.id = @ResidentId
                AND resident.center_id = profile.center_id AND resident.status = 'ACTIVE'
            JOIN dbo.resident_location_intervals location ON location.resident_id = resident.id
                AND location.center_id = profile.center_id AND location.unit_id = unit.id AND location.valid_until IS NULL
            JOIN dbo.resident_center_episodes episode ON episode.id = location.episode_id
                AND episode.center_id = profile.center_id AND episode.resident_id = resident.id AND episode.valid_until IS NULL
            LEFT JOIN dbo.profile_resident_scopes resident_scope ON resident_scope.profile_scope_id = profile.id
                AND resident_scope.center_id = profile.center_id AND resident_scope.resident_id = resident.id
                AND resident_scope.revoked_at IS NULL
            """ : "";

        var draftJoin = target is AuthorizationTarget.Sign ? """
            JOIN dbo.baseline_drafts draft ON draft.id = @DraftId AND draft.resident_id = resident.id
                AND draft.center_id = profile.center_id AND draft.created_in_unit_id = unit.id
                AND draft.created_by_account_id = account.id AND draft.created_by_profile = profile.profile_code
            """ : "";

        var residentPredicate = hasResident
            ? """
              AND (resident_scope.id IS NOT NULL OR (
                  profile.profile_code IN ('ADMINISTRACION', 'ENFERMERIA', 'MEDICINA', 'DIRECCION_CLINICA')
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.profile_resident_scopes restriction
                       WHERE restriction.profile_scope_id = profile.id AND restriction.center_id = profile.center_id)))
              """
            : "AND unit.id = @CreateUnitId";

        var sql = $"""
            SELECT
                account.id AS AccountId, profile.id AS ProfileScopeId, profile.profile_code AS Profile,
                profile.center_id AS CenterId, unit.id AS UnitId, unit_scope.id AS UnitScopeId,
                {(hasResident ? "resident.id" : "CAST(NULL AS UNIQUEIDENTIFIER)")} AS ResidentId,
                {(hasResident ? "location.id" : "CAST(NULL AS UNIQUEIDENTIFIER)")} AS LocationId,
                {(hasResident ? "resident_scope.id" : "CAST(NULL AS UNIQUEIDENTIFIER)")} AS ResidentScopeId,
                {(target is AuthorizationTarget.Sign ? "draft.reason_code" : "CAST(NULL AS NVARCHAR(32))")} AS DraftReason
              FROM dbo.accounts account
              JOIN dbo.profile_scopes profile ON profile.account_id = account.id
                  AND profile.id = @ProfileScopeId AND profile.center_id = @CenterId
                  AND profile.status = 'ACTIVE' AND profile.revoked_at IS NULL
              JOIN dbo.centers center ON center.id = profile.center_id AND center.status = 'ACTIVE'
              JOIN dbo.profile_unit_scopes unit_scope ON unit_scope.profile_scope_id = profile.id
                  AND unit_scope.center_id = profile.center_id AND unit_scope.revoked_at IS NULL
              JOIN dbo.units unit ON unit.id = unit_scope.unit_id AND unit.center_id = profile.center_id AND unit.status = 'ACTIVE'
              {residentJoin}
              {draftJoin}
             WHERE account.external_subject = @ExternalSubject AND account.status = 'ACTIVE'
             {residentPredicate}
            """;

        var parameters = new DynamicParameters();
        parameters.Add("ProfileScopeId", selection.ProfileScopeId);
        parameters.Add("CenterId", selection.CenterId.Value);
        parameters.Add("ExternalSubject", externalSubject);
        switch (target)
        {
            case AuthorizationTarget.Create create:
                parameters.Add("CreateUnitId", create.UnitId.Value);
                break;
            case AuthorizationTarget.Read read:
                parameters.Add("ResidentId", read.ResidentId.Value);
                break;
            case AuthorizationTarget.Sign sign:
                parameters.Add("ResidentId", sign.ResidentId.Value);
                parameters.Add("DraftId", sign.DraftId.Value);
                break;
        }
        return (sql, parameters);
    }

    private sealed record EvidenceRow(
        Guid AccountId, Guid ProfileScopeId, string Profile, Guid CenterId, Guid UnitId, Guid UnitScopeId,
        Guid? ResidentId, Guid? LocationId, Guid? ResidentScopeId, string? DraftReason);
}
