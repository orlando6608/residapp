using System.Globalization;
using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El registro de envíos del recordatorio de cita (script 0092). La reserva la hace el procedimiento <c>dbo.reservar_recordatorios_cita</c> (el rol de la tarea no puede insertar en la
/// tabla): calcula «mañana» con la hora de cada centro, solo actúa en la franja de envío y deja una fila PENDIENTE por cita, o ya cerrada como OMITIDO / CITA_RECIENTE. La tarea
/// reclama cada fila, vuelve a leer la cita y el vínculo con la misma consulta que usa el portal (<see cref="SqlFamilyPortalReader.AccessibleLinks"/>, no una copia) y decide a quién
/// escribir o por qué no. Nunca lee <c>citas_cambios</c> ni el detalle de ninguna solicitud. La fila no guarda la dirección: se devuelve para enviar y no se escribe en ningún sitio.
/// </summary>
public sealed class SqlReminderStore(SqlConnectionFactory connections, TimeZoneInfo? zone = null) : IReminderStore
{
    // Las consultas de la tarea programada, a la vista de las pruebas: se ejecutan con el usuario del rol residapp_tareas (script 0083) para comprobar que le bastan sus permisos.
    internal const string ReserveSql = "EXEC dbo.reservar_recordatorios_cita @language, @version";

    internal const string ListPendingSql = """
        SELECT TOP (@limit) id AS Id, centro_id AS CenterId, referencia_id AS AppointmentId, referencia_clave AS DateKey, idioma AS Language
          FROM dbo.envios_correo
         WHERE plantilla_codigo = @template AND estado_codigo = 'PENDIENTE' AND intentos = 0
         ORDER BY creado_en
        """;

    internal const string AppointmentSqlText = """
        SELECT estado_codigo AS Status, inicio_en AS StartsAt, cuenta_id AS AccountId, vinculo_id AS LinkId
          FROM dbo.citas WHERE id = @appointmentId AND centro_id = @center
        """;

    public async Task<int> ReserveAsync(string language, string templateVersion, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(ReserveSql, new { language, version = templateVersion }, commandTimeout: 120, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PendingReminder>> ListPendingAsync(int limit, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<PendingRow>(new CommandDefinition(ListPendingSql, new { limit, template = EmailLog.ReminderTemplate }, cancellationToken: ct));
        return [.. rows.Select(r => new PendingReminder(r.Id, CenterId.From(r.CenterId), r.AppointmentId, r.DateKey, r.Language))];
    }

    public Task<TimeZoneInfo> ZoneOfCenterAsync(CenterId centerId, CancellationToken ct = default) =>
        zone is null ? SqlTaskCenterZone.ReadAsync(connections, centerId, ct) : Task.FromResult(zone);

    public async Task<bool> ClaimAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(SqlChangeNoticeStore.ClaimSql, new { id }, cancellationToken: ct)) == 1;
    }

    /// <summary>Las comprobaciones de docs/privacidad/recordatorio-cita-por-correo.md, apartado 3, en su orden (la franja ya la comprobó el servicio): cita confirmada, de la fecha de la
    /// clave y de mañana, sin pasar; acceso vigente de quien la reservó (con la autorización mirada en la fecha de la cita), con correo y confirmado.</summary>
    public async Task<ChangeNoticePreparation> PrepareAsync(PendingReminder reminder, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<AppointmentRow>(
            new CommandDefinition(AppointmentSqlText, new { appointmentId = reminder.AppointmentId, center = reminder.CenterId.Value }, cancellationToken: ct));
        if (row is null || row.Status != AppointmentStatus.Confirmada.ToCode())
        {
            return new(null, EmailLog.AppointmentNotCurrent);
        }

        // El día de la cita y «mañana» son los de la zona del centro (la misma con la que la base reservó el recordatorio).
        var centerZone = await ZoneOfCenterAsync(reminder.CenterId, ct);
        var startsAt = AppointmentSql.Utc(row.StartsAt);
        var appointmentDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startsAt, centerZone).DateTime);
        if (appointmentDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != reminder.DateKey)
        {
            return new(null, EmailLog.AppointmentNotCurrent);
        }

        if (appointmentDay != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, centerZone).DateTime).AddDays(1))
        {
            return new(null, EmailLog.OutOfWindow);
        }

        if (startsAt <= now)
        {
            return new(null, EmailLog.AppointmentPassed);
        }

        // La autorización se mira en la fecha de la cita: si caduca hoy, mañana la persona no verá la cita y el aviso la llevaría a una pantalla vacía.
        var recipient = await connection.QuerySingleOrDefaultAsync<Recipient>(new CommandDefinition(
            SqlChangeNoticeStore.RecipientSql,
            new { row.AccountId, CenterId = reminder.CenterId.Value, Today = appointmentDay.ToDateTime(TimeOnly.MinValue), row.LinkId }, cancellationToken: ct));
        if (recipient is null)
        {
            return new(null, EmailLog.NoAccess);
        }

        if (recipient.OptedOutAt is not null)
        {
            return new(null, EmailLog.OptedOut);
        }

        if (string.IsNullOrWhiteSpace(recipient.Address))
        {
            return new(null, EmailLog.NoAddress);
        }

        return recipient.ConfirmedAt is null ? new(null, EmailLog.AddressNotConfirmed) : new(recipient.Address.Trim(), null);
    }

    public async Task ResolveAsync(
        Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(SqlChangeNoticeStore.ResolveSql, new { id, state, reason, operationId, attempts, now = now.UtcDateTime }, cancellationToken: ct));
    }

    public async Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(SqlChangeNoticeStore.IdentitySql, cancellationToken: ct)) == 1;
    }

    private sealed record PendingRow(Guid Id, Guid CenterId, Guid AppointmentId, string DateKey, string Language);

    private sealed record AppointmentRow(string Status, DateTime StartsAt, Guid AccountId, Guid LinkId);

    private sealed record Recipient(string? Address, DateTime? ConfirmedAt, DateTime? OptedOutAt);
}
