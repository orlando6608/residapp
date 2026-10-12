using System.Data;
using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Lo que comparten las lecturas de las citas de Administración y de la familia (script 0054): la configuración del centro, las franjas, los
/// bloqueos y las citas que ocupan hueco, con los que se calculan los huecos libres (AppointmentSlots). Las reservas de un mismo centro y equipo se
/// ordenan con un bloqueo de aplicación (<see cref="LockTeamAsync"/>): dos reservas simultáneas no calculan sus huecos sobre el mismo estado.
/// </summary>
internal static class AppointmentSql
{
    internal const string SlotIndex = "UX_citas_hueco";
    internal const string OperationIndex = "UX_citas_operacion";

    /// <summary>La zona en que se entienden las horas de las franjas: la del servidor, como el horario de los comunicados.</summary>
    internal static TimeZoneInfo Zone => TimeZoneInfo.Local;

    internal sealed record Settings(AppointmentServiceSettings Value, int Version);

    /// <summary>La configuración del centro; sin fila, el servicio desactivado y la versión 0.</summary>
    internal static async Task<Settings> ReadSettingsAsync(IDbConnection connection, IDbTransaction? transaction, Guid centerId, CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<SettingsRow>(new CommandDefinition("""
            SELECT modo_codigo AS Mode, antelacion_minima_horas AS Notice, horizonte_maximo_dias AS Horizon, version AS Version
              FROM dbo.citas_configuracion_centro WHERE centro_id = @centerId
            """, new { centerId }, transaction, cancellationToken: ct));
        return row is null
            ? new Settings(AppointmentServiceSettings.Default, 0)
            : new Settings(new AppointmentServiceSettings(EnumCode.ParseCode<AppointmentServiceMode>(row.Mode), row.Notice, row.Horizon), row.Version);
    }

