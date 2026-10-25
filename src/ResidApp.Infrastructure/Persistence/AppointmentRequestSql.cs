using System.Data;
using Dapper;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Lo que comparten los repositorios de solicitudes de cita de Administración y de la familia (script 0057): las columnas de una solicitud, su
/// historial y la anotación de cada paso con su auditoría. Las búsquedas dentro de una transacción de escritura llevan FORCESEEK: en una BD recién
/// creada (la del CI) el plan recorre la clave primaria y dos transacciones se bloquean (lección de 0055 y 0056).
/// </summary>
internal static class AppointmentRequestSql
{
    internal const string OpenIndex = "UX_citas_solicitudes_abierta";
    internal const string OperationIndex = "UX_citas_solicitudes_operacion";

    /// <summary>Las columnas de una solicitud, con el alias <c>r</c>.</summary>
    internal const string Columns = """
        r.id AS Id, r.residente_id AS ResidentId, r.vinculo_id AS LinkId, r.cuenta_id AS AccountId, r.equipo_codigo AS Team, r.modalidad_codigo AS Modality,
        r.motivo_codigo AS Reason, r.comentario AS Comment, r.disponibilidad_dias AS Days, r.disponibilidad_franja AS DayPart, r.estado_codigo AS Status,
        r.propuesta_inicio_en AS ProposedStart, r.propuesta_fin_en AS ProposedEnd, r.propuesta_en AS ProposedAt, r.profesional_interno AS Professional, r.cita_id AS AppointmentId,
        r.creada_en AS CreatedAt
        """;

    internal sealed record RequestRow(
        Guid Id, Guid ResidentId, Guid LinkId, Guid AccountId, string Team, string Modality, string Reason, string? Comment, string Days, string DayPart,
        string Status, DateTime? ProposedStart, DateTime? ProposedEnd, DateTime? ProposedAt, string? Professional, Guid? AppointmentId, DateTime CreatedAt)
    {
        internal AppointmentRequestStatus StatusValue => EnumCode.ParseCode<AppointmentRequestStatus>(Status);

        internal AppointmentTeam TeamValue => EnumCode.ParseCode<AppointmentTeam>(Team);

        internal AppointmentModality ModalityValue => EnumCode.ParseCode<AppointmentModality>(Modality);

        internal AppointmentAvailability Availability => AppointmentAvailability.FromCodes(Days, EnumCode.ParseCode<AppointmentDayPart>(DayPart));

        internal DateTimeOffset? ProposedStartsAt => ProposedStart is { } start ? AppointmentSql.Utc(start) : null;

        internal DateTimeOffset? ProposedEndsAt => ProposedEnd is { } end ? AppointmentSql.Utc(end) : null;

        /// <summary>Cuándo caduca la propuesta (48 horas o 24 horas antes de la cita, lo que llegue antes; se calcula, no se guarda); null sin propuesta.</summary>
        internal DateTimeOffset? ProposalExpiresAt => ProposedAt is { } at && ProposedStartsAt is { } start
            ? AppointmentRequestRules.ProposalExpiresAt(AppointmentSql.Utc(at), start)
            : null;

        internal bool ProposalExpired(DateTimeOffset now) =>
            StatusValue == AppointmentRequestStatus.Propuesta && ProposalExpiresAt is { } expiresAt && now >= expiresAt;
    }

    internal sealed record ChangeRow(Guid RequestId, string Action, string Profile, DateTime At, DateTime? ProposedStart, string? Detail);

    /// <summary>El historial de las solicitudes indicadas, por orden.</summary>
    internal static Task<ILookup<Guid, ChangeRow>> ReadChangesAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, IReadOnlyCollection<Guid> requestIds, CancellationToken ct) =>
        // FORCESEEK por solicitud (índice único solicitud_id, numero), como en 0055 y 0056: sin él, dentro de una transacción el plan recorría el historial de todas
        // las solicitudes del centro. Con una lista vacía no hay nada que buscar (y FORCESEEK no admite el IN vacío de Dapper).
        requestIds.Count == 0
            ? Task.FromResult(Enumerable.Empty<ChangeRow>().ToLookup(c => c.RequestId))
            : ReadChangeRowsAsync(connection, transaction, centerId, requestIds, ct);

    private static async Task<ILookup<Guid, ChangeRow>> ReadChangeRowsAsync(
        IDbConnection connection, IDbTransaction? transaction, Guid centerId, IReadOnlyCollection<Guid> requestIds, CancellationToken ct) =>
        (await connection.QueryAsync<ChangeRow>(new CommandDefinition("""
            SELECT solicitud_id AS RequestId, accion_codigo AS Action, actor_perfil_codigo AS Profile, ocurrido_en AS At,
                   inicio_propuesto_en AS ProposedStart, detalle AS Detail
              FROM dbo.citas_solicitudes_cambios WITH (FORCESEEK) WHERE centro_id = @centerId AND solicitud_id IN @requestIds ORDER BY numero
            """, new { centerId, requestIds }, transaction, cancellationToken: ct))).ToLookup(c => c.RequestId);

    /// <summary>Una solicitud del centro con su fila bloqueada para escribir, buscada por clave primaria. Null si no existe en este centro.</summary>
    internal static Task<RequestRow?> ReadForUpdateAsync(
        IDbConnection connection, IDbTransaction transaction, Guid centerId, Guid requestId, Guid? accountId, Guid? residentId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition($"""
            SELECT {Columns} FROM dbo.citas_solicitudes r WITH (UPDLOCK, ROWLOCK, FORCESEEK)
             WHERE r.id = @requestId AND r.centro_id = @centerId
               AND (@accountId IS NULL OR r.cuenta_id = @accountId) AND (@residentId IS NULL OR r.residente_id = @residentId)
            """, new { requestId, centerId, accountId, residentId }, transaction, cancellationToken: ct));

    /// <summary>Anota el paso en el historial de la solicitud (CIT-C03) y lo audita (recurso APPOINTMENT_REQUEST) en la misma transacción.</summary>
    internal static async Task RecordChangeAsync(
        IDbConnection connection, IDbTransaction transaction, Guid centerId, Guid requestId, Guid residentId, Guid accountId, SystemProfile profile,
        AppointmentRequestAction action, string auditAction, DateTimeOffset? proposedStartsAt, string? detail, DateTimeOffset occurredAt, CancellationToken ct)
    {
        var number = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT ISNULL(MAX(numero), 0) + 1 FROM dbo.citas_solicitudes_cambios WITH (FORCESEEK) WHERE solicitud_id = @requestId",
            new { requestId }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_solicitudes_cambios
                (id, centro_id, solicitud_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, inicio_propuesto_en, detalle, ocurrido_en)
            VALUES (NEWID(), @centerId, @requestId, @number, @action, @accountId, @profile, @proposed, @detail, @occurredAt);

            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @accountId, @profile, @centerId, NULL, @residentId, 'APPOINTMENT_REQUEST', @requestId, @auditAction, @occurredAt);
            """, new
        {
            centerId, requestId, number, action = action.ToCode(), accountId, profile = profile.ToCode(), proposed = proposedStartsAt?.UtcDateTime, detail,
            occurredAt = occurredAt.UtcDateTime, residentId, auditAction,
        }, transaction, cancellationToken: ct));
    }
}
