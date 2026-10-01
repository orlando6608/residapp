using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Audit;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-28: lee dbo.eventos_auditoria para Administración. Una sola consulta que:
///   - repite que quien consulta tiene su ámbito de Administración vigente (EnsureAdministratorAsync);
///   - se limita a las acciones de AdministrativeAudit, al centro del ámbito y, en los eventos con unidad, a las unidades
///     concedidas sin revocar a ese ámbito;
///   - resuelve por LEFT JOIN los nombres (quien actuó, la cuenta afectada vía ACCOUNT o PROFILE_SCOPE, la unidad, el residente);
///   - ordena por fecha descendente y trae un evento de más para saber si se trunca.
/// No devuelve valores ni texto: la tabla no los guarda.
/// </summary>
public sealed class SqlAdministrativeAuditDirectory(SqlConnectionFactory connections) : IAdministrativeAuditDirectory
{
    public async Task<AuditPage> ListAsync(AccountAdministrationAccess access, AdministrativeAuditQuery query, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var actions = query.Action is null ? AdministrativeAudit.Actions.Select(a => a.Code).ToList() : [query.Action];
        var rows = (await connection.QueryAsync<AuditRow>(new CommandDefinition($"""
            SELECT TOP ({AuditPage.MaxEntries + 1})
                   ev.ocurrido_en AS OccurredAt,
                   COALESCE(actor.nombre_visible, actor.sujeto_externo) AS ActorName,
                   ev.perfil_activo AS ActorProfileCode,
                   ev.accion_codigo AS Action,
                   COALESCE(affected.nombre_visible, affected.sujeto_externo) AS AffectedName,
                   unit.nombre_visible AS UnitName,
                   resident.nombre_visible AS ResidentName
              FROM dbo.eventos_auditoria ev
              JOIN dbo.cuentas actor ON actor.id = ev.cuenta_id
              LEFT JOIN dbo.ambitos_perfil profile_scope ON ev.tipo_recurso = 'PROFILE_SCOPE' AND profile_scope.id = ev.recurso_id
              LEFT JOIN dbo.cuentas affected ON affected.id = CASE ev.tipo_recurso
                                                                 WHEN 'ACCOUNT' THEN ev.recurso_id
                                                                 WHEN 'PROFILE_SCOPE' THEN profile_scope.cuenta_id END
              LEFT JOIN dbo.unidades unit ON unit.centro_id = ev.centro_id AND unit.id = ev.unidad_id
              LEFT JOIN dbo.residentes resident ON resident.centro_id = ev.centro_id AND resident.id = ev.residente_id
             WHERE ev.centro_id = @CenterId
               AND ev.ocurrido_en >= @FromUtc AND ev.ocurrido_en < @ToExclusiveUtc
               AND ev.accion_codigo IN @Actions
               AND (ev.unidad_id IS NULL OR EXISTS (
                        SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                         WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId
                           AND unit_scope.unidad_id = ev.unidad_id AND unit_scope.revocado_en IS NULL))
               AND (@AffectedAccountId IS NULL OR affected.id = @AffectedAccountId)
             ORDER BY ev.ocurrido_en DESC, ev.id
            """, new
        {
            CenterId = access.CenterId.Value, access.ProfileScopeId, query.FromUtc, query.ToExclusiveUtc, Actions = actions,
            AffectedAccountId = query.AffectedAccountId?.Value,
        }, cancellationToken: ct))).ToList();

        var truncated = rows.Count > AuditPage.MaxEntries;
        return new AuditPage(
            rows.Take(AuditPage.MaxEntries).Select(row => new AuditEntry(
                new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)), row.ActorName,
                EnumCode.ParseCode<SystemProfile>(row.ActorProfileCode), row.Action, row.AffectedName, row.UnitName, row.ResidentName)).ToList(),
            truncated);
    }

    private sealed record AuditRow(
        DateTime OccurredAt, string ActorName, string ActorProfileCode, string Action, string? AffectedName, string? UnitName,
        string? ResidentName);
}
