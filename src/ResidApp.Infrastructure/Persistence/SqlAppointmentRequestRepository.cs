using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using static ResidApp.Infrastructure.Persistence.AppointmentRequestSql;
using static ResidApp.Infrastructure.Persistence.AppointmentSql;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Solicitudes de cita para Administración del centro (script 0057). Cada operación comprueba en su transacción que quien actúa es Administración del
/// centro (<see cref="SqlProfessionalAccountRepository.EnsureAdministratorAsync"/>) y deja su auditoría. Proponer se ordena con el bloqueo del equipo (el
/// mismo de las reservas) y reserva el hueco: mientras la propuesta viva, <see cref="ReadTakenAsync"/> lo cuenta como ocupado. La cita nace cuando la
/// familia acepta, no aquí.
/// </summary>
public sealed class SqlAppointmentRequestRepository(SqlConnectionFactory connections) : IAppointmentRequestRepository
{
    public async Task<IReadOnlyList<AdministrationAppointmentRequest>> ListAsync(
        AccountAdministrationAccess access, bool onlyOpen, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        return await ReadAsync(connection, null, access.CenterId.Value, null, onlyOpen, now, ct);
    }

    public async Task<AdministrationAppointmentRequest> ReadAsync(
        AccountAdministrationAccess access, Guid requestId, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        return (await ReadAsync(connection, null, access.CenterId.Value, requestId, false, now, ct)).FirstOrDefault() ?? throw new AccessDeniedException();
    }

    public async Task<IReadOnlyList<AppointmentSlot>> ListProposalSlotsAsync(
        AccountAdministrationAccess access, Guid requestId, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var request = (await ReadAsync(connection, null, access.CenterId.Value, requestId, false, now, ct)).FirstOrDefault() ?? throw new AccessDeniedException();
        if (!CanPropose(request.Status, request.ProposalExpired))
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_NOT_OPEN");
        }

