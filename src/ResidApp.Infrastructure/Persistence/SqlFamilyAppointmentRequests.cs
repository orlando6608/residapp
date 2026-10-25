using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using static ResidApp.Infrastructure.Persistence.AppointmentRequestSql;
using static ResidApp.Infrastructure.Persistence.AppointmentSql;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Solicitudes de cita del Portal Familiar (script 0057). El acceso a cada residente es el criterio único del portal,
/// <see cref="SqlFamilyPortalReader.AccessibleLinks"/> (cuenta del familiar, vínculo no desvinculado y autorización Activa y vigente), y toda operación
/// empieza por él: sin acceso no hay datos ni rastro. Cada solicitud la ve y la cambia solo la cuenta que la envió. Enviar y aceptar se ordenan con el
/// bloqueo del equipo (el mismo de las reservas) y se comprueban en la misma transacción que escribe; aceptar crea la cita en dbo.citas y el índice
/// único del hueco es la última barrera. Repetir la operación de un envío (doble pulsación) o aceptar dos veces devuelve el mismo resultado.
/// La familia nunca recibe el profesional ni nada interno.
/// </summary>
public sealed class SqlFamilyAppointmentRequests(SqlConnectionFactory connections) : IFamilyAppointmentRequests
{
    /// <summary>La familia ve las solicitudes de los últimos 90 días y todas las abiertas.</summary>
    private static readonly TimeSpan History = TimeSpan.FromDays(90);

