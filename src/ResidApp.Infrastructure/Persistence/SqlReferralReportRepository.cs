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

    private sealed record PdfRow(Guid Id, byte[] Content, DateTime SignedAt);
}
