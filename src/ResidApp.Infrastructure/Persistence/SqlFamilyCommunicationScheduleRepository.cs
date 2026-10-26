using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Hora de publicación y margen de los comunicados por centro (script 0051). Mismo patrón que SqlProcessDeadlineRepository: la versión es el
/// número de filas de historial del centro y al guardar se lee con UPDLOCK/HOLDLOCK para que dos guardados simultáneos no pasen ambos la
/// comprobación. La tabla solo guarda lo que difiere del horario de CJ (FamilyCommunicationScheduleSettings.Default). Leer y guardar los hace
/// Administración del centro, comprobado en la misma transacción.
/// </summary>
public sealed class SqlFamilyCommunicationScheduleRepository(SqlConnectionFactory connections) : IFamilyCommunicationScheduleRepository
{
    public async Task<FamilyCommunicationScheduleView> ReadAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var settings = await ReadEffectiveAsync(connection, null, access.CenterId, ct);
        var history = (await connection.QueryAsync<HistoryRow>(new CommandDefinition("""
            SELECT hora_anterior AS PreviousTime, margen_anterior_minutos AS PreviousMargin, resumen_anterior_codigo AS PreviousSummary,
                   hora_nueva AS NewTime, margen_nuevo_minutos AS NewMargin, resumen_nuevo_codigo AS NewSummary,
                   CAST(CASE WHEN cambiado_por_cuenta_id = @AccountId THEN 1 ELSE 0 END AS BIT) AS ByCurrentAccount, cambiado_en AS ChangedAt
              FROM dbo.comunicados_horario_centro_historial
             WHERE centro_id = @CenterId
             ORDER BY cambiado_en DESC
            """, new { AccountId = access.AccountId.Value, CenterId = access.CenterId.Value }, cancellationToken: ct)))
            .Select(h => new FamilyCommunicationScheduleChange(
                Settings(h.PreviousTime, h.PreviousMargin, h.PreviousSummary), Settings(h.NewTime, h.NewMargin, h.NewSummary), h.ByCurrentAccount,
                new DateTimeOffset(DateTime.SpecifyKind(h.ChangedAt, DateTimeKind.Utc))))
            .ToList();
        return new FamilyCommunicationScheduleView(settings, history, history.Count);
    }

    public async Task<int> SaveAsync(
        AccountAdministrationAccess access, int expectedVersion, FamilyCommunicationScheduleSettings desired, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);

        var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.comunicados_horario_centro_historial WITH (UPDLOCK, HOLDLOCK) WHERE centro_id = @CenterId
            """, new { CenterId = access.CenterId.Value }, transaction, cancellationToken: ct));
        if (version != expectedVersion)
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_SCHEDULE_REVISION_CONFLICT");
        }

        var current = await ReadEffectiveAsync(connection, transaction, access.CenterId, ct);
        if (current == desired)
        {
            return version;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var parameters = new
        {
            CenterId = access.CenterId.Value, PreviousTime = current.PublicationTime.ToTimeSpan(), PreviousMargin = current.MarginMinutes,
            NewTime = desired.PublicationTime.ToTimeSpan(), NewMargin = desired.MarginMinutes, AccountId = access.AccountId.Value,
            PreviousSummary = current.SummaryFrequency.ToCode(), NewSummary = desired.SummaryFrequency.ToCode(),
            OccurredAt = occurredAt, Id = Guid.NewGuid(), AuditId = Guid.NewGuid(),
        };
        // Sin fila vale el horario de CJ: volver a él borra la fila del centro.
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM dbo.comunicados_horario_centro WHERE centro_id = @CenterId;
            """ + (desired == FamilyCommunicationScheduleSettings.Default ? "" : """

            INSERT INTO dbo.comunicados_horario_centro (centro_id, hora_publicacion, margen_minutos, resumen_frecuencia_codigo)
            VALUES (@CenterId, @NewTime, @NewMargin, @NewSummary);
            """) + """

            INSERT INTO dbo.comunicados_horario_centro_historial
                (id, centro_id, hora_anterior, margen_anterior_minutos, resumen_anterior_codigo, hora_nueva, margen_nuevo_minutos, resumen_nuevo_codigo,
                 cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (@Id, @CenterId, @PreviousTime, @PreviousMargin, @PreviousSummary, @NewTime, @NewMargin, @NewSummary, @AccountId, 'ADMINISTRACION', @OccurredAt);

            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (@AuditId, @AccountId, 'ADMINISTRACION', @CenterId, 'FAMILY_COMMUNICATION_SCHEDULE', @CenterId, 'FAMILY_SCHEDULE_UPDATE', @OccurredAt);
            """, parameters, transaction, cancellationToken: ct));
        transaction.Commit();
        return version + 1;
    }

    public async Task<FamilyCommunicationScheduleSettings> GetEffectiveAsync(CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await ReadEffectiveAsync(connection, null, centerId, ct);
    }

    /// <summary>El horario de CJ con lo que el centro haya cambiado por encima. También lo usa el cierre de un evento, dentro de su transacción,
    /// para fijar la hora del comunicado.</summary>
    internal static async Task<FamilyCommunicationScheduleSettings> ReadEffectiveAsync(
        IDbConnection connection, IDbTransaction? transaction, CenterId centerId, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScheduleRow>(new CommandDefinition("""
            SELECT hora_publicacion AS PublicationTime, margen_minutos AS Margin, resumen_frecuencia_codigo AS Summary
              FROM dbo.comunicados_horario_centro WHERE centro_id = @CenterId
            """, new { CenterId = centerId.Value }, transaction, cancellationToken: ct));
        return row is null ? FamilyCommunicationScheduleSettings.Default : Settings(row.PublicationTime, row.Margin, row.Summary);
    }

    private static FamilyCommunicationScheduleSettings Settings(TimeSpan time, int marginMinutes, string summaryCode) =>
        new(TimeOnly.FromTimeSpan(time), TimeSpan.FromMinutes(marginMinutes), EnumCode.ParseCode<FamilyCommunicationSummaryFrequency>(summaryCode));

    private sealed record ScheduleRow(TimeSpan PublicationTime, int Margin, string Summary);

    private sealed record HistoryRow(
        TimeSpan PreviousTime, int PreviousMargin, string PreviousSummary, TimeSpan NewTime, int NewMargin, string NewSummary, bool ByCurrentAccount,
        DateTime ChangedAt);
}