    internal static async Task<IReadOnlyList<AppointmentModalitySetting>> ReadModalitiesAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, CancellationToken ct) =>
        (await connection.QueryAsync<ModalityRow>(new CommandDefinition("""
            SELECT equipo_codigo AS Team, modalidad_codigo AS Modality FROM dbo.citas_modalidades_habilitadas
             WHERE centro_id = @centerId ORDER BY equipo_codigo, modalidad_codigo
            """, new { centerId }, transaction, cancellationToken: ct)))
        .Select(row => new AppointmentModalitySetting(EnumCode.ParseCode<AppointmentTeam>(row.Team), EnumCode.ParseCode<AppointmentModality>(row.Modality)))
        .ToList();

    internal static async Task<IReadOnlyList<AppointmentWindow>> ReadWindowsAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, AppointmentTeam? team, CancellationToken ct) =>
        (await connection.QueryAsync<WindowRow>(new CommandDefinition("""
            SELECT id AS Id, equipo_codigo AS Team, dia_semana AS Day, hora_inicio AS StartTime, hora_fin AS EndTime, duracion_minutos AS Minutes
              FROM dbo.citas_franjas
             WHERE centro_id = @centerId AND retirada_en IS NULL AND (@team IS NULL OR equipo_codigo = @team)
             ORDER BY equipo_codigo, dia_semana, hora_inicio
            """, new { centerId, team = team?.ToCode() }, transaction, cancellationToken: ct)))
        .Select(row => new AppointmentWindow(
            row.Id, EnumCode.ParseCode<AppointmentTeam>(row.Team), row.Day, TimeOnly.FromTimeSpan(row.StartTime), TimeOnly.FromTimeSpan(row.EndTime), row.Minutes))
        .ToList();

    /// <summary>Los bloqueos vigentes que no han terminado todavía (<paramref name="notEndedBefore"/>).</summary>
    internal static async Task<IReadOnlyList<AppointmentBlock>> ReadBlocksAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, AppointmentTeam? team, DateTimeOffset notEndedBefore, CancellationToken ct) =>
        (await connection.QueryAsync<BlockRow>(new CommandDefinition("""
            SELECT id AS Id, equipo_codigo AS Team, modalidad_codigo AS Modality, inicio_en AS StartsAt, fin_en AS EndsAt, motivo AS Reason
              FROM dbo.citas_bloqueos
             WHERE centro_id = @centerId AND retirado_en IS NULL AND fin_en > @notEndedBefore AND (@team IS NULL OR equipo_codigo = @team)
             ORDER BY inicio_en
            """, new { centerId, team = team?.ToCode(), notEndedBefore = notEndedBefore.UtcDateTime }, transaction, cancellationToken: ct)))
        .Select(row => new AppointmentBlock(
            row.Id, EnumCode.ParseCode<AppointmentTeam>(row.Team), row.Modality is null ? null : EnumCode.ParseCode<AppointmentModality>(row.Modality),
            Utc(row.StartsAt), Utc(row.EndsAt), row.Reason))
        .ToList();

    /// <summary>Lo que ocupa hueco en el equipo y no ha terminado: las citas no canceladas (salvo <paramref name="exceptId"/>) y las propuestas vivas de las
    /// solicitudes (salvo <paramref name="exceptRequestId"/>), que reservan su hueco (script 0057).</summary>
    internal static async Task<IReadOnlyList<AppointmentSlot>> ReadTakenAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, AppointmentTeam team, DateTimeOffset now, Guid? exceptId, CancellationToken ct,
        Guid? exceptRequestId = null) =>
        (await connection.QueryAsync<TakenRow>(new CommandDefinition("""
            SELECT inicio_en AS StartsAt, fin_en AS EndsAt FROM dbo.citas WITH (FORCESEEK)
             WHERE centro_id = @centerId AND equipo_codigo = @team AND estado_codigo <> 'CANCELADA' AND fin_en > @now
               AND (@exceptId IS NULL OR id <> @exceptId)
            UNION ALL
            SELECT propuesta_inicio_en, propuesta_fin_en FROM dbo.citas_solicitudes WITH (FORCESEEK)
             WHERE centro_id = @centerId AND equipo_codigo = @team AND estado_codigo = 'PROPUESTA' AND propuesta_fin_en > @now
               AND (@exceptRequestId IS NULL OR id <> @exceptRequestId)
            """, new { centerId, team = team.ToCode(), now = now.UtcDateTime, exceptId, exceptRequestId }, transaction, cancellationToken: ct)))
        .Select(row => new AppointmentSlot(Utc(row.StartsAt), Utc(row.EndsAt)))
        .ToList();

    /// <summary>Los huecos libres del equipo y la modalidad para quien reserva, con la antelación mínima indicada.</summary>
    internal static async Task<IReadOnlyList<AppointmentSlot>> FreeSlotsAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, AppointmentTeam team, AppointmentModality modality,
        AppointmentServiceSettings settings, DateTimeOffset now, Guid? exceptId, CancellationToken ct)
    {
        var windows = await ReadWindowsAsync(connection, transaction, centerId, team, ct);
        var blocks = await ReadBlocksAsync(connection, transaction, centerId, team, now, ct);
        var taken = await ReadTakenAsync(connection, transaction, centerId, team, now, exceptId, ct);
        return AppointmentSlots.Generate(windows, blocks, taken, team, modality, settings, now, Zone);
    }

    /// <summary>Ordena, dentro de la transacción, las reservas y los cambios de hora del mismo centro y equipo: la segunda espera a que la primera
    /// confirme y ve su resultado. El bloqueo se suelta al terminar la transacción.</summary>
    internal static async Task LockTeamAsync(IDbConnection connection, IDbTransaction transaction, Guid centerId, AppointmentTeam team, CancellationToken ct)
    {
        var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @result INT;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @result;
            """, new { resource = $"citas:{centerId:N}:{team.ToCode()}" }, transaction, cancellationToken: ct));
        if (result < 0)
        {
            throw new DomainValidationException("APPOINTMENT_SLOT_UNAVAILABLE");
        }
    }

    internal static Task<int> NextChangeNumberAsync(IDbConnection connection, IDbTransaction transaction, Guid appointmentId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT ISNULL(MAX(numero), 0) + 1 FROM dbo.citas_cambios WITH (FORCESEEK) WHERE cita_id = @appointmentId", new { appointmentId }, transaction, cancellationToken: ct));

    /// <summary>Anota el paso en el historial de la cita (CIT-C03) y lo audita (recurso APPOINTMENT) en la misma transacción; sin <c>auditAction</c> no
    /// audita (lo audita quien lo llama sobre otro recurso, como la aceptación de una solicitud).</summary>
    internal static async Task RecordChangeAsync(
        IDbConnection connection, IDbTransaction transaction, Guid centerId, Guid appointmentId, Guid residentId, Guid accountId, SystemProfile profile,
        AppointmentChangeAction action, string? auditAction, DateTimeOffset? previousStartsAt, DateTimeOffset? newStartsAt, string? reason,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        var number = await NextChangeNumberAsync(connection, transaction, appointmentId, ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_cambios
                (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, inicio_anterior_en, inicio_nuevo_en, motivo, ocurrido_en)
            VALUES (NEWID(), @centerId, @appointmentId, @number, @action, @accountId, @profile, @previous, @new, @reason, @occurredAt);

            IF @auditAction IS NOT NULL
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @accountId, @profile, @centerId, NULL, @residentId, 'APPOINTMENT', @appointmentId, @auditAction, @occurredAt);
            """, new
        {
            centerId, appointmentId, number, action = action.ToCode(), accountId, profile = profile.ToCode(), previous = previousStartsAt?.UtcDateTime,
            @new = newStartsAt?.UtcDateTime, reason, occurredAt = occurredAt.UtcDateTime, residentId, auditAction,
        }, transaction, cancellationToken: ct));
    }

    internal static bool IsUniqueViolation(Microsoft.Data.SqlClient.SqlException error, string indexName) =>
        (error.Number is 2601 or 2627) && error.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase);

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record SettingsRow(string Mode, int Notice, int Horizon, int Version);

    private sealed record ModalityRow(string Team, string Modality);

    private sealed record WindowRow(Guid Id, string Team, byte Day, TimeSpan StartTime, TimeSpan EndTime, int Minutes);

    private sealed record BlockRow(Guid Id, string Team, string? Modality, DateTime StartsAt, DateTime EndsAt, string Reason);

    private sealed record TakenRow(DateTime StartsAt, DateTime EndsAt);
}