    public async Task<IReadOnlyList<FamilyAppointmentRequest>?> ListAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, null, access, linkId, today, ct);
        return link is null ? null : await ReadAsync(connection, null, access, link.ResidentId, null, now, ct);
    }

    public async Task<FamilyAppointmentRequest?> ReadAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, null, access, linkId, today, ct);
        return link is null ? null : (await ReadAsync(connection, null, access, link.ResidentId, requestId, now, ct)).FirstOrDefault();
    }

    public async Task<FamilyAppointmentRequest?> CreateAsync(
        FamilyPortalAccess access, Guid linkId, AppointmentRequestInput input, Guid operationId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var center = access.CenterId.Value;
        await LockTeamAsync(connection, transaction, center, input.Team, ct);
        var settings = (await ReadSettingsAsync(connection, transaction, center, ct)).Value;

        // La misma operación de la misma cuenta es la doble pulsación: devuelve la solicitud ya creada, si es la misma petición.
        var previous = await connection.QuerySingleOrDefaultAsync<PreviousRow>(new CommandDefinition("""
            SELECT id AS Id, vinculo_id AS LinkId, equipo_codigo AS Team, modalidad_codigo AS Modality, motivo_codigo AS Reason
              FROM dbo.citas_solicitudes WITH (FORCESEEK) WHERE cuenta_id = @account AND operacion_id = @operationId
            """, new { account = access.AccountId.Value, operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            if (previous.LinkId != link.LinkId || previous.Team != input.Team.ToCode() || previous.Modality != input.Modality.ToCode()
                || previous.Reason != input.Reason.ToCode())
            {
                throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
            }

            return (await ReadAsync(connection, transaction, access, link.ResidentId, previous.Id, now, ct)).First();
        }

        if (!settings.RequestsEnabled)
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_DISABLED");
        }

        if (!(await ReadModalitiesAsync(connection, transaction, center, ct)).Contains(new AppointmentModalitySetting(input.Team, input.Modality)))
        {
            throw new DomainValidationException("APPOINTMENT_MODALITY_UNAVAILABLE");
        }

        await EnsureNoFutureAppointmentAsync(connection, transaction, center, link.ResidentId, input.Team, now, ct);
        var open = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.citas_solicitudes WITH (FORCESEEK)
             WHERE centro_id = @center AND residente_id = @resident AND equipo_codigo = @team AND estado_codigo IN ('PENDIENTE', 'PROPUESTA')
            """, new { center, resident = link.ResidentId, team = input.Team.ToCode() }, transaction, cancellationToken: ct));
        if (open > 0)
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_LIMIT_REACHED");
        }

        var id = Guid.NewGuid();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas_solicitudes
                    (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, motivo_codigo, comentario, disponibilidad_dias,
                     disponibilidad_franja, estado_codigo, operacion_id, creada_en)
                VALUES (@id, @center, @resident, @link, @account, @team, @modality, @reason, @comment, @days, @dayPart, 'PENDIENTE', @operationId, @now)
                """, new
            {
                id, center, resident = link.ResidentId, link = link.LinkId, account = access.AccountId.Value, team = input.Team.ToCode(),
                modality = input.Modality.ToCode(), reason = input.Reason.ToCode(), comment = input.Comment, days = input.Availability.DaysCode,
                dayPart = input.Availability.DayPart.ToCode(), operationId, now = now.UtcDateTime,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (IsUniqueViolation(error, OpenIndex))
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_LIMIT_REACHED");
        }

        await RecordChangeAsync(
            connection, transaction, center, id, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentRequestAction.Creada, "APPOINTMENT_REQUEST_CREATE", null, null, now, ct);
        var created = (await ReadAsync(connection, transaction, access, link.ResidentId, id, now, ct)).First();
        transaction.Commit();
        return created;
    }

    public async Task<FamilyAppointmentRequest?> AcceptAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var center = access.CenterId.Value;
        var team = (await ReadOwnAsync(connection, transaction, access, link.ResidentId, requestId, ct))?.TeamValue;
        if (team is null)
        {
            return null;
        }

        await LockTeamAsync(connection, transaction, center, team.Value, ct);
        var request = await ReadForUpdateAsync(connection, transaction, center, requestId, access.AccountId.Value, link.ResidentId, ct);
        if (request is null)
        {
            return null;
        }

        if (request.StatusValue == AppointmentRequestStatus.Aceptada)
        {
            return (await ReadAsync(connection, transaction, access, link.ResidentId, requestId, now, ct)).First();
        }

        if (request.StatusValue != AppointmentRequestStatus.Propuesta)
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_NOT_OPEN");
        }

        var startsAt = request.ProposedStartsAt!.Value;
        var endsAt = request.ProposedEndsAt!.Value;
        if (request.ProposalExpired(now))
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_EXPIRED");
        }

        var slot = new AppointmentSlot(startsAt, endsAt);
        var taken = await ReadTakenAsync(connection, transaction, center, request.TeamValue, now, null, ct, exceptRequestId: requestId);
        if (taken.Any(o => AppointmentSlots.Overlaps(slot, o.StartsAt, o.EndsAt)))
        {
            throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        }

        await EnsureNoFutureAppointmentAsync(connection, transaction, center, link.ResidentId, request.TeamValue, now, ct);
        var appointmentId = Guid.NewGuid();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas
                    (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
                VALUES (@appointmentId, @center, @resident, @link, @account, @team, @modality, @startsAt, @endsAt, 'CONFIRMADA', @operationId, @now)
                """, new
            {
                appointmentId, center, resident = link.ResidentId, link = link.LinkId, account = access.AccountId.Value, team = request.Team,
                modality = request.Modality, startsAt = startsAt.UtcDateTime, endsAt = endsAt.UtcDateTime, operationId = requestId, now = now.UtcDateTime,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (IsUniqueViolation(error, SlotIndex))
        {
            throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        }

        await AppointmentSql.RecordChangeAsync(
            connection, transaction, center, appointmentId, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentChangeAction.Creada, null, null, startsAt, null, now, ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.citas_solicitudes SET estado_codigo = 'ACEPTADA', cita_id = @appointmentId WHERE id = @requestId",
            new { appointmentId, requestId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, center, requestId, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentRequestAction.Aceptada, "APPOINTMENT_REQUEST_ACCEPT", startsAt, null, now, ct);
        var accepted = (await ReadAsync(connection, transaction, access, link.ResidentId, requestId, now, ct)).First();
        transaction.Commit();
        return accepted;
    }

    public async Task<FamilyAppointmentRequest?> RequestAnotherTimeAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var request = await ReadForUpdateAsync(connection, transaction, access.CenterId.Value, requestId, access.AccountId.Value, link.ResidentId, ct);
        if (request is null)
        {
            return null;
        }

        if (request.StatusValue != AppointmentRequestStatus.Propuesta)
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_NOT_OPEN");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_solicitudes SET estado_codigo = 'PENDIENTE', propuesta_inicio_en = NULL, propuesta_fin_en = NULL, propuesta_en = NULL, profesional_interno = NULL
             WHERE id = @requestId
            """, new { requestId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, access.CenterId.Value, requestId, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentRequestAction.OtroHorario, "APPOINTMENT_REQUEST_OTHER_TIME", request.ProposedStartsAt, null, now, ct);
        var changed = (await ReadAsync(connection, transaction, access, link.ResidentId, requestId, now, ct)).First();
        transaction.Commit();
        return changed;
    }

    public async Task<bool> CancelAsync(
        FamilyPortalAccess access, Guid linkId, Guid requestId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return false;
        }

        var request = await ReadForUpdateAsync(connection, transaction, access.CenterId.Value, requestId, access.AccountId.Value, link.ResidentId, ct);
        if (request is null)
        {
            return false;
        }

        if (request.StatusValue is not (AppointmentRequestStatus.Pendiente or AppointmentRequestStatus.Propuesta))
        {
            throw new DomainValidationException("APPOINTMENT_REQUEST_NOT_OPEN");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_solicitudes SET estado_codigo = 'CANCELADA', propuesta_inicio_en = NULL, propuesta_fin_en = NULL, propuesta_en = NULL, profesional_interno = NULL
             WHERE id = @requestId
            """, new { requestId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, access.CenterId.Value, requestId, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentRequestAction.Cancelada, "APPOINTMENT_REQUEST_CANCEL", null, null, now, ct);
        transaction.Commit();
        return true;
    }

    /// <summary>La misma regla que la reserva directa: una sola cita futura confirmada por residente y equipo.</summary>
    private static async Task EnsureNoFutureAppointmentAsync(
        IDbConnection connection, IDbTransaction transaction, Guid center, Guid residentId, AppointmentTeam team, DateTimeOffset now, CancellationToken ct)
    {
        var openForTeam = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.citas WITH (FORCESEEK)
             WHERE centro_id = @center AND residente_id = @resident AND equipo_codigo = @team AND estado_codigo = 'CONFIRMADA' AND inicio_en > @now
            """, new { center, resident = residentId, team = team.ToCode(), now = now.UtcDateTime }, transaction, cancellationToken: ct));
        if (openForTeam > 0)
        {
            throw new DomainValidationException("APPOINTMENT_LIMIT_REACHED");
        }
    }

    /// <summary>Una solicitud de esta cuenta sobre este residente, sin bloquear (para saber el equipo antes de tomar el bloqueo del equipo).</summary>
    private static Task<RequestRow?> ReadOwnAsync(
        IDbConnection connection, IDbTransaction transaction, FamilyPortalAccess access, Guid residentId, Guid requestId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition($"""
            SELECT {Columns} FROM dbo.citas_solicitudes r WITH (FORCESEEK)
             WHERE r.id = @requestId AND r.centro_id = @center AND r.residente_id = @residentId AND r.cuenta_id = @account
            """, new { requestId, center = access.CenterId.Value, residentId, account = access.AccountId.Value }, transaction, cancellationToken: ct));

    /// <summary>Las solicitudes de la cuenta sobre el residente (una sola si se indica), las abiertas primero y después las demás, de la más reciente a la más antigua.</summary>
    private static async Task<IReadOnlyList<FamilyAppointmentRequest>> ReadAsync(
        IDbConnection connection, IDbTransaction? transaction, FamilyPortalAccess access, Guid residentId, Guid? requestId, DateTimeOffset now, CancellationToken ct)
    {
        // Una solicitud se busca por su clave y la lista por centro y residente, las dos con FORCESEEK: con un OR sobre @requestId el plan recorría la tabla
        // entera y, dentro de una transacción, pedía bloqueos sobre filas de otras familias (interbloqueos en una BD recién creada; lección de 0055 y 0056).
        var filter = requestId is null ? "(r.creada_en >= @since OR r.estado_codigo IN ('PENDIENTE', 'PROPUESTA'))" : "r.id = @requestId";
        var rows = (await connection.QueryAsync<RequestRow>(new CommandDefinition($"""
            SELECT {Columns} FROM dbo.citas_solicitudes r WITH (FORCESEEK)
             WHERE r.centro_id = @center AND r.residente_id = @residentId AND r.cuenta_id = @account AND {filter}
            """, new
        {
            center = access.CenterId.Value, residentId, account = access.AccountId.Value, requestId, since = (now - History).UtcDateTime,
        }, transaction, cancellationToken: ct))).ToList();
        var changes = await ReadChangesAsync(connection, transaction, access.CenterId.Value, rows.Select(r => r.Id).ToList(), ct);
        var requests = rows.Select(row =>
        {
            var status = row.StatusValue;
            var expired = row.ProposalExpired(now);
            var proposed = status is AppointmentRequestStatus.Propuesta or AppointmentRequestStatus.Aceptada;
            return new FamilyAppointmentRequest(
                row.Id, row.TeamValue, row.ModalityValue, EnumCode.ParseCode<AppointmentRequestReason>(row.Reason), row.Comment, row.Availability, status,
                AppointmentSql.Utc(row.CreatedAt), proposed ? row.ProposedStartsAt : null, proposed ? row.ProposedEndsAt : null,
                status == AppointmentRequestStatus.Propuesta ? row.ProposalExpiresAt : null, expired, row.AppointmentId,
                status == AppointmentRequestStatus.Propuesta && !expired, status == AppointmentRequestStatus.Propuesta,
                status is AppointmentRequestStatus.Pendiente or AppointmentRequestStatus.Propuesta,
                changes[row.Id].Select(c => new FamilyAppointmentRequestChange(
                    EnumCode.ParseCode<AppointmentRequestAction>(c.Action), AppointmentSql.Utc(c.At), c.Profile == SystemProfile.Administracion.ToCode())).ToList());
        }).ToList();
        var open = requests.Where(r => r.CanCancel).OrderByDescending(r => r.CreatedAt);
        var rest = requests.Where(r => !r.CanCancel).OrderByDescending(r => r.CreatedAt);
        return open.Concat(rest).ToList();
    }

    private sealed record PreviousRow(Guid Id, Guid LinkId, string Team, string Modality, string Reason);
}
