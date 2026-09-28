using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>ENF-10: Enfermería confirma la lectura de una indicación médica y después registra su resultado.
/// La concurrencia optimista vive en la revisión de la propia indicación (TR_im_guard la exige también en
/// BD); cada paso deja una fila en dbo.eventos_auditoria con el perfil ENFERMERIA.</summary>
public sealed class SqlMedicalIndicationRepository(SqlConnectionFactory connections) : IMedicalIndicationRepository
{
    public async Task<int> AcknowledgeAsync(AcknowledgeMedicalIndicationInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.indicaciones_medicas
               SET estado_codigo = 'LEIDA', revision = revision + 1, leida_por_cuenta_id = @AccountId, leida_en = @OccurredAt
             WHERE id = @IndicationId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'PENDIENTE_LECTURA'
            """, new
        {
            AccountId = input.AccountId.Value, OccurredAt = occurredAt, input.IndicationId, CenterId = input.CenterId.Value, input.ExpectedRevision,
        }, transaction, cancellationToken: ct));

        return await FinishAsync(connection, transaction, updated, input.AccountId, input.CenterId, input.IndicationId,
            "MEDICAL_INDICATION_READ", occurredAt, ct);
    }

    public async Task<int> ResolveAsync(ResolveMedicalIndicationInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var occurredAt = DateTimeOffset.UtcNow;

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.indicaciones_medicas
               SET estado_codigo = @StatusCode, revision = revision + 1, resuelta_por_cuenta_id = @AccountId, resuelta_en = @OccurredAt,
                   incidencia = @Incident
             WHERE id = @IndicationId AND centro_id = @CenterId AND revision = @ExpectedRevision AND estado_codigo = 'LEIDA'
            """, new
        {
            StatusCode = input.Outcome.Done ? "REALIZADA" : "NO_REALIZADA", AccountId = input.AccountId.Value, OccurredAt = occurredAt,
            input.Outcome.Incident, input.IndicationId, CenterId = input.CenterId.Value, input.ExpectedRevision,
        }, transaction, cancellationToken: ct));

        return await FinishAsync(connection, transaction, updated, input.AccountId, input.CenterId, input.IndicationId,
            input.Outcome.Done ? "MEDICAL_INDICATION_DONE" : "MEDICAL_INDICATION_NOT_DONE", occurredAt, ct);
    }

    private static async Task<int> FinishAsync(
        SqlConnection connection, SqlTransaction transaction, int updated, AccountId accountId, CenterId centerId, Guid indicationId,
        string actionCode, DateTimeOffset occurredAt, CancellationToken ct)
    {
        if (updated != 1)
        {
            throw new DomainValidationException("MEDICAL_INDICATION_REVISION_CONFLICT");
        }

        var row = await connection.QuerySingleAsync<IndicationRow>(new CommandDefinition("""
            SELECT evento_id AS EventId, revision AS Revision FROM dbo.indicaciones_medicas WHERE id = @IndicationId
            """, new { IndicationId = indicationId }, transaction, cancellationToken: ct));
        await ClinicalEventAudit.RecordAsync(connection, transaction, accountId, "ENFERMERIA", centerId,
            row.EventId, "MEDICAL_INDICATION", indicationId, actionCode, occurredAt, ct);
        transaction.Commit();
        return row.Revision;
    }

    private sealed record IndicationRow(Guid EventId, int Revision);
}
