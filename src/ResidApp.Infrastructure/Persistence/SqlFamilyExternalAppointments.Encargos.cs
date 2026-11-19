using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// «Me encargo yo», «Ya no puedo», «No puedo» y deshacer «No puedo» (PR 6; script 0105). Cada acción va en una transacción que vuelve a comprobar el acceso del portal y la regla de visibilidad
/// del PR 5 (no otra copia), bloquea la cita y escribe una fila de solo añadir. La asignación es atómica: el índice único (cita, ámbito, número) hace que de dos pulsaciones a la vez gane una y la
/// otra reciba «ya lo tiene otra persona». Quien lo tiene ahora se calcula al leer (<c>dbo.fn_citas_externas_encargos_vigentes</c>): si la cita se reprogramó, cambió el modo o quien lo tenía perdió el
/// acceso, ya no cuenta, y antes de una nueva toma se anota su suelta para que la secuencia siga siendo válida. La fila guarda el vínculo, nunca un nombre ni la cuenta; la auditoría, solo identificadores.
/// </summary>
public sealed partial class SqlFamilyExternalAppointments
{
    /// <summary>La cita para actuar: solo la que la familia ve (no oculta, abierta o cancelada, dentro de los 90 días), con la fila bloqueada.</summary>
    private const string ArrangeAppointmentSql = """
        SELECT e.estado_codigo AS Status, e.fecha AS Date, e.transporte_modo_codigo AS TransportMode, e.transporte_estado_codigo AS TransportState,
               e.acompanante_modo_codigo AS CompanionMode, e.acompanante_estado_codigo AS CompanionState
          FROM dbo.citas_externas e WITH (UPDLOCK, ROWLOCK)
         WHERE e.id = @AppointmentId AND e.centro_id = @CenterId AND e.residente_id = @ResidentId AND e.visibilidad_familia_codigo <> 'OCULTA'
           AND e.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL', 'CANCELADA') AND e.fecha BETWEEN @From AND @To
        """;

