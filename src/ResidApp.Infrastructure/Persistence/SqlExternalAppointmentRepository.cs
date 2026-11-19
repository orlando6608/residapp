using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Citas externas de los residentes (script 0101). La base solo separa por centro (seguridad por filas); la unidad la aplica este repositorio con las unidades concedidas al ámbito
/// de Enfermería y Medicina, a partir de la ubicación vigente del residente (la cita no guarda unidad: un traslado de unidad mueve su visibilidad). Cada escritura va en una transacción que
/// bloquea la cita (o al residente activo, al registrar), vuelve a comprobar el ámbito y, en Enfermería, el permiso EXTERNAL_APPOINTMENT_MANAGE, comprueba la versión vista, escribe su
/// fila en dbo.citas_externas_cambios y deja el evento en dbo.eventos_auditoria con solo identificadores: ni la especialidad, ni el lugar, ni qué preparar, ni los motivos.
/// </summary>
public sealed partial class SqlExternalAppointmentRepository(SqlConnectionFactory connections) : IExternalAppointmentRepository
{
    private const string ResourceType = "EXTERNAL_APPOINTMENT";

    /// <summary>El residente <c>{R}</c> está en el ámbito del actor: el ámbito activo es suyo y del perfil que dice, y Administración ve todo el centro; Enfermería y Medicina, los residentes
    /// con ubicación vigente en una unidad concedida (y, si el ámbito está restringido a ciertos residentes, solo esos).</summary>
    private const string ScopeSql = """
        EXISTS (
            SELECT 1 FROM dbo.ambitos_perfil scope_profile
             WHERE scope_profile.id = @ProfileScopeId AND scope_profile.centro_id = @CenterId AND scope_profile.cuenta_id = @AccountId
               AND scope_profile.perfil_codigo = @Profile AND scope_profile.estado = 'ACTIVE' AND scope_profile.revocado_en IS NULL
               AND (scope_profile.perfil_codigo = 'ADMINISTRACION' OR (
                    EXISTS (
                        SELECT 1 FROM dbo.ambitos_perfil_unidad scope_unit
                          JOIN dbo.intervalos_ubicacion_residente scope_location ON scope_location.residente_id = {R} AND scope_location.centro_id = scope_profile.centro_id
                               AND scope_location.unidad_id = scope_unit.unidad_id AND scope_location.vigente_hasta IS NULL
                         WHERE scope_unit.ambito_perfil_id = scope_profile.id AND scope_unit.centro_id = scope_profile.centro_id AND scope_unit.revocado_en IS NULL)
                    AND (EXISTS (
                            SELECT 1 FROM dbo.ambitos_perfil_residente scope_resident
                             WHERE scope_resident.ambito_perfil_id = scope_profile.id AND scope_resident.centro_id = scope_profile.centro_id
                               AND scope_resident.residente_id = {R} AND scope_resident.revocado_en IS NULL)
                         OR NOT EXISTS (
                            SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                             WHERE restriction.ambito_perfil_id = scope_profile.id AND restriction.centro_id = scope_profile.centro_id)))))
        """;

    private static readonly string AppointmentScopeSql = ScopeSql.Replace("{R}", "e.residente_id");

    private const string SelectSql = """
        SELECT e.id AS Id, e.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, location.unidad_id AS UnitId, unit.nombre_visible AS UnitName, e.fecha AS Date, e.hora AS Time,
               e.tipo_codigo AS Type, e.especialidad_codigo AS Specialty, e.lugar AS Place,
               e.transporte_modo_codigo AS TransportMode, e.transporte_estado_codigo AS TransportState,
               e.acompanante_modo_codigo AS CompanionMode, e.acompanante_estado_codigo AS CompanionState,
               e.preparar_ayuno AS Fasting, e.preparar_informe AS LastReport, e.preparar_tarjeta AS HealthCard, e.preparar_medicacion AS Medication,
               e.preparar_silla_ruedas AS Wheelchair, e.preparar_otra AS PrepOther, e.preparar_texto AS PrepText,
               e.estado_codigo AS Status, e.visibilidad_familia_codigo AS Visibility, e.version AS Version,
               CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.suspensiones_residente suspension
                                       WHERE suspension.residente_id = e.residente_id AND suspension.centro_id = e.centro_id AND suspension.finalizada_en IS NULL)
                         THEN 1 ELSE 0 END AS BIT) AS Suspended
          FROM dbo.citas_externas e
          JOIN dbo.residentes resident ON resident.id = e.residente_id AND resident.centro_id = e.centro_id
          LEFT JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = e.residente_id AND location.centro_id = e.centro_id AND location.vigente_hasta IS NULL
          LEFT JOIN dbo.unidades unit ON unit.id = location.unidad_id AND unit.centro_id = e.centro_id
        """;

