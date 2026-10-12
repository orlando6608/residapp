using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;
using static ResidApp.Infrastructure.Persistence.AppointmentSql;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Configuración y agenda de las citas para Administración (script 0054). Cada operación comprueba en su transacción que quien actúa es el
/// administrador del centro y deja su auditoría. La configuración general se ordena con la versión (como el horario de los comunicados); las franjas
/// y los cambios de hora se ordenan con el bloqueo del equipo (<see cref="AppointmentSql.LockTeamAsync"/>). La agenda es la de todo el centro: el
/// servicio de citas es del centro, no de una unidad (suposición a confirmar con CJ).
/// </summary>
public sealed class SqlAppointmentServiceRepository(SqlConnectionFactory connections) : IAppointmentServiceRepository
{
    public async Task<AppointmentServiceView> ReadAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var center = access.CenterId.Value;
        var settings = await ReadSettingsAsync(connection, null, center, ct);
        return new AppointmentServiceView(
            settings.Value, await ReadModalitiesAsync(connection, null, center, ct), await ReadWindowsAsync(connection, null, center, null, ct),
            await ReadBlocksAsync(connection, null, center, null, DateTimeOffset.UtcNow, ct), settings.Version);
    }

    public async Task<int> SaveSettingsAsync(
        AccountAdministrationAccess access, int expectedVersion, AppointmentServiceSettings settings,
        IReadOnlyCollection<AppointmentModalitySetting> modalities, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var center = access.CenterId.Value;

        // El bloqueo ordena dos ediciones simultáneas: la segunda ya ve la versión de la primera.
        var version = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT version FROM dbo.citas_configuracion_centro WITH (UPDLOCK, HOLDLOCK) WHERE centro_id = @center", new { center }, transaction, cancellationToken: ct)) ?? 0;
        if (version != expectedVersion)
        {
            throw new DomainValidationException("APPOINTMENT_SERVICE_REVISION_CONFLICT");
        }

        var current = await ReadSettingsAsync(connection, transaction, center, ct);
        var currentModalities = await ReadModalitiesAsync(connection, transaction, center, ct);
        if (current.Value == settings && currentModalities.ToHashSet().SetEquals(modalities))
        {
            return version;
        }

        var occurredAt = DateTimeOffset.UtcNow;
        var parameters = new
        {
            center, mode = settings.Mode.ToCode(), notice = settings.MinimumNoticeHours, horizon = settings.MaximumHorizonDays, newVersion = version + 1,
        };
        await connection.ExecuteAsync(new CommandDefinition(version == 0
            ? """
              INSERT INTO dbo.citas_configuracion_centro (centro_id, modo_codigo, antelacion_minima_horas, horizonte_maximo_dias, version)
              VALUES (@center, @mode, @notice, @horizon, @newVersion)
              """
            : """
              UPDATE dbo.citas_configuracion_centro
                 SET modo_codigo = @mode, antelacion_minima_horas = @notice, horizonte_maximo_dias = @horizon, version = @newVersion
               WHERE centro_id = @center
              """, parameters, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.citas_modalidades_habilitadas WHERE centro_id = @center", new { center }, transaction, cancellationToken: ct));
        foreach (var modality in modalities)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas_modalidades_habilitadas (centro_id, equipo_codigo, modalidad_codigo) VALUES (@center, @team, @modality)
                """, new { center, team = modality.Team.ToCode(), modality = modality.Modality.ToCode() }, transaction, cancellationToken: ct));
        }

        await AuditAsync(connection, transaction, access, "APPOINTMENT_SERVICE", center, "APPOINTMENT_SERVICE_UPDATE", occurredAt, ct);
        transaction.Commit();
        return version + 1;
    }

    public async Task AddWindowAsync(AccountAdministrationAccess access, AppointmentWindow window, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var center = access.CenterId.Value;
        await LockTeamAsync(connection, transaction, center, window.Team, ct);
        var existing = await ReadWindowsAsync(connection, transaction, center, window.Team, ct);
        if (existing.Any(window.Overlaps))
        {
            throw new DomainValidationException(AppointmentWindow.OverlapCode);
        }

        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_franjas
                (id, centro_id, equipo_codigo, dia_semana, hora_inicio, hora_fin, duracion_minutos, creada_por_cuenta_id, creada_en)
            VALUES (@id, @center, @team, @day, @start, @end, @minutes, @account, @occurredAt)
            """, new
        {
            id = window.Id, center, team = window.Team.ToCode(), day = window.IsoDayOfWeek, start = window.Start.ToTimeSpan(), end = window.End.ToTimeSpan(),
            minutes = window.DurationMinutes, account = access.AccountId.Value, occurredAt = occurredAt.UtcDateTime,
        }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, "APPOINTMENT_WINDOW", window.Id, "APPOINTMENT_WINDOW_ADD", occurredAt, ct);
        transaction.Commit();
    }

    public async Task RetireWindowAsync(AccountAdministrationAccess access, Guid windowId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        var retired = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_franjas SET retirada_en = @occurredAt WHERE id = @windowId AND centro_id = @center AND retirada_en IS NULL
            """, new { windowId, center = access.CenterId.Value, occurredAt = occurredAt.UtcDateTime }, transaction, cancellationToken: ct));
        if (retired == 0)
        {
            throw new AccessDeniedException();
        }

        await AuditAsync(connection, transaction, access, "APPOINTMENT_WINDOW", windowId, "APPOINTMENT_WINDOW_RETIRE", occurredAt, ct);
        transaction.Commit();
    }

    public async Task AddBlockAsync(AccountAdministrationAccess access, AppointmentBlock block, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_bloqueos
                (id, centro_id, equipo_codigo, modalidad_codigo, inicio_en, fin_en, motivo, creado_por_cuenta_id, creado_en)
            VALUES (@id, @center, @team, @modality, @startsAt, @endsAt, @reason, @account, @occurredAt)
            """, new
        {
            id = block.Id, center = access.CenterId.Value, team = block.Team.ToCode(), modality = block.Modality?.ToCode(), startsAt = block.StartsAt.UtcDateTime,
            endsAt = block.EndsAt.UtcDateTime, reason = block.Reason, account = access.AccountId.Value, occurredAt = occurredAt.UtcDateTime,
        }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, access, "APPOINTMENT_BLOCK", block.Id, "APPOINTMENT_BLOCK_ADD", occurredAt, ct);
        transaction.Commit();
    }

    public async Task RetireBlockAsync(AccountAdministrationAccess access, Guid blockId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var occurredAt = DateTimeOffset.UtcNow;
        var retired = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_bloqueos SET retirado_en = @occurredAt WHERE id = @blockId AND centro_id = @center AND retirado_en IS NULL
            """, new { blockId, center = access.CenterId.Value, occurredAt = occurredAt.UtcDateTime }, transaction, cancellationToken: ct));
        if (retired == 0)
        {
            throw new AccessDeniedException();
        }

        await AuditAsync(connection, transaction, access, "APPOINTMENT_BLOCK", blockId, "APPOINTMENT_BLOCK_RETIRE", occurredAt, ct);
        transaction.Commit();
    }

    public async Task<IReadOnlyList<AdministrationAppointment>> ListAgendaAsync(
        AccountAdministrationAccess access, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = (await connection.QueryAsync<AgendaRow>(new CommandDefinition("""
            SELECT appointment.id AS Id, appointment.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, family.nombre_visible AS FamilyName,
                   link.relacion AS Relationship, appointment.equipo_codigo AS Team, appointment.modalidad_codigo AS Modality,
                   appointment.inicio_en AS StartsAt, appointment.fin_en AS EndsAt, appointment.estado_codigo AS Status
              FROM dbo.citas appointment
              JOIN dbo.residentes resident ON resident.id = appointment.residente_id AND resident.centro_id = appointment.centro_id
              JOIN dbo.residentes_familiares link ON link.id = appointment.vinculo_id AND link.centro_id = appointment.centro_id
              JOIN dbo.familiares family ON family.id = link.familiar_id AND family.centro_id = link.centro_id
             WHERE appointment.centro_id = @center AND appointment.inicio_en >= @from AND appointment.inicio_en < @to
             ORDER BY appointment.inicio_en, resident.nombre_visible
            """, new { center = access.CenterId.Value, from = from.UtcDateTime, to = to.UtcDateTime }, cancellationToken: ct))).ToList();
        var changes = (await connection.QueryAsync<ChangeRow>(new CommandDefinition("""
            SELECT cita_id AS AppointmentId, accion_codigo AS Action, actor_perfil_codigo AS Profile, ocurrido_en AS At,
                   inicio_anterior_en AS PreviousStartsAt, inicio_nuevo_en AS NewStartsAt, motivo AS Reason
              FROM dbo.citas_cambios WHERE centro_id = @center AND cita_id IN @ids ORDER BY numero
            """, new { center = access.CenterId.Value, ids = rows.Select(r => r.Id).ToList() }, cancellationToken: ct))).ToLookup(c => c.AppointmentId);
        return rows.Select(row => new AdministrationAppointment(
            row.Id, row.ResidentId, row.ResidentName, row.FamilyName, row.Relationship, EnumCode.ParseCode<AppointmentTeam>(row.Team),
            EnumCode.ParseCode<AppointmentModality>(row.Modality), Utc(row.StartsAt), Utc(row.EndsAt), EnumCode.ParseCode<AppointmentStatus>(row.Status),
            changes[row.Id].Select(c => new AppointmentChange(
                EnumCode.ParseCode<AppointmentChangeAction>(c.Action), EnumCode.ParseCode<SystemProfile>(c.Profile), Utc(c.At),
                c.PreviousStartsAt is { } previous ? Utc(previous) : null, c.NewStartsAt is { } next ? Utc(next) : null, c.Reason)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<AppointmentSlot>> ListRescheduleSlotsAsync(
        AccountAdministrationAccess access, Guid appointmentId, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var appointment = await ReadAppointmentAsync(connection, null, access.CenterId.Value, appointmentId, forUpdate: false, ct) ?? throw new AccessDeniedException();
        if (appointment.Status != AppointmentStatus.Confirmada)
        {
            throw new DomainValidationException("APPOINTMENT_NOT_CHANGEABLE");
        }

        var settings = (await ReadSettingsAsync(connection, null, access.CenterId.Value, ct)).Value with { MinimumNoticeHours = 0 };
        var slots = await FreeSlotsAsync(connection, null, access.CenterId.Value, appointment.Team, appointment.Modality, settings, now, appointment.Id, ct);
        return slots.Where(slot => slot.StartsAt.UtcDateTime != appointment.StartsAt.UtcDateTime).ToList();
    }

    public async Task CancelAsync(AccountAdministrationAccess access, Guid appointmentId, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var appointment = await ReadAppointmentAsync(connection, transaction, access.CenterId.Value, appointmentId, forUpdate: true, ct) ?? throw new AccessDeniedException();
        if (appointment.Status != AppointmentStatus.Confirmada)
        {
            throw new DomainValidationException("APPOINTMENT_NOT_CHANGEABLE");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.citas SET estado_codigo = 'CANCELADA' WHERE id = @appointmentId", new { appointmentId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, access.CenterId.Value, appointmentId, appointment.ResidentId, access.AccountId.Value, SystemProfile.Administracion,
            AppointmentChangeAction.Cancelada, "APPOINTMENT_CANCEL_ADMIN", appointment.StartsAt, null, reason, now, ct);
        transaction.Commit();
    }

    public async Task RescheduleAsync(
        AccountAdministrationAccess access, Guid appointmentId, DateTimeOffset newStartsAt, string reason, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var center = access.CenterId.Value;
        var team = (await ReadAppointmentAsync(connection, transaction, center, appointmentId, forUpdate: false, ct) ?? throw new AccessDeniedException()).Team;
        await LockTeamAsync(connection, transaction, center, team, ct);
        var appointment = await ReadAppointmentAsync(connection, transaction, center, appointmentId, forUpdate: true, ct) ?? throw new AccessDeniedException();
        if (appointment.Status != AppointmentStatus.Confirmada)
        {
            throw new DomainValidationException("APPOINTMENT_NOT_CHANGEABLE");
        }

        var settings = (await ReadSettingsAsync(connection, transaction, center, ct)).Value with { MinimumNoticeHours = 0 };
        var slots = await FreeSlotsAsync(connection, transaction, center, team, appointment.Modality, settings, now, appointment.Id, ct);
        var slot = slots.FirstOrDefault(s => s.StartsAt.UtcDateTime == newStartsAt.UtcDateTime);
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
            connection, transaction, center, appointmentId, appointment.ResidentId, access.AccountId.Value, SystemProfile.Administracion,
            AppointmentChangeAction.Reprogramada, "APPOINTMENT_RESCHEDULE_ADMIN", appointment.StartsAt, slot.StartsAt, reason, now, ct);
        transaction.Commit();
    }

    public async Task FinishAsync(AccountAdministrationAccess access, Guid appointmentId, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, transaction, access, ct);
        var appointment = await ReadAppointmentAsync(connection, transaction, access.CenterId.Value, appointmentId, forUpdate: true, ct) ?? throw new AccessDeniedException();
        if (appointment.Status != AppointmentStatus.Confirmada || appointment.StartsAt > now)
        {
            throw new DomainValidationException("APPOINTMENT_NOT_FINISHABLE");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.citas SET estado_codigo = 'FINALIZADA' WHERE id = @appointmentId", new { appointmentId }, transaction, cancellationToken: ct));
        await RecordChangeAsync(
            connection, transaction, access.CenterId.Value, appointmentId, appointment.ResidentId, access.AccountId.Value, SystemProfile.Administracion,
            AppointmentChangeAction.Finalizada, "APPOINTMENT_FINISH", null, null, null, now, ct);
        transaction.Commit();
    }

    private sealed record AppointmentRow(
        Guid Id, Guid ResidentId, AppointmentTeam Team, AppointmentModality Modality, DateTimeOffset StartsAt, AppointmentStatus Status);

    /// <summary>Una cita del centro, con su fila bloqueada si va a cambiar; null si no existe en el centro.</summary>
    private static async Task<AppointmentRow?> ReadAppointmentAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, Guid appointmentId, bool forUpdate, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<RawAppointmentRow>(new CommandDefinition($"""
            SELECT id AS Id, residente_id AS ResidentId, equipo_codigo AS Team, modalidad_codigo AS Modality, inicio_en AS StartsAt, estado_codigo AS Status
              FROM dbo.citas {(forUpdate ? "WITH (UPDLOCK, ROWLOCK)" : "")} WHERE id = @appointmentId AND centro_id = @centerId
            """, new { appointmentId, centerId }, transaction, cancellationToken: ct));
        return row is null
            ? null
            : new AppointmentRow(
                row.Id, row.ResidentId, EnumCode.ParseCode<AppointmentTeam>(row.Team), EnumCode.ParseCode<AppointmentModality>(row.Modality),
                Utc(row.StartsAt), EnumCode.ParseCode<AppointmentStatus>(row.Status));
    }

    private static Task AuditAsync(
        IDbConnection connection, IDbTransaction transaction, AccountAdministrationAccess access, string resourceType, Guid resourceId, string action,
        DateTimeOffset occurredAt, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @account, 'ADMINISTRACION', @center, @resourceType, @resourceId, @action, @occurredAt)
            """, new
        {
            account = access.AccountId.Value, center = access.CenterId.Value, resourceType, resourceId, action, occurredAt = occurredAt.UtcDateTime,
        }, transaction, cancellationToken: ct));

    private sealed record RawAppointmentRow(Guid Id, Guid ResidentId, string Team, string Modality, DateTime StartsAt, string Status);

    private sealed record AgendaRow(
        Guid Id, Guid ResidentId, string ResidentName, string FamilyName, string Relationship, string Team, string Modality, DateTime StartsAt, DateTime EndsAt,
        string Status);

    private sealed record ChangeRow(
        Guid AppointmentId, string Action, string Profile, DateTime At, DateTime? PreviousStartsAt, DateTime? NewStartsAt, string? Reason);
}
