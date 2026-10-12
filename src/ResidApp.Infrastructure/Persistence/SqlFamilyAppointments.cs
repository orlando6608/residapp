using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using static ResidApp.Infrastructure.Persistence.AppointmentSql;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Citas del Portal Familiar con reserva directa (script 0054). El acceso a cada residente es el criterio único del portal,
/// <see cref="SqlFamilyPortalReader.AccessibleLinks"/> (cuenta del familiar, vínculo no desvinculado y autorización Activa y vigente), y toda operación
/// empieza por él: sin acceso no hay datos ni rastro. Cada cita la ve y la cambia solo la cuenta que la reservó. Reservar y reprogramar se ordenan con
/// el bloqueo del equipo y se comprueban en la misma transacción que escribe, y el índice único del hueco es la última barrera: dos reservas del mismo
/// hueco no pueden convivir. Repetir la operación de una reserva (doble pulsación) devuelve la misma cita.
/// </summary>
public sealed class SqlFamilyAppointments(SqlConnectionFactory connections) : IFamilyAppointments
{
    private const string LinkColumns = "link.id AS LinkId, link.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, link.relacion AS Relationship";

    /// <summary>La familia ve las citas de los últimos 90 días y todas las futuras.</summary>
    private static readonly TimeSpan History = TimeSpan.FromDays(90);

