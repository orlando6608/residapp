using Dapper;
using ResidApp.Application.Ports;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>COR-01/COR-02 (script 0020): autoría, correcciones y rectificaciones de una valoración, para el
/// detalle del evento.</summary>
public sealed partial class SqlChangeInboxDirectory
{
    private static async Task<AssessmentAmendments?> FindAmendmentsAsync(
        System.Data.IDbConnection connection, Guid profileScopeId, Guid eventId, bool medical, CancellationToken ct)
    {
        var (versions, corrections, profileCode) = medical
            ? ("dbo.valoraciones_medicas_versiones", "dbo.valoraciones_medicas_correcciones", "MEDICINA")
            : ("dbo.valoraciones_enfermeria_versiones", "dbo.valoraciones_enfermeria_correcciones", "ENFERMERIA");
        var parameters = new { ProfileScopeId = profileScopeId, EventId = eventId, ProfileCode = profileCode };

        var last = await connection.QuerySingleOrDefaultAsync<LastSaveRow>(new CommandDefinition($"""
            SELECT TOP 1 CAST(CASE WHEN x.guardado_por_cuenta_id = profile.cuenta_id THEN 1 ELSE 0 END AS BIT) AS AuthoredByCurrentAccount,
                   x.guardado_en AS SavedAt
              FROM {versions} x
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId
             WHERE x.evento_id = @EventId
             ORDER BY x.revision_evento DESC
            """, parameters, cancellationToken: ct));
        if (last is null)
        {
            return null;
        }

        var correctionRows = await connection.QueryAsync<CorrectionRow>(new CommandDefinition($"""
            SELECT c.motivo AS Reason, c.corregido_en AS CorrectedAt
              FROM {corrections} c
             WHERE c.evento_id = @EventId
             ORDER BY c.corregido_en ASC
            """, parameters, cancellationToken: ct));
        var rectificationRows = await connection.QueryAsync<RectificationRow>(new CommandDefinition("""
            SELECT r.texto AS [Text], r.motivo AS Reason, r.registrado_en AS RecordedAt
              FROM dbo.valoraciones_rectificaciones r
             WHERE r.evento_id = @EventId AND r.perfil_codigo = @ProfileCode
             ORDER BY r.registrado_en ASC
            """, parameters, cancellationToken: ct));

        return new AssessmentAmendments(
            last.AuthoredByCurrentAccount, new DateTimeOffset(last.SavedAt, TimeSpan.Zero),
            correctionRows.Select(c => new AssessmentCorrectionSummary(c.Reason, new DateTimeOffset(c.CorrectedAt, TimeSpan.Zero))).ToList(),
            rectificationRows.Select(r => new AssessmentRectificationSummary(r.Text, r.Reason, new DateTimeOffset(r.RecordedAt, TimeSpan.Zero))).ToList());
    }

    private sealed record LastSaveRow(bool AuthoredByCurrentAccount, DateTime SavedAt);

    private sealed record CorrectionRow(string Reason, DateTime CorrectedAt);

    private sealed record RectificationRow(string Text, string Reason, DateTime RecordedAt);
}
