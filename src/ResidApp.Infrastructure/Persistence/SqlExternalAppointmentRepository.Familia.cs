using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Los dos interruptores de las citas externas para la familia (script 0104). Mismo patrón que el resto del repositorio: cada escritura va en una transacción que bloquea la fila vigente, vuelve a
/// comprobar el ámbito, comprueba el número visto (si otra persona cambió antes, conflicto en vez de pisarla), inserta la fila nueva (la tabla es de solo añadir) y deja el evento en
/// dbo.eventos_auditoria con solo identificadores: el valor anterior y el nuevo se leen de la fila, no se copian al evento. El interruptor del residente NO pide el permiso de gestionar citas:
/// es una herramienta de privacidad de Administración y de la Enfermería de su unidad.
/// </summary>
public sealed partial class SqlExternalAppointmentRepository : IExternalAppointmentFamilySettings
{
    private const string FamilyResourceType = "EXTERNAL_APPOINTMENT_FAMILY";

    /// <summary>El ámbito activo es de Administración de este centro.</summary>
    private const string AdministrationScopeSql = """
        SELECT 1 FROM dbo.ambitos_perfil scope_profile
         WHERE scope_profile.id = @ProfileScopeId AND scope_profile.centro_id = @CenterId AND scope_profile.cuenta_id = @AccountId
           AND scope_profile.perfil_codigo = 'ADMINISTRACION' AND scope_profile.estado = 'ACTIVE' AND scope_profile.revocado_en IS NULL
        """;

    /// <summary>Citas abiertas, no ocultas, de los próximos días, de residentes activos y no suspendidos: lo que las familias verían. Parámetros: @CenterId, @From, @To, @ResidentId (nulo = todos).
    /// {RESTRICTION} es la comprobación de restricción por residente (el recuento de un residente la omite: cuenta lo que se abriría).</summary>
    private const string FamilyCountSql = """
        SELECT COUNT(CASE WHEN e.visibilidad_familia_codigo = 'VISIBLE' THEN 1 END) AS Visible,
               COUNT(CASE WHEN e.visibilidad_familia_codigo = 'SIN_ESPECIALIDAD' THEN 1 END) AS WithoutSpecialty
          FROM dbo.citas_externas e
          JOIN dbo.residentes resident ON resident.id = e.residente_id AND resident.centro_id = e.centro_id AND resident.estado = 'ACTIVE'
         WHERE e.centro_id = @CenterId AND e.estado_codigo IN ('PROGRAMADA', 'CONFIRMADA_HOSPITAL') AND e.fecha BETWEEN @From AND @To
           AND e.visibilidad_familia_codigo <> 'OCULTA' AND (@ResidentId IS NULL OR e.residente_id = @ResidentId)
           AND NOT EXISTS (SELECT 1 FROM dbo.suspensiones_residente suspension
                            WHERE suspension.residente_id = e.residente_id AND suspension.centro_id = e.centro_id AND suspension.finalizada_en IS NULL)
           {RESTRICTION}
        """;

    public async Task<ExternalFamilyCenterState> ReadCenterAsync(ExternalAppointmentActor actor, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await RequireAdministrationAsync(connection, null, actor, ct);
        var latest = await LatestCenterAsync(connection, null, actor, ct);
        var count = await CountAsync(connection, actor, todayAtCenter, null, ignoreRestriction: false, ct);
        return new ExternalFamilyCenterState(latest?.Enabled ?? false, latest?.Number ?? 0, count);
    }