        var settings = (await ReadSettingsAsync(connection, null, access.CenterId.Value, ct)).Value with { MinimumNoticeHours = 0 };
        return (await FreeSlotsAsync(connection, null, access.CenterId.Value, request.Team, request.Modality, settings, now, null, ct))
            .Where(s => BornAlive(s, now)).ToList();
    }

    public async Task<int> ProposeAsync(
        AccountAdministrationAccess access, Guid requestId, DateTimeOffset startsAt, string? professional, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var center = access.CenterId.Value;
        var team = (await ReadUnlockedAsync(connection, transaction, center, requestId, ct) ?? throw new AccessDeniedException()).TeamValue;
        await LockTeamAsync(connection, transaction, center, team, ct);
        var request = await ReadForUpdateAsync(connection, transaction, center, requestId, null, null, ct) ?? throw new AccessDeniedException();
        if (!CanPropose(request.StatusValue, request.ProposalExpired(now)))
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_NOT_OPEN");
        }

        var settings = (await ReadSettingsAsync(connection, transaction, center, ct)).Value with { MinimumNoticeHours = 0 };
        var slot = (await FreeSlotsAsync(connection, transaction, center, team, request.ModalityValue, settings, now, null, ct))
            .FirstOrDefault(s => s.StartsAt.UtcDateTime == startsAt.UtcDateTime && BornAlive(s, now))
            ?? throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_solicitudes
               SET estado_codigo = 'PROPUESTA', propuesta_inicio_en = @startsAt, propuesta_fin_en = @endsAt, propuesta_en = @now, profesional_interno = @professional
             WHERE id = @requestId
            """, new { requestId, startsAt = slot.StartsAt.UtcDateTime, endsAt = slot.EndsAt.UtcDateTime, now = now.UtcDateTime, professional }, transaction, cancellationToken: ct));
        var changeNumber = await RecordChangeAsync(
            connection, transaction, center, requestId, request.ResidentId, access.AccountId.Value, SystemProfile.Administracion,
            AppointmentRequestAction.Propuesta, "APPOINTMENT_REQUEST_PROPOSE", slot.StartsAt, professional, now, ct);
        transaction.Commit();
        return changeNumber;
    }

    /// <summary>Se propone a una solicitud pendiente o a una cuya propuesta ya caducó (CJ, portal 6.4: después, Administración puede volver a proponer).</summary>
    private static bool CanPropose(AppointmentRequestStatus status, bool proposalExpired) =>
        status == AppointmentRequestStatus.Pendiente || (status == AppointmentRequestStatus.Propuesta && proposalExpired);

    /// <summary>Administración no tiene la antelación mínima de la familia, pero una propuesta de un hueco a 24 horas o menos nacería caducada (caduca 24
    /// horas antes de la cita): solo se ofrecen los que empiezan después (suposición mía, preguntada a CJ).</summary>
    private static bool BornAlive(AppointmentSlot slot, DateTimeOffset now) => AppointmentRequestRules.IsProposalAlive(now, slot.StartsAt, now);

    public async Task CloseAsync(AccountAdministrationAccess access, Guid requestId, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var center = access.CenterId.Value;
        var request = await ReadForUpdateAsync(connection, transaction, center, requestId, null, null, ct) ?? throw new AccessDeniedException();
        if (request.StatusValue is not (AppointmentRequestStatus.Pendiente or AppointmentRequestStatus.Propuesta))
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_NOT_OPEN");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_solicitudes SET estado_codigo = 'CERRADA', propuesta_inicio_en = NULL, propuesta_fin_en = NULL, propuesta_en = NULL, profesional_interno = NULL
             WHERE id = @requestId
            """, new { requestId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, center, requestId, request.ResidentId, access.AccountId.Value, SystemProfile.Administracion,
            AppointmentRequestAction.Cerrada, "APPOINTMENT_REQUEST_CLOSE", null, reason, now, ct);
        transaction.Commit();
    }

    /// <summary>Una solicitud del centro sin bloquear (para saber el equipo antes de tomar el bloqueo del equipo).</summary>
    private static Task<RequestRow?> ReadUnlockedAsync(IDbConnection connection, IDbTransaction transaction, Guid centerId, Guid requestId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition($"""
            SELECT {Columns} FROM dbo.citas_solicitudes r WITH (FORCESEEK) WHERE r.id = @requestId AND r.centro_id = @centerId
            """, new { requestId, centerId }, transaction, cancellationToken: ct));

    private sealed record AdministrationRow(
        Guid Id, Guid ResidentId, Guid LinkId, Guid AccountId, string Team, string Modality, string Reason, string? Comment, string Days, string DayPart,
        string Status, DateTime? ProposedStart, DateTime? ProposedEnd, DateTime? ProposedAt, string? Professional, Guid? AppointmentId, DateTime CreatedAt,
        string ResidentName, string FamilyName, string Relationship);

    /// <summary>Las solicitudes del centro (una sola si se indica), las más antiguas primero; con <paramref name="onlyOpen"/>, solo las abiertas.</summary>
    private static async Task<IReadOnlyList<AdministrationAppointmentRequest>> ReadAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, Guid? requestId, bool onlyOpen, DateTimeOffset now, CancellationToken ct)
    {
        var rows = (await connection.QueryAsync<AdministrationRow>(new CommandDefinition($"""
            SELECT {Columns}, resident.nombre_visible AS ResidentName, family.nombre_visible AS FamilyName, link.relacion AS Relationship
              FROM dbo.citas_solicitudes r
              JOIN dbo.residentes resident ON resident.id = r.residente_id AND resident.centro_id = r.centro_id
              JOIN dbo.residentes_familiares link ON link.id = r.vinculo_id AND link.centro_id = r.centro_id
              JOIN dbo.familiares family ON family.id = link.familiar_id AND family.centro_id = link.centro_id
             WHERE r.centro_id = @centerId AND (@requestId IS NULL OR r.id = @requestId)
               AND (@onlyOpen = 0 OR r.estado_codigo IN ('PENDIENTE', 'PROPUESTA'))
             ORDER BY r.creada_en, resident.nombre_visible
            """, new { centerId, requestId, onlyOpen }, transaction, cancellationToken: ct))).ToList();
        var changes = await ReadChangesAsync(connection, transaction, centerId, rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(row =>
        {
            var request = new RequestRow(
                row.Id, row.ResidentId, row.LinkId, row.AccountId, row.Team, row.Modality, row.Reason, row.Comment, row.Days, row.DayPart, row.Status,
                row.ProposedStart, row.ProposedEnd, row.ProposedAt, row.Professional, row.AppointmentId, row.CreatedAt);
            return new AdministrationAppointmentRequest(
                row.Id, row.ResidentId, row.ResidentName, row.FamilyName, row.Relationship, request.TeamValue, request.ModalityValue,
                EnumCode.ParseCode<AppointmentRequestReason>(row.Reason), row.Comment, request.Availability, request.StatusValue, Utc(row.CreatedAt),
                request.ProposedStartsAt, request.ProposedEndsAt, request.ProposalExpiresAt, row.Professional, request.ProposalExpired(now), row.AppointmentId,
                changes[row.Id].Select(c => new AppointmentRequestChange(
                    EnumCode.ParseCode<AppointmentRequestAction>(c.Action), EnumCode.ParseCode<SystemProfile>(c.Profile), Utc(c.At),
                    c.ProposedStart is { } start ? Utc(start) : null, c.Detail, c.Cause == "BAJA_RESIDENTE")).ToList());
        }).ToList();
    }
}