    public async Task<FamilyAppointmentsHome?> ReadHomeAsync(
        FamilyPortalAccess access, Guid linkId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var link = await FindLinkAsync(connection, null, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var settings = (await ReadSettingsAsync(connection, null, access.CenterId.Value, ct)).Value;
        return new FamilyAppointmentsHome(
            new FamilyPortalResident(link.LinkId, link.ResidentName, link.Relationship), settings,
            await ReadModalitiesAsync(connection, null, access.CenterId.Value, ct),
            await ReadAppointmentsAsync(connection, null, access, link.ResidentId, settings, now, null, ct));
    }

    public async Task<IReadOnlyList<AppointmentSlot>?> ListSlotsAsync(
        FamilyPortalAccess access, Guid linkId, AppointmentTeam team, AppointmentModality modality, Guid? reschedulingId, DateOnly today, DateTimeOffset now,
        CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var link = await FindLinkAsync(connection, null, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var center = access.CenterId.Value;
        var settings = (await ReadSettingsAsync(connection, null, center, ct)).Value;
        if (reschedulingId is { } id
            && (await ReadAppointmentsAsync(connection, null, access, link.ResidentId, settings, now, id, ct)).Count == 0)
        {
            return null;
        }

        if (!settings.BookingEnabled || !(await ReadModalitiesAsync(connection, null, center, ct)).Contains(new AppointmentModalitySetting(team, modality)))
        {
            return [];
        }

        return await FreeSlotsAsync(connection, null, center, team, modality, settings, now, reschedulingId, ct);
    }

    public async Task<FamilyAppointment?> BookAsync(
        FamilyPortalAccess access, Guid linkId, AppointmentTeam team, AppointmentModality modality, DateTimeOffset startsAt, Guid operationId,
        DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var center = access.CenterId.Value;
        await LockTeamAsync(connection, transaction, center, team, ct);
        var settings = (await ReadSettingsAsync(connection, transaction, center, ct)).Value;

        // La misma operación de la misma cuenta es la doble pulsación: devuelve la cita ya creada, si es la misma petición.
        var previous = await connection.QuerySingleOrDefaultAsync<PreviousRow>(new CommandDefinition("""
            SELECT id AS Id, vinculo_id AS LinkId, equipo_codigo AS Team, modalidad_codigo AS Modality, inicio_en AS StartsAt
              FROM dbo.citas WHERE cuenta_id = @account AND operacion_id = @operationId
            """, new { account = access.AccountId.Value, operationId }, transaction, cancellationToken: ct));
        if (previous is not null)
        {
            if (previous.LinkId != link.LinkId || previous.Team != team.ToCode() || previous.Modality != modality.ToCode()
                || Utc(previous.StartsAt).UtcDateTime != startsAt.UtcDateTime)
            {
                throw new DomainValidationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
            }

            return (await ReadAppointmentsAsync(connection, transaction, access, link.ResidentId, settings, now, previous.Id, ct)).FirstOrDefault();
        }

        if (!settings.BookingEnabled)
        {
            throw new DomainValidationException("APPOINTMENT_SERVICE_DISABLED");
        }

        if (!(await ReadModalitiesAsync(connection, transaction, center, ct)).Contains(new AppointmentModalitySetting(team, modality)))
        {
            throw new DomainValidationException("APPOINTMENT_MODALITY_UNAVAILABLE");
        }

        var slot = (await FreeSlotsAsync(connection, transaction, center, team, modality, settings, now, null, ct))
            .FirstOrDefault(s => s.StartsAt.UtcDateTime == startsAt.UtcDateTime)
            ?? throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        var openForTeam = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.citas
             WHERE centro_id = @center AND residente_id = @resident AND equipo_codigo = @team AND estado_codigo = 'CONFIRMADA' AND inicio_en > @now
            """, new { center, resident = link.ResidentId, team = team.ToCode(), now = now.UtcDateTime }, transaction, cancellationToken: ct));
        if (openForTeam > 0)
        {
            throw new DomainValidationException("APPOINTMENT_LIMIT_REACHED");
        }

        var id = Guid.NewGuid();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas
                    (id, centro_id, residente_id, vinculo_id, cuenta_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, estado_codigo, operacion_id, creada_en)
                VALUES (@id, @center, @resident, @link, @account, @team, @modality, @startsAt, @endsAt, 'CONFIRMADA', @operationId, @now)
                """, new
            {
                id, center, resident = link.ResidentId, link = link.LinkId, account = access.AccountId.Value, team = team.ToCode(), modality = modality.ToCode(),
                startsAt = slot.StartsAt.UtcDateTime, endsAt = slot.EndsAt.UtcDateTime, operationId, now = now.UtcDateTime,
            }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (IsUniqueViolation(error, SlotIndex))
        {
            throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        }

        await RecordChangeAsync(
            connection, transaction, center, id, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentChangeAction.Creada, "APPOINTMENT_BOOK", null, slot.StartsAt, null, now, ct);
        var booked = (await ReadAppointmentsAsync(connection, transaction, access, link.ResidentId, settings, now, id, ct)).First();
        transaction.Commit();
        return booked;
    }

    public async Task<FamilyAppointment?> RescheduleAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateTimeOffset newStartsAt, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var center = access.CenterId.Value;
        var team = (await ReadOwnAsync(connection, transaction, access, link.ResidentId, appointmentId, forUpdate: false, ct))?.Team;
        if (team is null)
        {
            return null;
        }

        await LockTeamAsync(connection, transaction, center, team.Value, ct);
        var appointment = await ReadOwnAsync(connection, transaction, access, link.ResidentId, appointmentId, forUpdate: true, ct);
        if (appointment is null)
        {
            return null;
        }

        var settings = (await ReadSettingsAsync(connection, transaction, center, ct)).Value;
        EnsureFamilyCanChange(appointment, settings, now);
        if (!settings.BookingEnabled)
        {
            throw new DomainValidationException("APPOINTMENT_SERVICE_DISABLED");
        }

        var slot = (await FreeSlotsAsync(connection, transaction, center, appointment.Team, appointment.Modality, settings, now, appointment.Id, ct))
            .FirstOrDefault(s => s.StartsAt.UtcDateTime == newStartsAt.UtcDateTime);
        if (slot is null || slot.StartsAt.UtcDateTime == appointment.StartsAt.UtcDateTime)
        {
            throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        }

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.citas SET inicio_en = @startsAt, fin_en = @endsAt WHERE id = @appointmentId",
                new { appointmentId, startsAt = slot.StartsAt.UtcDateTime, endsAt = slot.EndsAt.UtcDateTime }, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (IsUniqueViolation(error, SlotIndex))
        {
            throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        }

        await RecordChangeAsync(
            connection, transaction, center, appointmentId, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentChangeAction.Reprogramada, "APPOINTMENT_RESCHEDULE", appointment.StartsAt, slot.StartsAt, null, now, ct);
        var changed = (await ReadAppointmentsAsync(connection, transaction, access, link.ResidentId, settings, now, appointmentId, ct)).First();
        transaction.Commit();
        return changed;
    }

    public async Task<bool> CancelAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await FindLinkAsync(connection, transaction, access, linkId, today, ct);
        if (link is null)
        {
            return false;
        }

        var appointment = await ReadOwnAsync(connection, transaction, access, link.ResidentId, appointmentId, forUpdate: true, ct);
        if (appointment is null)
        {
            return false;
        }

        EnsureFamilyCanChange(appointment, (await ReadSettingsAsync(connection, transaction, access.CenterId.Value, ct)).Value, now);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.citas SET estado_codigo = 'CANCELADA' WHERE id = @appointmentId", new { appointmentId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, access.CenterId.Value, appointmentId, link.ResidentId, access.AccountId.Value, SystemProfile.Familiar,
            AppointmentChangeAction.Cancelada, "APPOINTMENT_CANCEL", appointment.StartsAt, null, null, now, ct);
        transaction.Commit();
        return true;
    }

    public async Task<FamilyAppointment?> ReadAppointmentAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, DateOnly today, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var link = await FindLinkAsync(connection, null, access, linkId, today, ct);
        if (link is null)
        {
            return null;
        }

        var settings = (await ReadSettingsAsync(connection, null, access.CenterId.Value, ct)).Value;
        return (await ReadAppointmentsAsync(connection, null, access, link.ResidentId, settings, now, appointmentId, ct)).FirstOrDefault();
    }

    /// <summary>Solo una cita confirmada y con tiempo suficiente: antes de la antelación mínima del centro.</summary>
    private static void EnsureFamilyCanChange(OwnAppointment appointment, AppointmentServiceSettings settings, DateTimeOffset now)
    {
        if (appointment.Status != AppointmentStatus.Confirmada || appointment.StartsAt <= now)
        {
            throw new DomainValidationException("APPOINTMENT_NOT_CHANGEABLE");
        }

        if (!AppointmentRules.CanFamilyChange(appointment.StartsAt, now, settings))
        {
            throw new DomainValidationException("APPOINTMENT_CHANGE_TOO_LATE");
        }
    }

    private static Task<LinkRow?> FindLinkAsync(
        IDbConnection connection, IDbTransaction? transaction, FamilyPortalAccess access, Guid linkId, DateOnly today, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<LinkRow>(new CommandDefinition(
            $"SELECT {LinkColumns} {SqlFamilyPortalReader.AccessibleLinks} AND link.id = @LinkId",
            new { AccountId = access.AccountId.Value, CenterId = access.CenterId.Value, Today = today.ToDateTime(TimeOnly.MinValue), LinkId = linkId },
            transaction, cancellationToken: ct));

    private sealed record OwnAppointment(Guid Id, AppointmentTeam Team, AppointmentModality Modality, DateTimeOffset StartsAt, AppointmentStatus Status);

    /// <summary>Una cita de esta cuenta sobre este residente, con su fila bloqueada si va a cambiar.</summary>
    private static async Task<OwnAppointment?> ReadOwnAsync(
        IDbConnection connection, IDbTransaction transaction, FamilyPortalAccess access, Guid residentId, Guid appointmentId, bool forUpdate, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<OwnRow>(new CommandDefinition($"""
            SELECT id AS Id, equipo_codigo AS Team, modalidad_codigo AS Modality, inicio_en AS StartsAt, estado_codigo AS Status
              FROM dbo.citas {(forUpdate ? "WITH (UPDLOCK, ROWLOCK)" : "")}
             WHERE id = @appointmentId AND centro_id = @center AND residente_id = @residentId AND cuenta_id = @account
            """, new { appointmentId, center = access.CenterId.Value, residentId, account = access.AccountId.Value }, transaction, cancellationToken: ct));
        return row is null
            ? null
            : new OwnAppointment(
                row.Id, EnumCode.ParseCode<AppointmentTeam>(row.Team), EnumCode.ParseCode<AppointmentModality>(row.Modality), Utc(row.StartsAt),
                EnumCode.ParseCode<AppointmentStatus>(row.Status));
    }

    /// <summary>Las citas de la cuenta sobre el residente (una sola si se indica), las próximas primero y después las anteriores, de la más reciente a la más antigua.</summary>
    private static async Task<IReadOnlyList<FamilyAppointment>> ReadAppointmentsAsync(
        IDbConnection connection, IDbTransaction? transaction, FamilyPortalAccess access, Guid residentId, AppointmentServiceSettings settings, DateTimeOffset now,
        Guid? appointmentId, CancellationToken ct)
    {
        var rows = (await connection.QueryAsync<AppointmentRow>(new CommandDefinition("""
            SELECT id AS Id, equipo_codigo AS Team, modalidad_codigo AS Modality, inicio_en AS StartsAt, fin_en AS EndsAt, estado_codigo AS Status
              FROM dbo.citas
             WHERE centro_id = @center AND residente_id = @residentId AND cuenta_id = @account
               AND ((@appointmentId IS NOT NULL AND id = @appointmentId) OR (@appointmentId IS NULL AND inicio_en >= @since))
            """, new
        {
            center = access.CenterId.Value, residentId, account = access.AccountId.Value, appointmentId, since = (now - History).UtcDateTime,
        }, transaction, cancellationToken: ct))).ToList();
        var changes = (await connection.QueryAsync<ChangeRow>(new CommandDefinition("""
            SELECT cita_id AS AppointmentId, accion_codigo AS Action, actor_perfil_codigo AS Profile, ocurrido_en AS At
              FROM dbo.citas_cambios WHERE centro_id = @center AND cita_id IN @ids ORDER BY numero
            """, new { center = access.CenterId.Value, ids = rows.Select(r => r.Id).ToList() }, transaction, cancellationToken: ct))).ToLookup(c => c.AppointmentId);
        var appointments = rows.Select(row =>
        {
            var status = EnumCode.ParseCode<AppointmentStatus>(row.Status);
            var startsAt = Utc(row.StartsAt);
            var canCancel = status == AppointmentStatus.Confirmada && startsAt > now && AppointmentRules.CanFamilyChange(startsAt, now, settings);
            return new FamilyAppointment(
                row.Id, EnumCode.ParseCode<AppointmentTeam>(row.Team), EnumCode.ParseCode<AppointmentModality>(row.Modality), startsAt, Utc(row.EndsAt), status,
                canCancel, canCancel && settings.BookingEnabled,
                changes[row.Id].Select(c => new FamilyAppointmentChange(
                    EnumCode.ParseCode<AppointmentChangeAction>(c.Action), Utc(c.At), c.Profile == SystemProfile.Administracion.ToCode())).ToList());
        }).ToList();
        var upcoming = appointments.Where(a => a.Status == AppointmentStatus.Confirmada && a.StartsAt > now).OrderBy(a => a.StartsAt);
        var rest = appointments.Where(a => !(a.Status == AppointmentStatus.Confirmada && a.StartsAt > now)).OrderByDescending(a => a.StartsAt);
        return upcoming.Concat(rest).ToList();
    }

    private sealed record LinkRow(Guid LinkId, Guid ResidentId, string ResidentName, string Relationship);

    private sealed record PreviousRow(Guid Id, Guid LinkId, string Team, string Modality, DateTime StartsAt);

    private sealed record OwnRow(Guid Id, string Team, string Modality, DateTime StartsAt, string Status);

    private sealed record AppointmentRow(Guid Id, string Team, string Modality, DateTime StartsAt, DateTime EndsAt, string Status);

    private sealed record ChangeRow(Guid AppointmentId, string Action, string Profile, DateTime At);
}