    public async Task<Guid> CreateAsync(
        ExternalAppointmentActor actor, ResidentId residentId, ExternalAppointment appointment, Guid? operationId = null, Guid? originAppointmentId = null, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireWriterAsync(connection, transaction, actor, ct);

        // Bloquea al residente activo y comprueba que está en el ámbito de quien registra.
        var unit = await connection.QuerySingleOrDefaultAsync<UnitRow?>(new CommandDefinition($"""
            SELECT (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.residente_id = resident.id AND location.centro_id = resident.centro_id AND location.vigente_hasta IS NULL) AS UnitId
              FROM dbo.residentes resident WITH (UPDLOCK, ROWLOCK)
             WHERE resident.id = @ResidentId AND resident.centro_id = @CenterId AND resident.estado = 'ACTIVE'
               AND {ScopeSql.Replace("{R}", "resident.id")}
            """, Params(actor, new { ResidentId = residentId.Value }), transaction, cancellationToken: ct));
        if (unit is null)
        {
            throw new AccessDeniedException();
        }

        // Reenvío del mismo formulario (script 0102): con el residente ya bloqueado, si esta cuenta ya registró esta operación, se devuelve la cita creada sin repetir nada (ni historial ni
        // auditoría). Si el identificador es de otro residente, error genérico: no se dice nada más.
        if (operationId is { } operation)
        {
            var existing = await connection.QuerySingleOrDefaultAsync<ExistingRow?>(new CommandDefinition("""
                SELECT id AS Id, residente_id AS ResidentId FROM dbo.citas_externas
                 WHERE centro_id = @CenterId AND creada_por_cuenta_id = @AccountId AND operacion_id = @OperationId
                """, Params(actor, new { OperationId = operation }), transaction, cancellationToken: ct));
            if (existing is not null)
            {
                return existing.ResidentId == residentId.Value ? existing.Id : throw new AccessDeniedException();
            }
        }

        // «La siguiente cita» (script 0103): la de origen es de este residente, está en el ámbito del actor y está realizada o no acudida. Si no, error genérico: no se dice si existe.
        if (originAppointmentId is { } origin)
        {
            var originStatus = await connection.ExecuteScalarAsync<string?>(new CommandDefinition($"""
                SELECT e.estado_codigo FROM dbo.citas_externas e
                 WHERE e.id = @OriginId AND e.centro_id = @CenterId AND e.residente_id = @ResidentId AND {AppointmentScopeSql}
                """, Params(actor, new { OriginId = origin, ResidentId = residentId.Value }), transaction, cancellationToken: ct));
            if (originStatus is null)
            {
                throw new AccessDeniedException();
            }
            if (originStatus is not ("REALIZADA" or "NO_ACUDIDA"))
            {
                throw new DomainValidationException(ExternalAppointment.InvalidCode);
            }
            // Una cita de origen solo tiene una siguiente viva (UX_cext_origen_viva): si otra persona ya la registró, no se crea otra y se le dice que la cita cambió.
            var alreadyHasNext = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM dbo.citas_externas WHERE cita_origen_id = @OriginId AND centro_id = @CenterId AND estado_codigo <> 'CANCELADA'
                """, Params(actor, new { OriginId = origin }), transaction, cancellationToken: ct));
            if (alreadyHasNext > 0)
            {
                throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
            }
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_externas
                (id, centro_id, residente_id, fecha, hora, tipo_codigo, especialidad_codigo, lugar, transporte_modo_codigo, transporte_estado_codigo,
                 acompanante_modo_codigo, acompanante_estado_codigo, preparar_ayuno, preparar_informe, preparar_tarjeta, preparar_medicacion, preparar_silla_ruedas,
                 preparar_otra, preparar_texto, estado_codigo, visibilidad_familia_codigo, version, creada_por_cuenta_id, creada_por_perfil, creada_en, modificada_en, operacion_id, cita_origen_id)
            VALUES
                (@Id, @CenterId, @ResidentId, @Date, @Time, @Type, @Specialty, @Place, @TransportMode, @TransportState,
                 @CompanionMode, @CompanionState, @Fasting, @LastReport, @HealthCard, @Medication, @Wheelchair,
                 @PrepOther, @PrepText, 'PROGRAMADA', @Visibility, 1, @AccountId, @Profile, @Now, @Now, @OperationId, @OriginId);
            """, Params(actor, Fields(appointment, new { Id = id, ResidentId = residentId.Value, Now = now.UtcDateTime, OperationId = operationId, OriginId = originAppointmentId })), transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // Un identificador de operación ya usado que esta consulta no puede ver (otro ámbito): el mismo error genérico, sin el texto de SQL, que trae los valores de la clave.
            throw new AccessDeniedException();
        }

        await AddChangeAsync(connection, transaction, actor, id, 1, "CREADA", null, appointment.Date, null, appointment.Time, null, "PROGRAMADA", null, null, now, ct);
        await AuditAsync(connection, transaction, actor, residentId.Value, unit.UnitId, id, "EXT_APPOINTMENT_CREATE", now, ct);
        transaction.Commit();
        return id;
    }

