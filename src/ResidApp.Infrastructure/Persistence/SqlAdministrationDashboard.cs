using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// El cuadro de mando de Administración, calculado al abrirlo. Los residentes de hoy son los de <see cref="SqlAdministracionResidentDirectory.ScopedResidentsSelect"/>,
/// la misma regla que la lista y el inicio; lo pasado (ocupación al empezar el periodo, altas y bajas) usa las unidades concedidas al perfil y, si
/// restringe residentes, solo esos. Las fechas se guardan en UTC. No lee nombres de residentes ni de familiares, ni texto libre, ni nada clínico.
/// </summary>
public sealed class SqlAdministrationDashboard(SqlConnectionFactory connections) : IAdministrationDashboardReader
{
    /// <summary>La restricción de residentes del perfil, para lo que no parte de la lista de hoy. La columna es la del residente de la fila.</summary>
    private static string ResidentAllowed(string column) => $"""
        (NOT EXISTS (SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                      WHERE restriction.ambito_perfil_id = @ProfileScopeId AND restriction.centro_id = @CenterId)
         OR EXISTS (SELECT 1 FROM dbo.ambitos_perfil_residente allowed
                     WHERE allowed.ambito_perfil_id = @ProfileScopeId AND allowed.centro_id = @CenterId
                       AND allowed.residente_id = {column} AND allowed.revocado_en IS NULL))
        """;

