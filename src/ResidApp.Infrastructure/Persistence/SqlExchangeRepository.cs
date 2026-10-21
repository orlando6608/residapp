using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADR 0010: escrituras de la importación y la exportación. Cada una va en una transacción que repite el ámbito de Administración de quien
/// gestiona (EnsureAdministratorAsync) y deja su evento en dbo.eventos_auditoria, sin datos ni recuentos: EXCHANGE_TEMPLATE_SAVE al guardar
/// una plantilla, IMPORT_RUN al terminar una importación y EXPORT_RUN antes de entregar una exportación. Lo que crea una importación lo
/// auditan, fila a fila, los casos de uso de siempre.
/// </summary>
public sealed class SqlExchangeRepository(SqlConnectionFactory connections) : IExchangeRepository
{
    public async Task<Guid> SaveTemplateAsync(
        AccountAdministrationAccess access, ExchangeDirection direction, ExchangeEntity entity, string name, IReadOnlyList<string> headers,
        ExchangeMapping mapping, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var key = new { CenterId = access.CenterId.Value, Direction = direction.ToCode(), Entity = entity.ToCode(), Name = name };
        // UPDLOCK + HOLDLOCK: dos guardados simultáneos con el mismo nombre se ordenan y el segundo sustituye al primero.
        var existing = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM dbo.plantillas_intercambio WITH (UPDLOCK, HOLDLOCK)
             WHERE centro_id = @CenterId AND direccion = @Direction AND entidad = @Entity AND nombre = @Name
            """, key, transaction, cancellationToken: ct));
        var templateId = existing ?? Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition(existing is null
            ? """
              INSERT INTO dbo.plantillas_intercambio
                  (id, centro_id, direccion, entidad, nombre, firma_cabeceras, mapeo_json, guardada_por_cuenta_id, guardada_en)
              VALUES (@TemplateId, @CenterId, @Direction, @Entity, @Name, @Signature, @MappingJson, @ActorId, @OccurredAt)
              """
            : """
              UPDATE dbo.plantillas_intercambio
                 SET firma_cabeceras = @Signature, mapeo_json = @MappingJson, guardada_por_cuenta_id = @ActorId, guardada_en = @OccurredAt
               WHERE id = @TemplateId
              """, new
        {
            TemplateId = templateId, key.CenterId, key.Direction, key.Entity, key.Name, Signature = ExchangeCatalog.Signature(headers),
            MappingJson = JsonSerializer.Serialize(StoredExchangeMapping.From(headers, mapping)), ActorId = access.AccountId.Value,
            OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        await SqlProfessionalAccountRepository.AuditAsync(
            connection, transaction, access, "EXCHANGE_TEMPLATE", templateId, "EXCHANGE_TEMPLATE_SAVE", occurredAt, ct: ct);
        transaction.Commit();
        return templateId;
    }

    public async Task RecordRunAsync(AccountAdministrationAccess access, ExchangeDirection direction, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var (resourceType, action) = direction == ExchangeDirection.Importar ? ("DATA_IMPORT", "IMPORT_RUN") : ("DATA_EXPORT", "EXPORT_RUN");
        await SqlProfessionalAccountRepository.AuditAsync(connection, transaction, access, resourceType, Guid.NewGuid(), action, DateTimeOffset.UtcNow, ct: ct);
        transaction.Commit();
    }
}