    public async Task<int> UpdateAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointment appointment,
        ExternalAppointmentReasonCode? reasonCode, string? reasonLine, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireWriterAsync(connection, transaction, actor, ct);
        var current = await LockOpenAsync(connection, transaction, actor, appointmentId, expectedVersion, ct);

        var before = ToRecord(current);
        if (before.Appointment == appointment)
        {
            return expectedVersion; // Nada cambia: ni versión ni historial.
        }

        var reschedules = ExternalAppointmentRules.IsReschedule(before.Appointment, appointment);
        if (reschedules && reasonCode is null)
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        var now = DateTimeOffset.UtcNow;
        var version = expectedVersion + 1;
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_externas
               SET fecha = @Date, hora = @Time, tipo_codigo = @Type, especialidad_codigo = @Specialty, lugar = @Place,
                   transporte_modo_codigo = @TransportMode, transporte_estado_codigo = @TransportState,
                   acompanante_modo_codigo = @CompanionMode, acompanante_estado_codigo = @CompanionState,
                   preparar_ayuno = @Fasting, preparar_informe = @LastReport, preparar_tarjeta = @HealthCard, preparar_medicacion = @Medication,
                   preparar_silla_ruedas = @Wheelchair, preparar_otra = @PrepOther, preparar_texto = @PrepText,
                   visibilidad_familia_codigo = @Visibility, version = @NewVersion, modificada_en = @Now
             WHERE id = @Id AND centro_id = @CenterId AND version = @Version
            """, Params(actor, Fields(appointment, new { Id = appointmentId, Version = expectedVersion, NewVersion = version, Now = now.UtcDateTime })),
            transaction, cancellationToken: ct));
        if (updated != 1)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        await AddChangeAsync(connection, transaction, actor, appointmentId, await NextNumberAsync(connection, transaction, actor, appointmentId, ct),
            reschedules ? "REPROGRAMADA" : "MODIFICADA", before.Appointment.Date, appointment.Date, before.Appointment.Time, appointment.Time, null, null,
            reschedules ? reasonCode : null, reschedules ? reasonLine : null, now, ct);
        await AuditAsync(connection, transaction, actor, before.ResidentId.Value, before.UnitId?.Value, appointmentId,
            reschedules ? "EXT_APPOINTMENT_RESCHEDULE" : "EXT_APPOINTMENT_UPDATE", now, ct);
        if (before.Appointment.FamilyVisibility != appointment.FamilyVisibility)
        {
            await AuditAsync(connection, transaction, actor, before.ResidentId.Value, before.UnitId?.Value, appointmentId, "EXT_APPOINTMENT_VISIBILITY", now, ct);
        }
        transaction.Commit();
        return version;
    }

    public async Task<int> ChangeStatusAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointmentStatus newStatus, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireWriterAsync(connection, transaction, actor, ct);
        var current = await LockOpenAsync(connection, transaction, actor, appointmentId, expectedVersion, ct);
        var before = ToRecord(current);
        if (newStatus == ExternalAppointmentStatus.Cancelada || !ExternalAppointmentRules.CanTransition(before.Status, newStatus, before.Appointment.Date, ResidApp.Domain.Modules.CenterModuleRules.TodayInMadrid(DateTimeOffset.UtcNow)))
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        var now = DateTimeOffset.UtcNow;
        var version = await SetStatusAsync(connection, transaction, actor, appointmentId, expectedVersion, newStatus, now, ct);
        await AddChangeAsync(connection, transaction, actor, appointmentId, await NextNumberAsync(connection, transaction, actor, appointmentId, ct), "ESTADO_CAMBIADO",
            null, null, null, null, before.Status.ToCode(), newStatus.ToCode(), null, null, now, ct);
        await AuditAsync(connection, transaction, actor, before.ResidentId.Value, before.UnitId?.Value, appointmentId, "EXT_APPOINTMENT_STATUS", now, ct);
        transaction.Commit();
        return version;
    }

    public async Task<int> CancelAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointmentReasonCode reasonCode, string? reasonLine, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireWriterAsync(connection, transaction, actor, ct);
        var current = await LockOpenAsync(connection, transaction, actor, appointmentId, expectedVersion, ct);
        var before = ToRecord(current);

        var now = DateTimeOffset.UtcNow;
        var version = await SetStatusAsync(connection, transaction, actor, appointmentId, expectedVersion, ExternalAppointmentStatus.Cancelada, now, ct);
        await AddChangeAsync(connection, transaction, actor, appointmentId, await NextNumberAsync(connection, transaction, actor, appointmentId, ct), "CANCELADA",
            null, null, null, null, before.Status.ToCode(), ExternalAppointmentStatus.Cancelada.ToCode(), reasonCode, reasonLine, now, ct);
        await AuditAsync(connection, transaction, actor, before.ResidentId.Value, before.UnitId?.Value, appointmentId, "EXT_APPOINTMENT_CANCEL", now, ct);
        transaction.Commit();
        return version;
    }

    public async Task<ExternalAppointmentRecord?> FindAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition($"""
            {SelectSql}
             WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}
            """, Params(actor, new { Id = appointmentId }), cancellationToken: ct));
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ExternalAppointmentRecord>> ListAsync(ExternalAppointmentActor actor, DateOnly from, DateOnly to, ResidentId? residentId = null, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition($"""
            {SelectSql}
             WHERE e.centro_id = @CenterId AND e.fecha BETWEEN @From AND @To AND (@ResidentId IS NULL OR e.residente_id = @ResidentId) AND {AppointmentScopeSql}
             ORDER BY e.fecha, CASE WHEN e.hora IS NULL THEN 1 ELSE 0 END, e.hora, e.creada_en
            """, Params(actor, new { From = from, To = to, ResidentId = residentId?.Value }), cancellationToken: ct));
        return rows.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<ExternalAppointmentForAuxiliar>> ListForAuxiliarAsync(ExternalAppointmentActor actor, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Solo las columnas que el Auxiliar puede ver: no se lee ni el tipo, ni la especialidad, ni el lugar. Mismo criterio de residente asignado, unidad concedida y activa que «Mis residentes»; un residente suspendido por un ingreso hospitalario no sale (decisión de Orlando, 2026-11-18).
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<AuxiliarRow>(new CommandDefinition("""
            SELECT e.id AS Id, e.residente_id AS ResidentId, resident.nombre_visible AS ResidentName, e.fecha AS Date, e.hora AS Time,
                   e.transporte_modo_codigo AS TransportMode, e.acompanante_modo_codigo AS CompanionMode,
                   e.preparar_ayuno AS Fasting, e.preparar_informe AS LastReport, e.preparar_tarjeta AS HealthCard, e.preparar_medicacion AS Medication,
                   e.preparar_silla_ruedas AS Wheelchair, e.preparar_otra AS PrepOther, e.preparar_texto AS PrepText
              FROM dbo.citas_externas e
              JOIN dbo.residentes resident ON resident.id = e.residente_id AND resident.centro_id = e.centro_id AND resident.estado = 'ACTIVE'
             WHERE e.centro_id = @CenterId AND e.fecha BETWEEN @From AND @To AND e.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL')
               AND NOT EXISTS (SELECT 1 FROM dbo.suspensiones_residente suspension WHERE suspension.residente_id = e.residente_id AND suspension.centro_id = e.centro_id AND suspension.finalizada_en IS NULL)
               AND EXISTS (
                   SELECT 1 FROM dbo.ambitos_perfil scope_profile
                     JOIN dbo.ambitos_perfil_unidad scope_unit ON scope_unit.ambito_perfil_id = scope_profile.id AND scope_unit.centro_id = scope_profile.centro_id AND scope_unit.revocado_en IS NULL
                     JOIN dbo.unidades active_unit ON active_unit.id = scope_unit.unidad_id AND active_unit.centro_id = scope_profile.centro_id AND active_unit.estado = 'ACTIVE'
                     JOIN dbo.ambitos_perfil_residente scope_resident ON scope_resident.ambito_perfil_id = scope_profile.id AND scope_resident.centro_id = scope_profile.centro_id
                          AND scope_resident.residente_id = e.residente_id AND scope_resident.revocado_en IS NULL
                     JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = e.residente_id AND location.centro_id = scope_profile.centro_id
                          AND location.unidad_id = scope_unit.unidad_id AND location.vigente_hasta IS NULL
                    WHERE scope_profile.id = @ProfileScopeId AND scope_profile.centro_id = @CenterId AND scope_profile.cuenta_id = @AccountId
                      AND scope_profile.perfil_codigo = 'AUXILIAR' AND scope_profile.estado = 'ACTIVE' AND scope_profile.revocado_en IS NULL)
             ORDER BY e.fecha, CASE WHEN e.hora IS NULL THEN 1 ELSE 0 END, e.hora, resident.nombre_visible
            """, Params(actor, new { From = from, To = to }), cancellationToken: ct));
        return rows.Select(row => new ExternalAppointmentForAuxiliar(
            row.Id, ResidentId.From(row.ResidentId), row.ResidentName, row.Date, row.Time is { } time ? TimeOnly.FromTimeSpan(time) : null,
            new ExternalPreparation(row.Fasting, row.LastReport, row.HealthCard, row.Medication, row.Wheelchair, row.PrepOther, row.PrepText),
            EnumCode.ParseCode<ExternalTransportMode>(row.TransportMode), EnumCode.ParseCode<ExternalCompanionMode>(row.CompanionMode))).ToList();
    }

    public async Task<ExternalAppointmentFollowUp?> FindFollowUpAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default)
    {
        // Solo dos datos de la nota: la siguiente cita prevista de la versión vigente (si no está retirada) y la siguiente cita ya registrada. Ni el resultado, ni la línea, ni el autor, ni el número.
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<FollowUpRow>(new CommandDefinition($"""
            SELECT
                   (SELECT TOP (1) CASE WHEN n.retirada = 0 THEN n.siguiente_fecha END FROM dbo.citas_externas_notas n WHERE n.cita_id = e.id AND n.centro_id = e.centro_id ORDER BY n.numero DESC) AS PlannedDate,
                   (SELECT TOP (1) CASE WHEN n.retirada = 0 THEN n.siguiente_meses END FROM dbo.citas_externas_notas n WHERE n.cita_id = e.id AND n.centro_id = e.centro_id ORDER BY n.numero DESC) AS PlannedMonths,
                   (SELECT TOP (1) following.id FROM dbo.citas_externas following WHERE following.cita_origen_id = e.id AND following.centro_id = e.centro_id AND following.estado_codigo <> 'CANCELADA' ORDER BY following.creada_en) AS RegisteredId,
                   (SELECT TOP (1) following.fecha FROM dbo.citas_externas following WHERE following.cita_origen_id = e.id AND following.centro_id = e.centro_id AND following.estado_codigo <> 'CANCELADA' ORDER BY following.creada_en) AS RegisteredDate
              FROM dbo.citas_externas e
             WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}
            """, Params(actor, new { Id = appointmentId }), cancellationToken: ct));
        return row is null ? null : new ExternalAppointmentFollowUp(new ExternalNextVisit(ToDate(row.PlannedDate), row.PlannedMonths), row.RegisteredId, ToDate(row.RegisteredDate));
    }

    public async Task<ExternalNoteVersion?> FindCurrentNoteAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default)
    {
        RequireClinicalReader(actor);
        using var connection = await connections.OpenAsync(ct);
        // Una cita fuera del ámbito del actor (otra unidad, otro centro, residente dado de baja) no existe para él: acceso denegado, no «sin nota».
        var visible = await connection.ExecuteScalarAsync<int?>(new CommandDefinition($"""
            SELECT 1 FROM dbo.citas_externas e WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}
            """, Params(actor, new { Id = appointmentId }), cancellationToken: ct));
        if (visible is null)
        {
            throw new AccessDeniedException();
        }

        var rows = await connection.QueryAsync<NoteRow>(new CommandDefinition($"""
            {NoteSelectSql}
             WHERE n.cita_id = @Id AND n.centro_id = @CenterId
             ORDER BY n.numero DESC
            """, Params(actor, new { Id = appointmentId }), cancellationToken: ct));
        return rows.Select(ToNote).FirstOrDefault();
    }

    public async Task<IReadOnlyList<ExternalNoteVersion>> ListNoteVersionsAsync(ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct = default)
    {
        RequireClinicalReader(actor);
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var appointment = await connection.QuerySingleOrDefaultAsync<NoteAppointmentRow>(new CommandDefinition($"""
            SELECT e.residente_id AS ResidentId, e.fecha AS Date, e.estado_codigo AS Status,
                   (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.residente_id = e.residente_id AND location.centro_id = e.centro_id AND location.vigente_hasta IS NULL) AS UnitId
              FROM dbo.citas_externas e
             WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}
            """, Params(actor, new { Id = appointmentId }), transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();

        // Abrir las versiones anteriores deja un evento por apertura, sin contenido (DPO, segunda lectura, apartado c).
        await AuditAsync(connection, transaction, actor, appointment.ResidentId, appointment.UnitId, appointmentId, "EXT_APPOINTMENT_NOTE_HIST_READ", DateTimeOffset.UtcNow, ct);
        var rows = await connection.QueryAsync<NoteRow>(new CommandDefinition($"""
            {NoteSelectSql}
             WHERE n.cita_id = @Id AND n.centro_id = @CenterId
             ORDER BY n.numero DESC
            """, Params(actor, new { Id = appointmentId }), transaction, cancellationToken: ct));
        var versions = rows.Select(ToNote).ToList();
        transaction.Commit();
        return versions;
    }

    public Task<int> SaveNoteAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedNumber, ExternalAppointmentNote note, Guid? operationId = null, CancellationToken ct = default) =>
        WriteNoteAsync(actor, appointmentId, expectedNumber, note, null, operationId, ct);

    public Task<int> RetractNoteAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedNumber, ExternalNoteRetractionReason reason, Guid? operationId = null, CancellationToken ct = default) =>
        WriteNoteAsync(actor, appointmentId, expectedNumber, null, reason, operationId, ct);

    /// <summary>Escribe una versión de la nota (nueva o de retirada) en una transacción que bloquea la cita, comprueba el ámbito (la unidad actual del residente), que el residente sigue activo y que la
    /// cita está realizada, comprueba la versión vista y deja el evento de auditoría con solo identificadores (acción, cita y versión; ni el resultado, ni la línea, ni el motivo).</summary>
    private async Task<int> WriteNoteAsync(
        ExternalAppointmentActor actor, Guid appointmentId, int expectedNumber, ExternalAppointmentNote? note, ExternalNoteRetractionReason? retraction, Guid? operationId, CancellationToken ct)
    {
        if (actor.Profile != SystemProfile.Enfermeria)
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var appointment = await connection.QuerySingleOrDefaultAsync<NoteAppointmentRow>(new CommandDefinition($"""
            SELECT e.residente_id AS ResidentId, e.fecha AS Date, e.estado_codigo AS Status,
                   (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.residente_id = e.residente_id AND location.centro_id = e.centro_id AND location.vigente_hasta IS NULL) AS UnitId
              FROM dbo.citas_externas e WITH (UPDLOCK, ROWLOCK)
              JOIN dbo.residentes resident ON resident.id = e.residente_id AND resident.centro_id = e.centro_id AND resident.estado = 'ACTIVE'
             WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}
            """, Params(actor, new { Id = appointmentId }), transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();
        if (appointment.Status != "REALIZADA")
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }
        note?.RequireNextAfter(appointment.Date);

        // Reenvío del mismo formulario: si esta cuenta ya guardó esta operación, se devuelve su número sin repetir versión ni auditoría.
        if (operationId is { } operation)
        {
            var existing = await connection.QuerySingleOrDefaultAsync<ExistingNoteRow?>(new CommandDefinition("""
                SELECT cita_id AS AppointmentId, numero AS Number FROM dbo.citas_externas_notas
                 WHERE centro_id = @CenterId AND actor_cuenta_id = @AccountId AND operacion_id = @OperationId
                """, Params(actor, new { OperationId = operation }), transaction, cancellationToken: ct));
            if (existing is not null)
            {
                return existing.AppointmentId == appointmentId ? existing.Number : throw new AccessDeniedException();
            }
        }

        var latest = await connection.QuerySingleOrDefaultAsync<LatestNoteRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, retirada AS Retracted FROM dbo.citas_externas_notas
             WHERE cita_id = @Id AND centro_id = @CenterId ORDER BY numero DESC
            """, Params(actor, new { Id = appointmentId }), transaction, cancellationToken: ct));
        if ((latest?.Number ?? 0) != expectedNumber)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }
        if (retraction is not null && (latest is null || latest.Retracted))
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode); // No hay nota vigente que retirar.
        }

        var versionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.citas_externas_notas
                    (id, centro_id, cita_id, residente_id, numero, retirada, resultado_codigo, texto, siguiente_fecha, siguiente_meses, motivo_retirada_codigo,
                     actor_cuenta_id, actor_perfil_codigo, operacion_id, ocurrido_en)
                VALUES
                    (@VersionId, @CenterId, @Id, @ResidentId, @Number, @Retracted, @Result, @Text, @NextDate, @NextMonths, @Reason,
                     @AccountId, @Profile, @OperationId, @Now);
                """, Params(actor, new
            {
                VersionId = versionId, Id = appointmentId, appointment.ResidentId, Number = expectedNumber + 1, Retracted = retraction is not null,
                Result = note?.Result.ToCode(), Text = note?.Text, NextDate = note?.NextDate, NextMonths = note?.NextMonths, Reason = retraction?.ToCode(),
                OperationId = operationId, Now = now.UtcDateTime,
            }), transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new AccessDeniedException();
        }

        await AuditAsync(connection, transaction, actor, appointment.ResidentId, appointment.UnitId, versionId,
            retraction is null ? "EXT_APPOINTMENT_NOTE" : "EXT_APPOINTMENT_NOTE_RETRACT", now, ct, "EXTERNAL_APPOINTMENT_NOTE");
        transaction.Commit();
        return expectedNumber + 1;
    }

    /// <summary>La nota la leen Enfermería y Medicina; Administración, el Auxiliar y el resto, no (Llei 21/2000, art. 11.4).</summary>
    private static void RequireClinicalReader(ExternalAppointmentActor actor)
    {
        if (actor.Profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            throw new AccessDeniedException();
        }
    }

    private const string NoteSelectSql = """
        SELECT n.id AS Id, n.numero AS Number, n.retirada AS Retracted, n.resultado_codigo AS Result, n.texto AS Text, n.siguiente_fecha AS NextDate, n.siguiente_meses AS NextMonths,
               n.motivo_retirada_codigo AS Reason, n.ocurrido_en AS At, ISNULL(author.nombre_visible, N'Enfermería') AS AuthorName
          FROM dbo.citas_externas_notas n
          LEFT JOIN dbo.cuentas author ON author.id = n.actor_cuenta_id
        """;

    private static ExternalNoteVersion ToNote(NoteRow row) => new(
        row.Id, row.Number, row.Retracted, row.Result is { } result ? EnumCode.ParseCode<ExternalNoteResult>(result) : null, row.Text, ToDate(row.NextDate), row.NextMonths,
        row.Reason is { } reason ? EnumCode.ParseCode<ExternalNoteRetractionReason>(reason) : null, new DateTimeOffset(DateTime.SpecifyKind(row.At, DateTimeKind.Utc)), row.AuthorName);

    private static DateOnly? ToDate(DateTime? value) => value is { } date ? DateOnly.FromDateTime(date) : null;

    private sealed record FollowUpRow(DateTime? PlannedDate, byte? PlannedMonths, Guid? RegisteredId, DateTime? RegisteredDate);

    private sealed record NoteRow(
        Guid Id, int Number, bool Retracted, string? Result, string? Text, DateTime? NextDate, byte? NextMonths, string? Reason, DateTime At, string AuthorName);

    private sealed record NoteAppointmentRow(Guid ResidentId, DateOnly Date, string Status, Guid? UnitId);

    private sealed record ExistingNoteRow(Guid AppointmentId, int Number);

    private sealed record LatestNoteRow(int Number, bool Retracted);

    /// <summary>Quien escribe: Administración (su ámbito se comprueba en cada consulta) o Enfermería con el permiso EXTERNAL_APPOINTMENT_MANAGE vigente, vuelto a mirar dentro de la
    /// transacción por si Administración se lo retiró después de la comprobación del caso de uso. Medicina y el resto, no.</summary>
    private static async Task RequireWriterAsync(SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, CancellationToken ct)
    {
        if (actor.Profile == SystemProfile.Administracion)
        {
            return;
        }

        var allowed = actor.Profile == SystemProfile.Enfermeria && await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.permisos_perfil permission
             WHERE permission.ambito_perfil_id = @ProfileScopeId AND permission.centro_id = @CenterId
               AND permission.permiso_codigo = @Permission AND permission.revocado_en IS NULL
            """, new { actor.ProfileScopeId, CenterId = actor.CenterId.Value, Permission = ProfilePermissions.ExternalAppointmentManage },
            transaction, cancellationToken: ct)) > 0;
        if (!allowed)
        {
            throw new AccessDeniedException();
        }
    }

    /// <summary>Bloquea la cita, comprueba que está en el ámbito del actor y que su residente sigue activo, y que no está cerrada ni la versión vista es otra.</summary>
    private static async Task<Row> LockOpenAsync(
        SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, CancellationToken ct)
    {
        var locked = await connection.ExecuteScalarAsync<int?>(new CommandDefinition($"""
            SELECT 1 FROM dbo.citas_externas e WITH (UPDLOCK, ROWLOCK)
              JOIN dbo.residentes resident ON resident.id = e.residente_id AND resident.centro_id = e.centro_id AND resident.estado = 'ACTIVE'
             WHERE e.id = @Id AND e.centro_id = @CenterId AND {AppointmentScopeSql}
            """, Params(actor, new { Id = appointmentId }), transaction, cancellationToken: ct));
        if (locked is null)
        {
            throw new AccessDeniedException();
        }

        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition($"""
            {SelectSql}
             WHERE e.id = @Id AND e.centro_id = @CenterId
            """, Params(actor, new { Id = appointmentId }), transaction, cancellationToken: ct));
        if (!ExternalAppointmentRules.IsOpen(EnumCode.ParseCode<ExternalAppointmentStatus>(row.Status)))
        {
            throw new DomainValidationException(ExternalAppointment.ClosedCode);
        }
        if (row.Version != expectedVersion)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }
        return row;
    }

    private static async Task<int> SetStatusAsync(
        SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, Guid appointmentId, int expectedVersion, ExternalAppointmentStatus status,
        DateTimeOffset now, CancellationToken ct)
    {
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.citas_externas SET estado_codigo = @Status, version = @NewVersion, modificada_en = @Now
             WHERE id = @Id AND centro_id = @CenterId AND version = @Version
            """, new
        {
            Status = status.ToCode(), NewVersion = expectedVersion + 1, Now = now.UtcDateTime, Id = appointmentId, CenterId = actor.CenterId.Value, Version = expectedVersion,
        }, transaction, cancellationToken: ct));
        return updated == 1 ? expectedVersion + 1 : throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
    }

    private static Task<int> NextNumberAsync(SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, Guid appointmentId, CancellationToken ct) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT ISNULL(MAX(numero), 0) + 1 FROM dbo.citas_externas_cambios WITH (FORCESEEK) WHERE cita_id = @Id AND centro_id = @CenterId
            """, new { Id = appointmentId, CenterId = actor.CenterId.Value }, transaction, cancellationToken: ct));

    private static Task AddChangeAsync(
        SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, Guid appointmentId, int number, string action,
        DateOnly? dateBefore, DateOnly? dateAfter, TimeOnly? timeBefore, TimeOnly? timeAfter, string? statusBefore, string? statusAfter,
        object? reasonCode, string? reasonLine, DateTimeOffset now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_externas_cambios
                (id, centro_id, cita_id, numero, accion_codigo, actor_cuenta_id, actor_perfil_codigo, fecha_anterior, fecha_nueva, hora_anterior, hora_nueva,
                 estado_anterior_codigo, estado_nuevo_codigo, motivo_codigo, motivo, ocurrido_en)
            VALUES
                (NEWID(), @CenterId, @AppointmentId, @Number, @Action, @AccountId, @Profile, @DateBefore, @DateAfter, @TimeBefore, @TimeAfter,
                 @StatusBefore, @StatusAfter, @ReasonCode, @ReasonLine, @Now);
            """, new
        {
            CenterId = actor.CenterId.Value, AppointmentId = appointmentId, Number = number, Action = action, AccountId = actor.AccountId.Value, Profile = actor.Profile.ToCode(),
            DateBefore = dateBefore, DateAfter = dateAfter, TimeBefore = timeBefore?.ToTimeSpan(), TimeAfter = timeAfter?.ToTimeSpan(),
            StatusBefore = statusBefore, StatusAfter = statusAfter,
            ReasonCode = reasonCode switch { ExternalAppointmentReasonCode code => code.ToCode(), _ => null }, ReasonLine = reasonLine, Now = now.UtcDateTime,
        }, transaction, cancellationToken: ct));

    /// <summary>La auditoría lleva solo identificadores: cuenta, perfil, centro, unidad, residente, tipo y número de recurso, acción y hora. Nada del contenido de la cita.</summary>
    private static Task AuditAsync(
        SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, Guid residentId, Guid? unitId, Guid appointmentId, string action,
        DateTimeOffset now, CancellationToken ct, string? resourceType = null) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, @ResourceType, @AppointmentId, @Action, @Now);
            """, new
        {
            AccountId = actor.AccountId.Value, Profile = actor.Profile.ToCode(), CenterId = actor.CenterId.Value, UnitId = unitId, ResidentId = residentId,
            ResourceType = resourceType ?? ResourceType, AppointmentId = appointmentId, Action = action, Now = now.UtcDateTime,
        }, transaction, cancellationToken: ct));

    private static DynamicParameters Params(ExternalAppointmentActor actor, object extra)
    {
        var parameters = new DynamicParameters(extra);
        parameters.Add("ProfileScopeId", actor.ProfileScopeId);
        parameters.Add("CenterId", actor.CenterId.Value);
        parameters.Add("AccountId", actor.AccountId.Value);
        parameters.Add("Profile", actor.Profile.ToCode());
        return parameters;
    }

    private static object Fields(ExternalAppointment a, object extra)
    {
        var parameters = new DynamicParameters(extra);
        parameters.AddDynamicParams(new
        {
            Date = a.Date, Time = a.Time?.ToTimeSpan(), Type = a.Type.ToCode(), Specialty = a.Specialty?.ToCode(), Place = a.Place,
            TransportMode = a.TransportMode.ToCode(), TransportState = a.TransportState.ToCode(),
            CompanionMode = a.CompanionMode.ToCode(), CompanionState = a.CompanionState.ToCode(),
            Fasting = a.Preparation.Fasting, LastReport = a.Preparation.LastReport, HealthCard = a.Preparation.HealthCard, Medication = a.Preparation.Medication,
            Wheelchair = a.Preparation.Wheelchair, PrepOther = a.Preparation.Other, PrepText = a.Preparation.Text, Visibility = a.FamilyVisibility.ToCode(),
        });
        return parameters;
    }

    private static ExternalAppointmentRecord ToRecord(Row row) => new(
        row.Id, ResidentId.From(row.ResidentId), row.ResidentName, row.UnitId is { } unit ? UnitId.From(unit) : null,
        new ExternalAppointment(
            row.Date, row.Time is { } time ? TimeOnly.FromTimeSpan(time) : null,
            EnumCode.ParseCode<ExternalAppointmentType>(row.Type), row.Specialty is { } specialty ? EnumCode.ParseCode<ExternalAppointmentSpecialty>(specialty) : null, row.Place,
            EnumCode.ParseCode<ExternalTransportMode>(row.TransportMode), EnumCode.ParseCode<ExternalArrangementState>(row.TransportState),
            EnumCode.ParseCode<ExternalCompanionMode>(row.CompanionMode), EnumCode.ParseCode<ExternalArrangementState>(row.CompanionState),
            new ExternalPreparation(row.Fasting, row.LastReport, row.HealthCard, row.Medication, row.Wheelchair, row.PrepOther, row.PrepText),
            EnumCode.ParseCode<ExternalFamilyVisibility>(row.Visibility)),
        EnumCode.ParseCode<ExternalAppointmentStatus>(row.Status), row.Version, row.Suspended, row.UnitName);

    private sealed record UnitRow(Guid? UnitId);

    private sealed record AuxiliarRow(
        Guid Id, Guid ResidentId, string ResidentName, DateOnly Date, TimeSpan? Time, string TransportMode, string CompanionMode,
        bool Fasting, bool LastReport, bool HealthCard, bool Medication, bool Wheelchair, bool PrepOther, string? PrepText);

    private sealed record ExistingRow(Guid Id, Guid ResidentId);

    private sealed record Row(
        Guid Id, Guid ResidentId, string ResidentName, Guid? UnitId, string? UnitName, DateOnly Date, TimeSpan? Time, string Type, string? Specialty, string? Place,
        string TransportMode, string TransportState, string CompanionMode, string CompanionState,
        bool Fasting, bool LastReport, bool HealthCard, bool Medication, bool Wheelchair, bool PrepOther, string? PrepText,
        string Status, string Visibility, int Version, bool Suspended);
}