    public async Task<DashboardOccupancy> ReadOccupancyAsync(AccountAdministrationAccess access, DateTime beforeUtc, CancellationToken ct = default)
    {
        using var connection = await OpenAsync(access, ct);
        var units = await connection.QueryAsync<DashboardUnit>(new CommandDefinition($"""
            WITH scoped AS ({SqlAdministracionResidentDirectory.ScopedResidentsSelect}),
            units AS (
                SELECT unit.id AS UnitId, unit.nombre_visible AS UnitName, building.nombre_visible AS BuildingName,
                       CAST(CASE WHEN unit.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active
                  FROM dbo.ambitos_perfil_unidad unit_scope
                  JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = unit_scope.centro_id
                  LEFT JOIN dbo.edificios building ON building.id = unit.edificio_id AND building.centro_id = unit.centro_id
                 WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId AND unit_scope.revocado_en IS NULL),
            active_places AS (
                SELECT place.id AS PlaceId, place.unidad_id AS UnitId
                  FROM dbo.plazas place
                  JOIN dbo.habitaciones room ON room.id = place.habitacion_id AND room.centro_id = place.centro_id AND room.estado = 'ACTIVE'
                  JOIN dbo.unidades unit ON unit.id = place.unidad_id AND unit.centro_id = place.centro_id AND unit.estado = 'ACTIVE'
                 WHERE place.centro_id = @CenterId AND place.estado = 'ACTIVE'),
            current_location AS (
                SELECT scoped.UnitId, location.plaza_id AS PlaceId
                  FROM scoped
                  JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = scoped.ResidentId
                       AND location.centro_id = @CenterId AND location.vigente_hasta IS NULL),
            past_location AS (
                SELECT location.unidad_id AS UnitId, location.plaza_id AS PlaceId
                  FROM dbo.intervalos_ubicacion_residente location
                 WHERE location.centro_id = @CenterId AND location.vigente_desde < @Before
                   AND (location.vigente_hasta IS NULL OR location.vigente_hasta >= @Before)
                   AND {ResidentAllowed("location.residente_id")})
            SELECT units.UnitId, units.UnitName, units.BuildingName,
                   (SELECT COUNT(*) FROM current_location here WHERE here.UnitId = units.UnitId) AS Residents,
                   (SELECT COUNT(*) FROM active_places place WHERE place.UnitId = units.UnitId) AS Places,
                   (SELECT COUNT(*) FROM current_location here JOIN active_places place ON place.PlaceId = here.PlaceId
                     WHERE here.UnitId = units.UnitId) AS Occupied,
                   (SELECT COUNT(*) FROM past_location past WHERE past.UnitId = units.UnitId) AS ResidentsBefore,
                   (SELECT COUNT(*) FROM past_location past JOIN active_places place ON place.PlaceId = past.PlaceId
                     WHERE past.UnitId = units.UnitId) AS OccupiedBefore,
                   units.Active
              FROM units
             ORDER BY units.UnitName, units.UnitId
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value, Before = beforeUtc }, cancellationToken: ct));
        return new DashboardOccupancy(units.ToList());
    }

    public async Task<DashboardMovementFacts> ReadMovementsAsync(
        AccountAdministrationAccess access, DateOnly fromDate, DateTime fromUtc, CancellationToken ct = default)
    {
        using var connection = await OpenAsync(access, ct);
        var parameters = new
        {
            access.ProfileScopeId, CenterId = access.CenterId.Value, FromDate = fromDate.ToDateTime(TimeOnly.MinValue), FromUtc = fromUtc,
        };
        // El alta cuenta en la unidad de la primera ubicación de la estancia. La fecha de ingreso (script 0072) solo la trae la importación y es de la primera.
        var admissions = await connection.QueryAsync<DashboardAdmissionFact>(new CommandDefinition($"""
            SELECT admission.AdmissionDate, episode.vigente_desde AS RegisteredAt
              FROM dbo.episodios_residente_centro episode
              JOIN dbo.residentes resident ON resident.id = episode.residente_id AND resident.centro_id = episode.centro_id
             CROSS APPLY (SELECT TOP (1) location.unidad_id FROM dbo.intervalos_ubicacion_residente location
                           WHERE location.episodio_id = episode.id AND location.residente_id = episode.residente_id AND location.centro_id = @CenterId
                           ORDER BY location.vigente_desde) first_location
             CROSS APPLY (SELECT CAST(CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.episodios_residente_centro earlier
                                                            WHERE earlier.residente_id = episode.residente_id AND earlier.centro_id = @CenterId
                                                              AND earlier.vigente_desde < episode.vigente_desde)
                                           THEN resident.fecha_ingreso END AS DATETIME2(0)) AS AdmissionDate) admission
             WHERE episode.centro_id = @CenterId
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                            WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId
                              AND unit_scope.revocado_en IS NULL AND unit_scope.unidad_id = first_location.unidad_id)
               AND (admission.AdmissionDate >= @FromDate OR (admission.AdmissionDate IS NULL AND episode.vigente_desde >= @FromUtc))
               AND {ResidentAllowed("episode.residente_id")}
            """, parameters, cancellationToken: ct));
        var discharges = await connection.QueryAsync<DischargeRow>(new CommandDefinition($"""
            SELECT discharge.motivo_codigo AS ReasonCode, discharge.dada_en AS DischargedAt
              FROM dbo.bajas_residente discharge
             WHERE discharge.centro_id = @CenterId AND discharge.dada_en >= @FromUtc
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil_unidad unit_scope
                            WHERE unit_scope.ambito_perfil_id = @ProfileScopeId AND unit_scope.centro_id = @CenterId
                              AND unit_scope.revocado_en IS NULL AND unit_scope.unidad_id = discharge.unidad_id)
               AND {ResidentAllowed("discharge.residente_id")}
            """, parameters, cancellationToken: ct));
        return new DashboardMovementFacts(
            admissions.ToList(),
            discharges.Select(d => new DashboardDischargeFact(EnumCode.ParseCode<ResidentDischargeReason>(d.ReasonCode), d.DischargedAt)).ToList());
    }

    public async Task<IReadOnlyList<DashboardAppointmentFact>> ReadAppointmentsAsync(
        AccountAdministrationAccess access, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default)
    {
        using var connection = await OpenAsync(access, ct);
        var rows = await connection.QueryAsync<AppointmentRow>(new CommandDefinition("""
            SELECT inicio_en AS StartsAt, estado_codigo AS StatusCode FROM dbo.citas
             WHERE centro_id = @CenterId AND inicio_en >= @From AND inicio_en < @To
            """, new { CenterId = access.CenterId.Value, From = fromUtc, To = toUtcExclusive }, cancellationToken: ct));
        return rows.Select(r => new DashboardAppointmentFact(r.StartsAt, EnumCode.ParseCode<AppointmentStatus>(r.StatusCode))).ToList();
    }

    public async Task<IReadOnlyList<DashboardLinkFact>> ReadAuthorizationsAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await OpenAsync(access, ct);
        var rows = await connection.QueryAsync<AuthorizationRow>(new CommandDefinition($"""
            WITH scoped AS ({SqlAdministracionResidentDirectory.ScopedResidentsSelect})
            SELECT link.id AS LinkId, link.vinculado_en AS LinkedAt, link.desvinculado_en AS UnlinkedAt, authorization_change.numero AS Number,
                   authorization_change.estado_codigo AS StatusCode, authorization_change.valida_hasta AS ValidUntil,
                   authorization_change.registrado_en AS RecordedAt
              FROM scoped
              JOIN dbo.residentes_familiares link ON link.residente_id = scoped.ResidentId AND link.centro_id = @CenterId
              LEFT JOIN dbo.familiares_autorizaciones_cambios authorization_change ON authorization_change.vinculo_id = link.id
                   AND authorization_change.centro_id = link.centro_id
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value }, cancellationToken: ct));
        return rows.GroupBy(r => r.LinkId)
            .Select(link => new DashboardLinkFact(
                link.First().LinkedAt, link.First().UnlinkedAt,
                link.Where(r => r.Number is not null)
                    .Select(r => new DashboardAuthorizationChange(
                        r.Number!.Value, EnumCode.ParseCode<FamilyAuthorizationStatus>(r.StatusCode!),
                        r.ValidUntil is { } until ? DateOnly.FromDateTime(until) : null, r.RecordedAt!.Value))
                    .ToList()))
            .ToList();
    }

    private async Task<SqlConnection> OpenAsync(AccountAdministrationAccess access, CancellationToken ct)
    {
        var connection = await connections.OpenAsync(ct);
        try
        {
            await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private sealed record DischargeRow(string ReasonCode, DateTime DischargedAt);

    private sealed record AppointmentRow(DateTime StartsAt, string StatusCode);

    private sealed record AuthorizationRow(
        Guid LinkId, DateTime LinkedAt, DateTime? UnlinkedAt, int? Number, string? StatusCode, DateTime? ValidUntil, DateTime? RecordedAt);
}
