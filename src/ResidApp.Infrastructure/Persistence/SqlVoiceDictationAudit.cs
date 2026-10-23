using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Deja en dbo.eventos_auditoria (de solo inserción) que se usó el dictado de «Registrar cambio» (ADR 0009 y 0011). Sin el audio, el texto ni
/// nada del contenido: solo quién, en qué unidad, sobre qué residente y cuándo. El identificador de la operación hace de recurso.</summary>
public sealed class SqlVoiceDictationAudit(SqlConnectionFactory connections) : IVoiceDictationAudit
{
    public const string ActionCode = "VOICE_DICTATION_USED";
    public const string ResourceType = "VOICE_DICTATION";

    public async Task RecordAsync(VoiceDictationAuditInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (@Id, @AccountId, 'AUXILIAR', @CenterId, @UnitId, @ResidentId, @ResourceType, @OperationId, @ActionCode, @OccurredAt)
            """, new
        {
            Id = Guid.NewGuid(), AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, UnitId = input.UnitId.Value,
            ResidentId = input.ResidentId.Value, ResourceType, input.OperationId, ActionCode, OccurredAt = DateTimeOffset.UtcNow,
        }, cancellationToken: ct));
    }
}
