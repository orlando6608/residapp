using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>DER-05: descarga del PDF firmado con el mismo predicado de ámbito que el detalle del evento
/// (SqlChangeInboxDirectory.ScopedEventsFrom: con un ámbito de Medicina, solo eventos escalados). La lectura y
/// su fila de auditoría REFERRAL_REPORT_DOWNLOAD van en la misma transacción; no cambia la revisión del
/// evento.</summary>
public sealed class SqlReferralReportRepository(SqlConnectionFactory connections) : IReferralReportRepository
{
    public async Task<ReferralReportPdf?> DownloadAsync(
        AccountId accountId, SystemProfile profile, Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);

        var row = await connection.QuerySingleOrDefaultAsync<PdfRow>(new CommandDefinition($"""
            SELECT d.id AS Id, d.pdf AS Content, d.firmado_en AS SignedAt
              FROM dbo.informes_derivacion d
             WHERE d.evento_id = @EventId
               AND EXISTS (SELECT 1
                   {SqlChangeInboxDirectory.ScopedEventsFrom}
                      AND ea.id = @EventId)
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, EventId = eventId }, transaction, cancellationToken: ct));
        if (row is null)
        {
            transaction.Rollback();
            return null;
        }

        await ClinicalEventAudit.RecordAsync(connection, transaction, accountId, profile.ToCode(), centerId,
            eventId, "REFERRAL_REPORT", row.Id, "REFERRAL_REPORT_DOWNLOAD", DateTimeOffset.UtcNow, ct);
        transaction.Commit();
        return new ReferralReportPdf(row.Content, new DateTimeOffset(row.SignedAt, TimeSpan.Zero));
    }

    public async Task<ReferralReportPdf?> DownloadAsDirectionAsync(DirectionReferralDownloadInput input, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);

        // Todo en una sola consulta: cuenta activa, ámbito de Dirección vigente del centro, permiso clínico, evento del residente en una unidad concedida
        // (y residente visible si el ámbito los restringe) y declaración vigente de esta cuenta, ámbito y residente. La finalidad y la justificación que
        // se auditan son las de la declaración guardada, no las que diga quien llama.
        var row = await connection.QuerySingleOrDefaultAsync<DirectionPdfRow>(new CommandDefinition("""
            SELECT r.id AS Id, r.pdf AS Content, r.firmado_en AS SignedAt, ea.unidad_id AS UnitId,
                   declaration.proposito_codigo AS Purpose, declaration.justificacion AS Justification
              FROM dbo.informes_derivacion r
              JOIN dbo.eventos_asistenciales ea ON ea.id = r.evento_id AND ea.centro_id = r.centro_id AND ea.residente_id = @ResidentId
              JOIN dbo.cuentas account ON account.id = @AccountId AND account.estado = 'ACTIVE'
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.cuenta_id = account.id AND profile.centro_id = @CenterId
                   AND profile.perfil_codigo = 'DIRECCION_CLINICA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id AND unit_scope.centro_id = profile.centro_id
                   AND unit_scope.unidad_id = ea.unidad_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.permisos_perfil permission ON permission.ambito_perfil_id = profile.id AND permission.centro_id = profile.centro_id
                   AND permission.permiso_codigo = 'CLINICAL_DETAIL_READ' AND permission.revocado_en IS NULL
              JOIN dbo.residentes resident ON resident.id = ea.residente_id AND resident.centro_id = profile.centro_id AND resident.estado = 'ACTIVE'
              JOIN dbo.declaraciones_acceso_clinico declaration ON declaration.id = @DeclarationId AND declaration.cuenta_id = account.id
                   AND declaration.ambito_perfil_id = profile.id AND declaration.residente_id = resident.id
                   AND declaration.terminada_en IS NULL AND declaration.caduca_en > SYSUTCDATETIME()
              LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
                   AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = resident.id AND resident_scope.revocado_en IS NULL
             WHERE r.evento_id = @EventId AND r.centro_id = @CenterId
               AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
                   SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                    WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
            """, new
        {
            AccountId = input.AccountId.Value, ProfileScopeId = input.ProfileScopeId, CenterId = input.CenterId.Value,
            ResidentId = input.ResidentId.Value, EventId = input.EventId, DeclarationId = input.DeclarationId,
        }, transaction, cancellationToken: ct));
        if (row is null)
        {
            transaction.Rollback();
            return null;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, proposito_codigo,
                 justificacion, ocurrido_en)
            VALUES (NEWID(), @AccountId, 'DIRECCION_CLINICA', @CenterId, @UnitId, @ResidentId, 'REFERRAL_REPORT', @ReportId,
                    'CLINICAL_DETAIL_READ', @Purpose, @Justification, @OccurredAt)
            """, new
        {
            AccountId = input.AccountId.Value, CenterId = input.CenterId.Value, UnitId = row.UnitId, ResidentId = input.ResidentId.Value,
            ReportId = row.Id, row.Purpose, row.Justification, OccurredAt = occurredAt,
        }, transaction, cancellationToken: ct));
        transaction.Commit();
        return new ReferralReportPdf(row.Content, new DateTimeOffset(row.SignedAt, TimeSpan.Zero));
    }

    private sealed record PdfRow(Guid Id, byte[] Content, DateTime SignedAt);

    private sealed record DirectionPdfRow(Guid Id, byte[] Content, DateTime SignedAt, Guid UnitId, string Purpose, string Justification);
}
