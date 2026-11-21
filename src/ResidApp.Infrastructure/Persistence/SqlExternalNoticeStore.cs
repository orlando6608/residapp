using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El registro de envíos de los avisos de citas externas a la familia (script 0105). La reserva la hace el procedimiento <c>dbo.reservar_avisos_citas_externas</c> y la comprobación justo antes de
/// enviar, <c>dbo.comprobar_aviso_cita_externa</c> (el rol de la tarea no puede insertar en la tabla ni leer el lugar, la especialidad ni «qué preparar» de la cita): una sola regla en la base,
/// la misma al reservar y al enviar. Después, con la cuenta y el vínculo que devuelve esa comprobación, vuelve a comprobar el acceso con la misma consulta que usa el portal
/// (<see cref="SqlFamilyPortalReader.AccessibleLinks"/>, no una copia), con la autorización mirada en la fecha de la cita. La fila no guarda la dirección: se devuelve para enviar y no se escribe en
/// ningún sitio.
/// </summary>
public sealed class SqlExternalNoticeStore(SqlConnectionFactory connections) : IExternalNoticeStore
{
    // Las consultas de la tarea programada, a la vista de las pruebas: se ejecutan con el usuario del rol residapp_tareas (script 0083) para comprobar que le bastan sus permisos.
    internal const string ReserveSql = "EXEC dbo.reservar_avisos_citas_externas @language, @version";

    internal const string CheckSql = "EXEC dbo.comprobar_aviso_cita_externa @id";

    internal const string ListPendingSql = """
        SELECT TOP (@limit) id AS Id, centro_id AS CenterId, referencia_id AS AppointmentId, referencia_clave AS [Key], idioma AS Language, plantilla_codigo AS Template
          FROM dbo.envios_correo
         WHERE plantilla_codigo IN (@template, @uncoveredTemplate) AND estado_codigo = 'PENDIENTE' AND intentos = 0
         ORDER BY creado_en
        """;

    public async Task<int> ReserveAsync(string language, string templateVersion, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(ReserveSql, new { language, version = templateVersion }, commandTimeout: 120, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PendingExternalNotice>> ListPendingAsync(int limit, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<PendingRow>(new CommandDefinition(
            ListPendingSql, new { limit, template = EmailLog.ExternalNoticeTemplate, uncoveredTemplate = EmailLog.ExternalNoticeUncoveredTemplate }, cancellationToken: ct));
        return [.. rows.Select(r => new PendingExternalNotice(r.Id, CenterId.From(r.CenterId), r.AppointmentId, r.Key, r.Language, r.Template))];
    }

    public Task<TimeZoneInfo> ZoneOfCenterAsync(CenterId centerId, CancellationToken ct = default) => SqlTaskCenterZone.ReadAsync(connections, centerId, ct);

    public async Task<bool> ClaimAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(SqlChangeNoticeStore.ClaimSql, new { id }, cancellationToken: ct)) == 1;
    }

    public async Task<ChangeNoticePreparation> PrepareAsync(PendingExternalNotice notice, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var check = await connection.QuerySingleAsync<(string? Reason, Guid? AccountId, Guid? LinkId, DateTime? Date)>(new CommandDefinition(CheckSql, new { id = notice.Id }, commandTimeout: 120, cancellationToken: ct));
        if (check.Reason is { } reason)
        {
            return new(null, reason);
        }

        // La misma consulta que el portal, con la autorización mirada en la fecha de la cita: si caduca antes, la persona no verá la cita y el aviso la llevaría a una pantalla vacía.
        var recipient = await connection.QuerySingleOrDefaultAsync<Recipient>(new CommandDefinition(
            SqlChangeNoticeStore.RecipientSql,
            new { AccountId = check.AccountId, CenterId = notice.CenterId.Value, Today = check.Date!.Value.Date, LinkId = check.LinkId }, cancellationToken: ct));
        if (recipient is null)
        {
            return new(null, EmailLog.NoAccess);
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

    private sealed record PendingRow(Guid Id, Guid CenterId, Guid AppointmentId, string Key, string Language, string Template);


    private sealed record Recipient(string? Address, DateTime? ConfirmedAt);
}
