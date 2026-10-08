using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Corrección del texto de un comunicado a la familia durante su margen de 1 hora (script 0049; CJ, 2026-10-07). Una transacción bloquea el
/// comunicado, comprueba que es de un evento del ámbito de Enfermería de quien actúa (el mismo predicado que sus bandejas), que sigue dentro
/// del margen y sin publicar, y que la versión esperada es la vigente; añade la corrección y la audita sin datos.
/// </summary>
public sealed class SqlFamilyCommunicationCorrection(SqlConnectionFactory connections) : IFamilyCommunicationCorrector
{
    public async Task CorrectAsync(FamilyCommunicationCorrectionInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var parameters = new
        {
            ProfileScopeId = input.ProfileScopeId, CenterId = input.CenterId.Value, AccountId = input.AccountId.Value,
            CommunicationId = input.CommunicationId,
        };
        // El bloqueo ordena dos correcciones o una publicación simultáneas del mismo comunicado.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM dbo.comunicaciones_familiares WITH (UPDLOCK, ROWLOCK) WHERE id = @CommunicationId AND centro_id = @CenterId",
            parameters, transaction, cancellationToken: ct));
        var current = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition($"""
            SELECT family.preparado_en AS PreparedAt, ea.unidad_id AS UnitId, ea.residente_id AS ResidentId,
                   {FamilyCommunicationSql.CurrentType} AS TypeCode, {FamilyCommunicationSql.CurrentText} AS Text,
                   {FamilyCommunicationSql.Version} AS Version,
                   (SELECT early.publicada_en FROM dbo.comunicaciones_familiares_publicacion_anticipada early
                     WHERE early.comunicacion_id = family.id) AS PublishedEarlyAt
            {SqlChangeInboxDirectory.ScopedEventsFrom}
               AND profile.perfil_codigo = 'ENFERMERIA' AND profile.cuenta_id = @AccountId AND family.id = @CommunicationId
            """, parameters, transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();

        var occurredAt = DateTimeOffset.UtcNow;
        var preparedAt = new DateTimeOffset(DateTime.SpecifyKind(current.PreparedAt, DateTimeKind.Utc));
        DateTimeOffset? publishedEarlyAt = current.PublishedEarlyAt is { } early
            ? new DateTimeOffset(DateTime.SpecifyKind(early, DateTimeKind.Utc))
            : null;
        if (!FamilyCommunicationSchedule.CanCorrect(preparedAt, publishedEarlyAt, occurredAt))
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_CORRECTION_CLOSED");
        }

        if (current.Version != input.ExpectedVersion)
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_REVISION_CONFLICT");
        }

        if (string.Equals(current.TypeCode, input.Type.ToCode(), StringComparison.Ordinal) && string.Equals(current.Text, input.Text, StringComparison.Ordinal))
        {
            throw new DomainValidationException("FAMILY_COMMUNICATION_INVALID");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.comunicaciones_familiares_correcciones
                (id, comunicacion_id, centro_id, residente_id, numero, tipo_codigo, texto, corregida_por_cuenta_id, corregida_en)
            VALUES (NEWID(), @CommunicationId, @CenterId, @ResidentId, @Number, @TypeCode, @Text, @AccountId, @OccurredAt);

            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, 'ENFERMERIA', @CenterId, @UnitId, @ResidentId, 'FAMILY_COMMUNICATION', @CommunicationId,
                    'FAMILY_COMMUNICATION_CORRECT', @OccurredAt);
            """, new
        {
            input.CommunicationId, CenterId = input.CenterId.Value, current.ResidentId, Number = current.Version + 1,
            TypeCode = input.Type.ToCode(), input.Text, AccountId = input.AccountId.Value, current.UnitId, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        transaction.Commit();
    }

    private sealed record CurrentRow(DateTime PreparedAt, Guid UnitId, Guid ResidentId, string TypeCode, string Text, int Version, DateTime? PublishedEarlyAt);
}
