using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Publicación de los comunicados a la familia por Administración (script 0048; CJ, 2026-10-07). Lee los comunicados de las unidades del
/// ámbito y publica uno antes de su hora dentro de una transacción que bloquea el comunicado, comprueba el ámbito y que no esté ya
/// publicado, y lo audita sin datos (FAMILY_COMMUNICATION_PUBLISH_NOW). La hora de publicación se calcula con la zona del servidor.
/// </summary>
public sealed class SqlFamilyCommunicationPublication(SqlConnectionFactory connections) : IFamilyCommunicationDirectory, IFamilyCommunicationPublisher
{
    public async Task<IReadOnlyList<AdministrationFamilyCommunication>> ListAsync(
        AccountAdministrationAccess access, DateTimeOffset since, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<CommunicationRow>(new CommandDefinition("""
            SELECT family.id AS Id, family.evento_id AS EventId, family.residente_id AS ResidentId, resident.nombre_visible AS ResidentName,
                   unit.nombre_visible AS UnitName, family.tipo_codigo AS TypeCode, family.texto AS Text, family.preparado_en AS PreparedAt,
                   early.publicada_en AS PublishedEarlyAt
              FROM dbo.comunicaciones_familiares family
              JOIN dbo.eventos_asistenciales ea ON ea.id = family.evento_id AND ea.centro_id = family.centro_id
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = family.centro_id
                   AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = ea.unidad_id AND unit.centro_id = family.centro_id
              JOIN dbo.residentes resident ON resident.id = family.residente_id AND resident.centro_id = family.centro_id
              LEFT JOIN dbo.comunicaciones_familiares_publicacion_anticipada early ON early.comunicacion_id = family.id
             WHERE family.centro_id = @CenterId AND family.preparado_en >= @Since
             ORDER BY family.preparado_en DESC
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, Since = since.UtcDateTime }, cancellationToken: ct));
        return rows.Select(row => new AdministrationFamilyCommunication(
            row.Id, row.EventId, ResidentId.From(row.ResidentId), row.ResidentName, row.UnitName,
            EnumCode.ParseCode<FamilyCommunicationType>(row.TypeCode), row.Text, Utc(row.PreparedAt),
            row.PublishedEarlyAt is { } early ? Utc(early) : null)).ToList();
    }

    public async Task PublishNowAsync(AccountAdministrationAccess access, Guid communicationId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        // El bloqueo ordena dos publicaciones simultáneas del mismo comunicado: la segunda ya lo ve publicado.
        var communication = await connection.QuerySingleOrDefaultAsync<PublishRow>(new CommandDefinition("""
            SELECT family.preparado_en AS PreparedAt, ea.unidad_id AS UnitId, family.residente_id AS ResidentId,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.comunicaciones_familiares_publicacion_anticipada early
                                           WHERE early.comunicacion_id = family.id) THEN 1 ELSE 0 END AS BIT) AS PublishedEarly
              FROM dbo.comunicaciones_familiares family WITH (UPDLOCK, ROWLOCK)
              JOIN dbo.eventos_asistenciales ea ON ea.id = family.evento_id AND ea.centro_id = family.centro_id
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = family.centro_id
                   AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
             WHERE family.id = @CommunicationId AND family.centro_id = @CenterId
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, CommunicationId = communicationId },
            transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();

        var occurredAt = DateTimeOffset.UtcNow;
        if (FamilyCommunicationSchedule.IsPublished(
                Utc(communication.PreparedAt), communication.PublishedEarly ? occurredAt : null, occurredAt, TimeZoneInfo.Local))
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_ALREADY_PUBLISHED");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.comunicaciones_familiares_publicacion_anticipada (id, comunicacion_id, centro_id, publicada_por_cuenta_id, publicada_en)
            VALUES (NEWID(), @CommunicationId, @CenterId, @AccountId, @OccurredAt);

            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, 'ADMINISTRACION', @CenterId, @UnitId, @ResidentId, 'FAMILY_COMMUNICATION', @CommunicationId,
                    'FAMILY_COMMUNICATION_PUBLISH_NOW', @OccurredAt);
            """, new
        {
            CommunicationId = communicationId, CenterId = access.CenterId.Value, AccountId = access.AccountId.Value, communication.UnitId,
            communication.ResidentId, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record CommunicationRow(
        Guid Id, Guid EventId, Guid ResidentId, string ResidentName, string? UnitName, string TypeCode, string Text, DateTime PreparedAt,
        DateTime? PublishedEarlyAt);

    private sealed record PublishRow(DateTime PreparedAt, Guid UnitId, Guid ResidentId, bool PublishedEarly);
}
