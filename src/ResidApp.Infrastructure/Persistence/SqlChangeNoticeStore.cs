using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El registro de envíos del aviso de cambio de cita (script 0082). Reservar es solo anotar una fila PENDIENTE con el familiar que reservó la cita (sin dirección); la
/// tarea programada la reclama, vuelve a leer la cita y su último cambio y el vínculo con la misma consulta que usa el portal
/// (<see cref="SqlFamilyPortalReader.AccessibleLinks"/>, no una copia), y decide a quién escribir o por qué no. Nunca lee <c>citas_cambios.motivo</c> (el motivo interno de
/// Administración, que la familia no ve) ni el detalle de ninguna solicitud. La fila no guarda la dirección: se devuelve para enviar y no se escribe en ningún sitio.
/// </summary>
public sealed class SqlChangeNoticeStore(SqlConnectionFactory connections) : IChangeNoticeStore
{
    private const int UniqueViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    // Las consultas de la tarea programada, a la vista de las pruebas: se ejecutan con el usuario del rol residapp_tareas (script 0083) para comprobar que le bastan sus permisos.
    internal const string ListDueSql = """
        SELECT TOP (@limit) id AS Id, centro_id AS CenterId, referencia_id AS AppointmentId, referencia_clave AS ChangeKey, idioma AS Language
          FROM dbo.envios_correo
         WHERE plantilla_codigo = @template AND estado_codigo = 'PENDIENTE' AND intentos = 0 AND creado_en <= @before
         ORDER BY creado_en
        """;

    internal const string ClaimSql = "UPDATE dbo.envios_correo SET intentos = 1 WHERE id = @id AND estado_codigo = 'PENDIENTE' AND intentos = 0";

    internal const string ChangeSql = """
        SELECT c.estado_codigo AS Status, c.inicio_en AS StartsAt, c.cuenta_id AS AccountId, c.vinculo_id AS LinkId,
               (SELECT MAX(x.numero) FROM dbo.citas_cambios x WHERE x.cita_id = c.id) AS LastNumber,
               ch.accion_codigo AS Action, ch.actor_perfil_codigo AS ActorProfile, ch.causa_codigo AS Cause,
               ch.inicio_anterior_en AS PreviousStart, ch.inicio_nuevo_en AS NewStart
          FROM dbo.citas c
          LEFT JOIN dbo.citas_cambios ch ON ch.cita_id = c.id AND ch.centro_id = c.centro_id AND ch.numero = @number
         WHERE c.id = @appointmentId AND c.centro_id = @center
        """;

    internal const string RecipientSql = $"SELECT fam.correo AS Address {SqlFamilyPortalReader.AccessibleLinks} AND link.id = @LinkId";

    internal const string IdentitySql = "SELECT CASE WHEN IS_MEMBER(N'residapp_tareas') = 1 OR IS_MEMBER(N'db_owner') = 1 THEN 1 ELSE 0 END";

    internal const string ResolveSql = """
        UPDATE dbo.envios_correo
           SET estado_codigo = @state, motivo_codigo = @reason, acs_operacion_id = @operationId, intentos = @attempts, resuelto_en = @now
         WHERE id = @id AND estado_codigo = 'PENDIENTE'
        """;

    public async Task RegisterAsync(
        CenterId centerId, Guid appointmentId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.envios_correo
                    (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, motivo_codigo, intentos, creado_en, resuelto_en)
                SELECT NEWID(), c.centro_id, @template, @version, @language, link.familiar_id, c.id, @key, 'PENDIENTE', NULL, 0, @now, NULL
                  FROM dbo.citas c
                  JOIN dbo.residentes_familiares link ON link.id = c.vinculo_id AND link.centro_id = c.centro_id
                 WHERE c.id = @appointmentId AND c.centro_id = @center
                """, new
            {
                template = EmailLog.ChangeTemplate, version = templateVersion, language, appointmentId, center = centerId.Value,
                key = changeNumber.ToString(CultureInfo.InvariantCulture), now = now.UtcDateTime,
            }, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is UniqueViolation or UniqueConstraintViolation)
        {
            // otra petición ya reservó este aviso: se manda una sola vez
        }
    }

    public async Task<IReadOnlyList<PendingChangeNotice>> ListDueAsync(DateTimeOffset reservedBefore, int limit, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<DueRow>(new CommandDefinition(ListDueSql, new { limit, template = EmailLog.ChangeTemplate, before = reservedBefore.UtcDateTime }, cancellationToken: ct));
        return [.. rows.Select(r => new PendingChangeNotice(r.Id, CenterId.From(r.CenterId), r.AppointmentId, int.Parse(r.ChangeKey, CultureInfo.InvariantCulture), r.Language))];
    }

    public async Task<bool> ClaimAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(ClaimSql, new { id }, cancellationToken: ct)) == 1;
    }

    public async Task<ChangeNoticePreparation> PrepareAsync(PendingChangeNotice notice, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ChangeRow>(new CommandDefinition(ChangeSql, new { appointmentId = notice.AppointmentId, center = notice.CenterId.Value, number = notice.ChangeNumber }, cancellationToken: ct));
        if (row is null || !row.IsCurrent(notice.ChangeNumber))
        {
            return new(null, EmailLog.AppointmentNotCurrent);
        }

        if (row.RelevantStart is not { } relevantStart || AppointmentSql.Utc(relevantStart) <= now)
        {
            return new(null, EmailLog.AppointmentPassed);
        }

        var recipient = await connection.QuerySingleOrDefaultAsync<Recipient>(new CommandDefinition(
            RecipientSql,
            new
            {
                row.AccountId, CenterId = notice.CenterId.Value, Today = DateOnly.FromDateTime(now.LocalDateTime).ToDateTime(TimeOnly.MinValue), row.LinkId,
            }, cancellationToken: ct));
        if (recipient is null)
        {
            return new(null, EmailLog.NoAccess);
        }

        return string.IsNullOrWhiteSpace(recipient.Address) ? new(null, EmailLog.NoAddress) : new(recipient.Address.Trim(), null);
    }

    public async Task ResolveAsync(
        Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(ResolveSql, new { id, state, reason, operationId, attempts, now = now.UtcDateTime }, cancellationToken: ct));
    }

    public async Task<bool> HasTaskIdentityAsync(CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(IdentitySql, cancellationToken: ct)) == 1;
    }

    private sealed record DueRow(Guid Id, Guid CenterId, Guid AppointmentId, string ChangeKey, string Language);

    private sealed record Recipient(string? Address);

    private sealed record ChangeRow(
        string Status, DateTime StartsAt, Guid AccountId, Guid LinkId, int? LastNumber, string? Action, string? ActorProfile, string? Cause, DateTime? PreviousStart, DateTime? NewStart)
    {
        /// <summary>El cambio avisado sigue siendo el último de la cita, lo hizo Administración sin causa de sistema (la baja) y la cita está como lo dejó ese cambio.</summary>
        internal bool IsCurrent(int changeNumber) =>
            LastNumber == changeNumber && ActorProfile == SystemProfile.Administracion.ToCode() && Cause is null
            && ((Action == AppointmentChangeAction.Reprogramada.ToCode() && Status == AppointmentStatus.Confirmada.ToCode() && NewStart == StartsAt)
                || (Action == AppointmentChangeAction.Cancelada.ToCode() && Status == AppointmentStatus.Cancelada.ToCode()));

        /// <summary>La hora que importaba: la nueva en una reprogramación, la original en una cancelación.</summary>
        internal DateTime? RelevantStart => Action == AppointmentChangeAction.Reprogramada.ToCode() ? NewStart : PreviousStart;
    }
}
