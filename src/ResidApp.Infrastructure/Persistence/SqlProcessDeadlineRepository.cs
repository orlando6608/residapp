using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Plazos de los hitos del proceso por centro (script 0046). Mismo patrón que SqlReferenceRangeRepository: el permiso
/// PROCESS_DEADLINES_MANAGE se comprueba en SQL sobre el ámbito concreto, la versión es el número de filas de historial del centro y al
/// guardar se lee con UPDLOCK/HOLDLOCK para que dos guardados simultáneos no pasen ambos la comprobación. La tabla solo guarda lo que
/// difiere de los plazos de CJ (ProcessMilestoneRules.Defaults).
/// </summary>
public sealed class SqlProcessDeadlineRepository(SqlConnectionFactory connections) : IProcessDeadlineRepository
{
    public async Task<ProcessDeadlinesView> ReadAsync(ProcessDeadlinesAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await EnsurePermissionAsync(connection, null, access, ct);

        var deadlines = await ReadEffectiveAsync(connection, null, access.CenterId, ct);
        var history = (await connection.QueryAsync<HistoryRow>(new CommandDefinition("""
            SELECT hito_codigo AS Code, plazo_anterior_minutos AS PreviousNormal, prioritario_anterior_minutos AS PreviousPriority,
                   plazo_nuevo_minutos AS NewNormal, prioritario_nuevo_minutos AS NewPriority, cambiado_por_perfil AS ProfileCode,
                   CAST(CASE WHEN cambiado_por_cuenta_id = @AccountId THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount,
                   cambiado_en AS ChangedAt
              FROM dbo.plazos_hitos_centro_historial
             WHERE centro_id = @CenterId
             ORDER BY cambiado_en DESC, hito_codigo
            """, new { AccountId = access.AccountId.Value, CenterId = access.CenterId.Value }, cancellationToken: ct)))
            .Select(h => new ProcessDeadlineChange(
                EnumCode.ParseCode<ProcessMilestone>(h.Code), Deadline(h.PreviousNormal, h.PreviousPriority), Deadline(h.NewNormal, h.NewPriority),
                EnumCode.ParseCode<SystemProfile>(h.ProfileCode), h.ByCurrentAccount, new DateTimeOffset(h.ChangedAt, TimeSpan.Zero)))
            .ToList();

        return new ProcessDeadlinesView(deadlines, history, history.Count);
    }

