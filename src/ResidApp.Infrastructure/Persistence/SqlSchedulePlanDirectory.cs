using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>ADM-14/15 (0027): lectura de la planificación de las unidades concedidas al ámbito de Administración, entre dos fechas
/// (ambas incluidas), sin lo retirado. Repite que quien consulta tiene su ámbito vigente.</summary>
public sealed class SqlSchedulePlanDirectory(SqlConnectionFactory connections) : ISchedulePlanDirectory
{
    public async Task<IReadOnlyList<ScheduleEntry>> ListScheduleAsync(
        AccountAdministrationAccess access, DateOnly from, DateOnly to, UnitId? unitId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition($"""
            SELECT sch.id AS ScheduleId, sch.lote_id AS BatchId, sch.unidad_id AS UnitId, unit.nombre_visible AS UnitName, sch.equipo_id AS TeamId,
                   team.nombre_visible AS TeamName,
                   (SELECT COUNT(*) FROM dbo.equipos_miembros member WHERE member.equipo_id = team.id AND member.revocado_en IS NULL) AS TeamMembers,
                   sch.turno_id AS ShiftId, sh.nombre_visible AS ShiftName, sh.hora_inicio AS Start, sh.hora_fin AS [End],
                   sch.fecha AS Date, sch.conflicto_justificacion AS Justification
              FROM dbo.planificacion_turnos sch
              JOIN dbo.equipos team ON team.id = sch.equipo_id
              JOIN dbo.unidades unit ON unit.centro_id = sch.centro_id AND unit.id = sch.unidad_id
              JOIN dbo.turnos_catalogo sh ON sh.id = sch.turno_id
             WHERE sch.centro_id = @CenterId AND sch.retirado_en IS NULL AND sch.fecha >= @From AND sch.fecha <= @To
               AND (@UnitId IS NULL OR sch.unidad_id = @UnitId)
               AND {SqlSchedulingDirectory.AdministratorUnitGrant("sch.unidad_id")}
             ORDER BY sch.fecha, sh.hora_inicio, unit.nombre_visible, team.nombre_visible
            """, new
        {
            CenterId = access.CenterId.Value, access.ProfileScopeId, From = from.ToDateTime(TimeOnly.MinValue), To = to.ToDateTime(TimeOnly.MinValue),
            UnitId = unitId?.Value,
        }, cancellationToken: ct));
        return rows.Select(r => new ScheduleEntry(
            r.ScheduleId, r.BatchId, UnitId.From(r.UnitId), r.UnitName, r.TeamId, r.TeamName, r.TeamMembers, r.ShiftId, r.ShiftName,
            TimeOnly.FromTimeSpan(r.Start), TimeOnly.FromTimeSpan(r.End), DateOnly.FromDateTime(r.Date), r.Justification)).ToList();
    }

    public async Task<ScheduleSeries> FindSeriesAsync(AccountAdministrationAccess access, Guid batchId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = (await connection.QueryAsync<SeriesRow>(new CommandDefinition($"""
            SELECT sch.id AS ScheduleId, sch.unidad_id AS UnitId, unit.nombre_visible AS UnitName, sch.equipo_id AS TeamId,
                   team.nombre_visible AS TeamName, sch.turno_id AS ShiftId, sh.nombre_visible AS ShiftName, sh.hora_inicio AS Start,
                   sh.hora_fin AS [End], sch.fecha AS Date, sch.conflicto_justificacion AS Justification,
                   CAST(CASE WHEN sch.retirado_en IS NULL THEN 0 ELSE 1 END AS BIT) AS Retired
              FROM dbo.planificacion_turnos sch
              JOIN dbo.equipos team ON team.id = sch.equipo_id
              JOIN dbo.unidades unit ON unit.centro_id = sch.centro_id AND unit.id = sch.unidad_id
              JOIN dbo.turnos_catalogo sh ON sh.id = sch.turno_id
             WHERE sch.lote_id = @BatchId AND sch.centro_id = @CenterId AND {SqlSchedulingDirectory.AdministratorUnitGrant("sch.unidad_id")}
             ORDER BY sch.fecha
            """, new { BatchId = batchId, CenterId = access.CenterId.Value, access.ProfileScopeId }, cancellationToken: ct))).ToList();
        if (rows.Count == 0)
        {
            throw new AccessDeniedException();
        }

        var first = rows[0];
        return new ScheduleSeries(
            batchId, UnitId.From(first.UnitId), first.UnitName, first.TeamId, first.TeamName, first.ShiftId, first.ShiftName,
            TimeOnly.FromTimeSpan(first.Start), TimeOnly.FromTimeSpan(first.End),
            rows.Where(r => !r.Retired).Select(r => new ScheduleSeriesDate(r.ScheduleId, DateOnly.FromDateTime(r.Date), r.Justification)).ToList(),
            rows.Count(r => r.Retired));
    }

    private sealed record SeriesRow(
        Guid ScheduleId, Guid UnitId, string UnitName, Guid TeamId, string TeamName, Guid ShiftId, string ShiftName, TimeSpan Start, TimeSpan End,
        DateTime Date, string? Justification, bool Retired);

    private sealed record Row(
        Guid ScheduleId, Guid BatchId, Guid UnitId, string UnitName, Guid TeamId, string TeamName, int TeamMembers, Guid ShiftId, string ShiftName,
        TimeSpan Start, TimeSpan End, DateTime Date, string? Justification);
}