    public async Task<int> SetCenterAsync(
        ExternalAppointmentActor actor, int expectedNumber, bool enable, ExternalFamilyConfirmations confirmations, CancellationToken ct = default)
    {
        if (actor.Profile != SystemProfile.Administracion)
        {
            throw new AccessDeniedException();
        }

        if (enable && !confirmations.All)
        {
            throw new DomainValidationException(ExternalAppointment.InvalidCode);
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        await RequireAdministrationAsync(connection, transaction, actor, ct);
        var latest = await LatestCenterAsync(connection, transaction, actor, ct, lockRow: true);
        if ((latest?.Number ?? 0) != expectedNumber || (latest?.Enabled ?? false) == enable)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertOrConflictAsync(connection, transaction, """
            INSERT INTO dbo.citas_externas_familia_centro
                (id, centro_id, numero, familia_ve, confirma_informada, confirma_voluntad, confirma_eipd, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (@Id, @CenterId, @Number, @Enable, @Informed, @Will, @Impact, @AccountId, @Profile, @Now);
            """, Params(actor, new
        {
            Id = id, Number = expectedNumber + 1, Enable = enable, Informed = enable && confirmations.Informed, Will = enable && confirmations.ResidentWill,
            Impact = enable && confirmations.ImpactAssessment, Now = now.UtcDateTime,
        }), ct);
        await AuditFamilyAsync(connection, transaction, actor, null, null, id, enable ? "EXT_APPOINTMENT_FAMILY_ON" : "EXT_APPOINTMENT_FAMILY_OFF", now, ct);
        transaction.Commit();
        return expectedNumber + 1;
    }

    public async Task<ExternalFamilyResidentState> ReadResidentAsync(ExternalAppointmentActor actor, ResidentId residentId, DateOnly todayAtCenter, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var name = await connection.ExecuteScalarAsync<string?>(new CommandDefinition($"""
            SELECT resident.nombre_visible FROM dbo.residentes resident
             WHERE resident.id = @ResidentId AND resident.centro_id = @CenterId AND resident.estado = 'ACTIVE' AND {ScopeSql.Replace("{R}", "resident.id")}
            """, Params(actor, new { ResidentId = residentId.Value }), cancellationToken: ct)) ?? throw new AccessDeniedException();
        var latest = await LatestResidentAsync(connection, null, actor, residentId.Value, ct);
        var centerEnabled = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            $"SELECT CASE WHEN {ExternalFamilyVisibilitySql.CenterEnabled} THEN 1 ELSE 0 END", Params(actor, new { }), cancellationToken: ct));
        var count = await CountAsync(connection, actor, todayAtCenter, residentId.Value, ignoreRestriction: true, ct);
        return new ExternalFamilyResidentState(name, latest?.Restricted ?? false, latest?.Number ?? 0, centerEnabled, count);
    }

    public async Task<int> SetResidentAsync(ExternalAppointmentActor actor, ResidentId residentId, int expectedNumber, bool restrict, CancellationToken ct = default)
    {
        if (actor.Profile is not (SystemProfile.Administracion or SystemProfile.Enfermeria))
        {
            throw new AccessDeniedException();
        }

        using var connection = await connections.OpenAsync(ct);
        using var transaction = (SqlTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
        // Bloquea al residente activo y comprueba que está en el ámbito de quien cambia (en Enfermería, la unidad actual del residente).
        var unit = await connection.QuerySingleOrDefaultAsync<UnitRow?>(new CommandDefinition($"""
            SELECT (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                     WHERE location.residente_id = resident.id AND location.centro_id = resident.centro_id AND location.vigente_hasta IS NULL) AS UnitId
              FROM dbo.residentes resident WITH (UPDLOCK, ROWLOCK)
             WHERE resident.id = @ResidentId AND resident.centro_id = @CenterId AND resident.estado = 'ACTIVE'
               AND {ScopeSql.Replace("{R}", "resident.id")}
            """, Params(actor, new { ResidentId = residentId.Value }), transaction, cancellationToken: ct)) ?? throw new AccessDeniedException();
        var latest = await LatestResidentAsync(connection, transaction, actor, residentId.Value, ct);
        if ((latest?.Number ?? 0) != expectedNumber || (latest?.Restricted ?? false) == restrict)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await InsertOrConflictAsync(connection, transaction, """
            INSERT INTO dbo.citas_externas_familia_residente
                (id, centro_id, residente_id, numero, familia_no_ve, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Number, @Restrict, @AccountId, @Profile, @Now);
            """, Params(actor, new { Id = id, ResidentId = residentId.Value, Number = expectedNumber + 1, Restrict = restrict, Now = now.UtcDateTime }), ct);
        await AuditFamilyAsync(connection, transaction, actor, residentId.Value, unit.UnitId, id, restrict ? "EXT_APPOINTMENT_RESIDENT_HIDE" : "EXT_APPOINTMENT_RESIDENT_SHOW", now, ct);
        transaction.Commit();
        return expectedNumber + 1;
    }

    private static async Task RequireAdministrationAsync(SqlConnection connection, SqlTransaction? transaction, ExternalAppointmentActor actor, CancellationToken ct)
    {
        if (actor.Profile != SystemProfile.Administracion
            || await connection.ExecuteScalarAsync<int?>(new CommandDefinition(AdministrationScopeSql, Params(actor, new { }), transaction, cancellationToken: ct)) is null)
        {
            throw new AccessDeniedException();
        }
    }

    private static Task<CenterSwitchRow?> LatestCenterAsync(SqlConnection connection, SqlTransaction? transaction, ExternalAppointmentActor actor, CancellationToken ct, bool lockRow = false) =>
        connection.QuerySingleOrDefaultAsync<CenterSwitchRow?>(new CommandDefinition($"""
            SELECT TOP (1) numero AS Number, familia_ve AS Enabled FROM dbo.citas_externas_familia_centro {(lockRow ? "WITH (UPDLOCK, HOLDLOCK)" : "")}
             WHERE centro_id = @CenterId ORDER BY numero DESC
            """, Params(actor, new { }), transaction, cancellationToken: ct));

    private static Task<ResidentSwitchRow?> LatestResidentAsync(SqlConnection connection, SqlTransaction? transaction, ExternalAppointmentActor actor, Guid residentId, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<ResidentSwitchRow?>(new CommandDefinition("""
            SELECT TOP (1) numero AS Number, familia_no_ve AS Restricted FROM dbo.citas_externas_familia_residente
             WHERE centro_id = @CenterId AND residente_id = @ResidentId ORDER BY numero DESC
            """, Params(actor, new { ResidentId = residentId }), transaction, cancellationToken: ct));

    private static async Task<ExternalFamilyCounts> CountAsync(
        SqlConnection connection, ExternalAppointmentActor actor, DateOnly todayAtCenter, Guid? residentId, bool ignoreRestriction, CancellationToken ct)
    {
        var restriction = ignoreRestriction ? "" : "AND NOT " + ExternalFamilyVisibilitySql.ResidentRestricted("e.residente_id");
        var row = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
            FamilyCountSql.Replace("{RESTRICTION}", restriction),
            Params(actor, new { From = todayAtCenter, To = todayAtCenter.AddDays(ExternalFamilyRules.WindowDays), ResidentId = residentId }), cancellationToken: ct));
        return new ExternalFamilyCounts(row.Visible, row.WithoutSpecialty);
    }

    /// <summary>Inserta la fila nueva del interruptor. Si otra persona insertó la misma antes (índice único) o la base rechaza la secuencia (disparador), es un conflicto de versión, no un fallo.</summary>
    private static async Task InsertOrConflictAsync(SqlConnection connection, SqlTransaction transaction, string sql, DynamicParameters parameters, CancellationToken ct)
    {
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 50940)
        {
            throw new DomainValidationException(ExternalAppointment.RevisionConflictCode);
        }
    }

    /// <summary>Auditoría con solo identificadores: el recurso es la fila del interruptor, de la que se lee el valor anterior y el nuevo. El del centro no lleva residente ni unidad.</summary>
    private static Task AuditFamilyAsync(
        SqlConnection connection, SqlTransaction transaction, ExternalAppointmentActor actor, Guid? residentId, Guid? unitId, Guid rowId, string action, DateTimeOffset now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @AccountId, @Profile, @CenterId, @UnitId, @ResidentId, @ResourceType, @RowId, @Action, @Now);
            """, new
        {
            AccountId = actor.AccountId.Value, Profile = actor.Profile.ToCode(), CenterId = actor.CenterId.Value, UnitId = unitId, ResidentId = residentId,
            ResourceType = FamilyResourceType, RowId = rowId, Action = action, Now = now.UtcDateTime,
        }, transaction, cancellationToken: ct));

    private sealed record CenterSwitchRow(int Number, bool Enabled);

    private sealed record ResidentSwitchRow(int Number, bool Restricted);

    private sealed record CountRow(int Visible, int WithoutSpecialty);
}