    public async Task<int> SaveAsync(SaveProcessDeadlinesInput input, CancellationToken ct = default)
    {
        var access = input.Access;
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await EnsurePermissionAsync(connection, transaction, access, ct);

        var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.plazos_hitos_centro_historial WITH (UPDLOCK, HOLDLOCK) WHERE centro_id = @CenterId
            """, new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (version != input.ExpectedVersion)
        {
            throw new DomainValidationException("PROCESS_DEADLINES_REVISION_CONFLICT");
        }

        var current = await ReadEffectiveAsync(connection, transaction, access.CenterId, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        var changes = 0;
        foreach (var milestone in ProcessMilestoneRules.All)
        {
            var before = current[milestone];
            var after = input.Deadlines.GetValueOrDefault(milestone, before);
            if (before == after)
            {
                continue;
            }

            var isDefault = after == ProcessMilestoneRules.Defaults[milestone];
            var parameters = new
            {
                CenterId = access.CenterId.Value, Code = milestone.ToCode(), NewNormal = ProcessMilestoneRules.ToMinutes(after.Normal),
                NewPriority = ProcessMilestoneRules.ToMinutes(after.Priority), PreviousNormal = ProcessMilestoneRules.ToMinutes(before.Normal),
                PreviousPriority = ProcessMilestoneRules.ToMinutes(before.Priority), AccountId = access.AccountId.Value,
                Profile = access.ActiveProfile.ToCode(), OccurredAt = occurredAt, Id = Guid.NewGuid(),
            };
            // Sin fila vale el plazo de CJ: volver a él borra la fila del centro.
            await connection.ExecuteAsync(new CommandDefinition("""
                DELETE FROM dbo.plazos_hitos_centro WHERE centro_id = @CenterId AND hito_codigo = @Code;
                """ + (isDefault ? "" : """

                INSERT INTO dbo.plazos_hitos_centro (centro_id, hito_codigo, plazo_minutos, plazo_prioritario_minutos)
                VALUES (@CenterId, @Code, @NewNormal, @NewPriority);
                """) + """

                INSERT INTO dbo.plazos_hitos_centro_historial
                    (id, centro_id, hito_codigo, plazo_anterior_minutos, prioritario_anterior_minutos, plazo_nuevo_minutos,
                     prioritario_nuevo_minutos, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
                VALUES (@Id, @CenterId, @Code, @PreviousNormal, @PreviousPriority, @NewNormal, @NewPriority, @AccountId, @Profile, @OccurredAt);
                """, parameters, transaction, cancellationToken: ct));
            changes++;
        }

        if (changes > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                VALUES (@Id, @AccountId, @Profile, @CenterId, 'PROCESS_DEADLINES', @CenterId, 'PROCESS_DEADLINES_UPDATE', @OccurredAt)
                """, new
            {
                Id = Guid.NewGuid(), AccountId = access.AccountId.Value, Profile = access.ActiveProfile.ToCode(),
                CenterId = access.CenterId.Value, OccurredAt = occurredAt,
            }, transaction, cancellationToken: ct));
        }

        transaction.Commit();
        return version + changes;
    }

    public async Task<IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline>> GetEffectiveAsync(CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await ReadEffectiveAsync(connection, null, centerId, ct);
    }

    private static async Task EnsurePermissionAsync(
        SqlConnection connection, SqlTransaction? transaction, ProcessDeadlinesAccess access, CancellationToken ct)
    {
        var allowed = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)
              FROM dbo.ambitos_perfil profile
              JOIN dbo.permisos_perfil permission ON permission.ambito_perfil_id = profile.id AND permission.centro_id = profile.centro_id
                   AND permission.permiso_codigo = 'PROCESS_DEADLINES_MANAGE' AND permission.revocado_en IS NULL
             WHERE profile.id = @ProfileScopeId AND profile.cuenta_id = @AccountId AND profile.centro_id = @CenterId
               AND profile.perfil_codigo = @Profile AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
            """, new
        {
            access.ProfileScopeId, AccountId = access.AccountId.Value, CenterId = access.CenterId.Value, Profile = access.ActiveProfile.ToCode(),
        }, transaction, cancellationToken: ct));
        if (allowed == 0)
        {
            throw new DomainValidationException("PROCESS_DEADLINES_NOT_AUTHORIZED");
        }
    }

    /// <summary>Los plazos de CJ con lo que el centro haya cambiado por encima.</summary>
    private static async Task<IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline>> ReadEffectiveAsync(
        SqlConnection connection, SqlTransaction? transaction, CenterId centerId, CancellationToken ct)
    {
        var effective = ProcessMilestoneRules.Defaults.ToDictionary(d => d.Key, d => d.Value);
        var rows = await connection.QueryAsync<DeadlineRow>(new CommandDefinition("""
            SELECT hito_codigo AS Code, plazo_minutos AS Normal, plazo_prioritario_minutos AS Priority
              FROM dbo.plazos_hitos_centro WHERE centro_id = @CenterId
            """, new { CenterId = centerId.Value }, transaction, cancellationToken: ct));
        foreach (var row in rows)
        {
            effective[EnumCode.ParseCode<ProcessMilestone>(row.Code)] = Deadline(row.Normal, row.Priority);
        }

        return effective;
    }

    private static MilestoneDeadline Deadline(int? normal, int? priority) =>
        new(normal is { } n ? TimeSpan.FromMinutes(n) : null, priority is { } p ? TimeSpan.FromMinutes(p) : null);

    private sealed record DeadlineRow(string Code, int? Normal, int? Priority);

    private sealed record HistoryRow(
        string Code, int? PreviousNormal, int? PreviousPriority, int? NewNormal, int? NewPriority, string ProfileCode,
        bool ByCurrentAccount, DateTime ChangedAt);
}
