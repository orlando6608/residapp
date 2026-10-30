using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El registro de envíos del aviso de propuesta de cita (script 0081). Al reservar vuelve a leer la solicitud y el vínculo con la misma consulta que usa el portal
/// (<see cref="SqlFamilyPortalReader.AccessibleLinks"/>, no una copia): si la propuesta ya no vale o la familia ya no puede verla, anota OMITIDO y no se envía. El
/// destinatario es solo el familiar de esa solicitud, con su correo actual. La fila no guarda la dirección: se devuelve para enviar y no se escribe en ningún sitio.
/// </summary>
public sealed class SqlProposalNoticeStore(SqlConnectionFactory connections) : IProposalNoticeStore
{
    private const int UniqueViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<NoticeReservation?> ReserveAsync(
        CenterId centerId, Guid requestId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var request = await connection.QuerySingleOrDefaultAsync<NoticeRequestRow>(new CommandDefinition("""
            SELECT s.estado_codigo AS Status, s.cuenta_id AS AccountId, s.vinculo_id AS LinkId, s.propuesta_inicio_en AS ProposedStart, s.propuesta_en AS ProposedAt,
                   (SELECT TOP (1) c.numero FROM dbo.citas_solicitudes_cambios c WHERE c.solicitud_id = s.id ORDER BY c.numero DESC) AS LastNumber,
                   (SELECT TOP (1) c.accion_codigo FROM dbo.citas_solicitudes_cambios c WHERE c.solicitud_id = s.id ORDER BY c.numero DESC) AS LastAction
              FROM dbo.citas_solicitudes s
             WHERE s.id = @requestId AND s.centro_id = @center
            """, new { requestId, center = centerId.Value }, cancellationToken: ct));
        if (request is null)
        {
            return null;
        }

        var familyId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @LinkId AND centro_id = @center",
            new { request.LinkId, center = centerId.Value }, cancellationToken: ct));
        if (familyId is null)
        {
            return null;
        }

        string? address = null;
        string? reason;
        if (!request.IsCurrent(changeNumber, now))
        {
            reason = EmailLog.ProposalNotCurrent;
        }
        else
        {
            var recipient = await connection.QuerySingleOrDefaultAsync<Recipient>(new CommandDefinition(
                $"SELECT fam.correo AS Address {SqlFamilyPortalReader.AccessibleLinks} AND link.id = @LinkId",
                new
                {
                    AccountId = request.AccountId, CenterId = centerId.Value, Today = DateOnly.FromDateTime(now.LocalDateTime).ToDateTime(TimeOnly.MinValue), request.LinkId,
                }, cancellationToken: ct));
            if (recipient is null)
            {
                reason = EmailLog.NoAccess;
            }
            else if (string.IsNullOrWhiteSpace(recipient.Address))
            {
                reason = EmailLog.NoAddress;
            }
            else
            {
                reason = null;
                address = recipient.Address.Trim();
            }
        }

        var id = Guid.NewGuid();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.envios_correo
                    (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, intentos, creado_en, resuelto_en)
                VALUES (@id, @center, @template, @version, @language, @familyId, @requestId, @key, @state, @reason, 0, @now, @resolved)
                """, new
            {
                id, center = centerId.Value, template = EmailLog.ProposalTemplate, version = templateVersion, language, familyId, requestId,
                key = changeNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                state = reason is null ? EmailLog.Pending : EmailLog.Omitted, reason, now = now.UtcDateTime, resolved = reason is null ? (DateTime?)null : now.UtcDateTime,
            }, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is UniqueViolation or UniqueConstraintViolation)
        {
            return null; // otro envío ya reservó este aviso: se manda una sola vez
        }

        return new NoticeReservation(id, address, language);
    }

    public async Task ResolveAsync(
        Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.envios_correo
               SET estado_codigo = @state, motivo_codigo = @reason, acs_operacion_id = @operationId, intentos = @attempts, resuelto_en = @now
             WHERE id = @id AND estado_codigo = 'PENDIENTE'
            """, new { id, state, reason, operationId, attempts, now = now.UtcDateTime }, cancellationToken: ct));
    }

    private sealed record Recipient(string? Address);

    private sealed record NoticeRequestRow(
        string Status, Guid AccountId, Guid LinkId, DateTime? ProposedStart, DateTime? ProposedAt, int? LastNumber, string? LastAction)
    {
        /// <summary>La propuesta sigue siendo la que se guardó (el último cambio es ese PROPUESTA, no otro posterior) y no ha caducado.</summary>
        internal bool IsCurrent(int changeNumber, DateTimeOffset now) =>
            Status == AppointmentRequestStatus.Propuesta.ToCode() && LastAction == AppointmentRequestAction.Propuesta.ToCode() && LastNumber == changeNumber
            && ProposedStart is { } start && ProposedAt is { } at
            && AppointmentRequestRules.IsProposalAlive(AppointmentSql.Utc(at), AppointmentSql.Utc(start), now);
    }
}