    public async Task<FamilyArrangementOutcome> ArrangeAsync(
        FamilyPortalAccess access, Guid linkId, Guid appointmentId, ExternalArrangementScope scope, FamilyArrangementAction action, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var link = await SqlFamilyAppointments.FindLinkAsync(connection, transaction, access, linkId, todayAtCenter, ct);
        if (link is null || !await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                $"SELECT CASE WHEN {Gate} THEN 1 ELSE 0 END", new { CenterId = access.CenterId.Value, link.ResidentId }, transaction, cancellationToken: ct)))
        {
            return FamilyArrangementOutcome.NotFound;
        }

        var bounds = new DynamicParameters(Parameters(access, link.ResidentId, todayAtCenter));
        bounds.Add("AppointmentId", appointmentId);
        var appointment = await connection.QuerySingleOrDefaultAsync<ArrangeRow>(new CommandDefinition(ArrangeAppointmentSql, bounds, transaction, cancellationToken: ct));
        if (appointment is null)
        {
            return FamilyArrangementOutcome.NotFound;
        }

        var now = DateTimeOffset.UtcNow;
        var open = appointment.Status != ExternalAppointmentStatus.Cancelada.ToCode();
        if (!open)
        {
            // Una cita cancelada se sigue viendo hasta su fecha, pero ya no admite nada.
            return FamilyArrangementOutcome.NotApplicable;
        }

        var applicable = open && (scope == ExternalArrangementScope.Transporte
            ? appointment.TransportMode == ExternalTransportMode.Familia.ToCode() && appointment.TransportState == ExternalArrangementState.Pendiente.ToCode()
            : appointment.CompanionMode == ExternalCompanionMode.Familiar.ToCode() && appointment.CompanionState == ExternalArrangementState.Pendiente.ToCode());
        var key = new Key(access.CenterId.Value, appointmentId, link.LinkId, link.ResidentId, scope.ToCode(), todayAtCenter.ToDateTime(TimeOnly.MinValue));

        try
        {
            return action is FamilyArrangementAction.Take or FamilyArrangementAction.Release
                ? await TakeOrReleaseAsync(connection, transaction, key, appointment, action, applicable, now, ct)
                : await RefuseAsync(connection, transaction, key, action == FamilyArrangementAction.Refuse, open, now, ct);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 50943 or 50948)
        {
            // Otra pulsación llegó antes a la misma fila (clave única) o la secuencia ya no cuadra: la tiene otra persona (o ya estaba hecho).
            return action == FamilyArrangementAction.Take ? FamilyArrangementOutcome.TakenByAnother : FamilyArrangementOutcome.NotApplicable;
        }
    }

    private static async Task<FamilyArrangementOutcome> TakeOrReleaseAsync(
        SqlConnection connection, SqlTransaction transaction, Key key, ArrangeRow appointment, FamilyArrangementAction action, bool applicable, DateTimeOffset now, CancellationToken ct)
    {
        var holder = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT vinculo_id FROM dbo.fn_citas_externas_encargos_vigentes(@Today) WHERE cita_id = @AppointmentId AND ambito_codigo = @Scope", key, transaction, cancellationToken: ct));
        var latest = await connection.QuerySingleOrDefaultAsync<ArrangementRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, accion_codigo AS Action, vinculo_id AS LinkId FROM dbo.citas_externas_encargos WITH (UPDLOCK, HOLDLOCK)
             WHERE cita_id = @AppointmentId AND ambito_codigo = @Scope ORDER BY numero DESC
            """, key, transaction, cancellationToken: ct));
        var number = latest?.Number ?? 0;

        if (action == FamilyArrangementAction.Release)
        {
            // Soltar lo que no se tiene no hace nada; lo de otra persona no se puede soltar.
            if (holder != key.LinkId)
            {
                return holder is null ? FamilyArrangementOutcome.Done : FamilyArrangementOutcome.NotApplicable;
            }

            if (!applicable)
            {
                return FamilyArrangementOutcome.NotApplicable;
            }

            var released = await AddArrangementAsync(connection, transaction, key, number + 1, "SOLTAR", "FAMILIAR", key.LinkId, appointment.Date, now, ct);
            await AuditAsync(connection, transaction, key, released, "EXT_APPOINTMENT_ARRANGE_RELEASE", now, ct);
            transaction.Commit();
            return FamilyArrangementOutcome.Done;
        }

        if (holder == key.LinkId)
        {
            return FamilyArrangementOutcome.Done;
        }

        if (holder is not null)
        {
            return FamilyArrangementOutcome.TakenByAnother;
        }

        if (!applicable)
        {
            return FamilyArrangementOutcome.NotApplicable;
        }

        // Quien la tenía antes ya no cuenta (reprogramada, cambió el modo o perdió el acceso): se anota su suelta para que la secuencia siga siendo válida, y entonces se toma.
        if (latest is { Action: "ME_ENCARGO" })
        {
            await AddArrangementAsync(connection, transaction, key, ++number, "LIBERADO_AUTOMATICO", "SISTEMA", latest.LinkId, appointment.Date, now, ct);
        }

        var taken = await AddArrangementAsync(connection, transaction, key, number + 1, "ME_ENCARGO", "FAMILIAR", key.LinkId, appointment.Date, now, ct);
        if (await IsRefusedAsync(connection, transaction, key, ct))
        {
            // Quien se encarga ya no está en «No puedo».
            await AddRefusalAsync(connection, transaction, key, false, now, ct);
        }

        await AuditAsync(connection, transaction, key, taken, "EXT_APPOINTMENT_ARRANGE_TAKE", now, ct);
        transaction.Commit();
        return FamilyArrangementOutcome.Done;
    }

    private static async Task<FamilyArrangementOutcome> RefuseAsync(
        SqlConnection connection, SqlTransaction transaction, Key key, bool refuse, bool open, DateTimeOffset now, CancellationToken ct)
    {
        if (!open)
        {
            return FamilyArrangementOutcome.NotApplicable;
        }

        if (await IsRefusedAsync(connection, transaction, key, ct) == refuse)
        {
            return FamilyArrangementOutcome.Done;
        }

        if (refuse && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.fn_citas_externas_encargos_vigentes(@Today) WHERE cita_id = @AppointmentId AND vinculo_id = @LinkId) THEN 1 ELSE 0 END",
                key, transaction, cancellationToken: ct)))
        {
            // Quien se ha encargado de algo no dice «No puedo»: suelta («Ya no puedo»).
            return FamilyArrangementOutcome.NotApplicable;
        }

        var id = await AddRefusalAsync(connection, transaction, key, refuse, now, ct);
        await AuditAsync(connection, transaction, key, id, refuse ? "EXT_APPOINTMENT_ARRANGE_REFUSE" : "EXT_APPOINTMENT_ARRANGE_UNDO", now, ct);
        transaction.Commit();
        return FamilyArrangementOutcome.Done;
    }

    private static async Task<bool> IsRefusedAsync(SqlConnection connection, SqlTransaction transaction, Key key, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<bool?>(new CommandDefinition("""
            SELECT TOP (1) no_puede FROM dbo.citas_externas_no_puedo WITH (UPDLOCK, HOLDLOCK) WHERE cita_id = @AppointmentId AND vinculo_id = @LinkId ORDER BY numero DESC
            """, key, transaction, cancellationToken: ct)) == true;

    private static async Task<Guid> AddRefusalAsync(SqlConnection connection, SqlTransaction transaction, Key key, bool refuse, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var parameters = new DynamicParameters(key);
        parameters.AddDynamicParams(new { Id = id, Value = refuse, Now = now.UtcDateTime });
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_externas_no_puedo (id, centro_id, cita_id, vinculo_id, numero, no_puede, cambiado_en)
            VALUES (@Id, @CenterId, @AppointmentId, @LinkId,
                    ISNULL((SELECT MAX(numero) FROM dbo.citas_externas_no_puedo WHERE cita_id = @AppointmentId AND vinculo_id = @LinkId), 0) + 1, @Value, @Now);
            """, parameters, transaction, cancellationToken: ct));
        return id;
    }

    private static async Task<Guid> AddArrangementAsync(
        SqlConnection connection, SqlTransaction transaction, Key key, int number, string action, string profile, Guid arrangementLinkId, DateTime date, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var parameters = new DynamicParameters(key);
        parameters.AddDynamicParams(new { Id = id, Number = number, Action = action, Profile = profile, ArrangementLinkId = arrangementLinkId, Date = date, Now = now.UtcDateTime });
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.citas_externas_encargos (id, centro_id, cita_id, ambito_codigo, numero, accion_codigo, vinculo_id, actor_perfil_codigo, fecha_cita, cambiado_en)
            VALUES (@Id, @CenterId, @AppointmentId, @Scope, @Number, @Action, @ArrangementLinkId, @Profile, @Date, @Now);
            """, parameters, transaction, cancellationToken: ct));
        return id;
    }

    /// <summary>Auditoría con solo identificadores: cuenta, perfil FAMILIAR, centro, unidad, residente, la fila de encargo o de «No puedo» como recurso y la acción. Ni la especialidad, ni el lugar, ni qué preparar.</summary>
    private static Task AuditAsync(SqlConnection connection, SqlTransaction transaction, Key key, Guid rowId, string action, DateTimeOffset now, CancellationToken ct)
    {
        var parameters = new DynamicParameters(key);
        parameters.AddDynamicParams(new { RowId = rowId, Action = action, Now = now.UtcDateTime });
        return connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            SELECT NEWID(), fam.cuenta_id, 'FAMILIAR', @CenterId,
                   (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.residente_id = @ResidentId AND location.centro_id = @CenterId AND location.vigente_hasta IS NULL),
                   @ResidentId, 'EXTERNAL_APPOINTMENT_ARRANGEMENT', @RowId, @Action, @Now
              FROM dbo.residentes_familiares link JOIN dbo.familiares fam ON fam.id = link.familiar_id AND fam.centro_id = link.centro_id
             WHERE link.id = @LinkId AND link.centro_id = @CenterId;
            """, parameters, transaction, cancellationToken: ct));
    }

    private sealed record Key(Guid CenterId, Guid AppointmentId, Guid LinkId, Guid ResidentId, string Scope, DateTime Today);

    private sealed record ArrangeRow(string Status, DateTime Date, string TransportMode, string TransportState, string CompanionMode, string CompanionState);

    private sealed record ArrangementRow(int Number, string Action, Guid LinkId);
}
