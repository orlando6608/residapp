using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Historia 2 (0029): habitaciones activas y plazas activas y libres de las unidades activas concedidas al ámbito (la misma unión que
/// el selector de unidades del alta). Una plaza con un residente ubicado ahora no se ofrece.</summary>
public sealed class SqlLocationOptionsDirectory(SqlConnectionFactory connections) : ILocationOptionsDirectory
{
    public async Task<IReadOnlyList<LocationOption>> ListAsync(
        string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT unit.id AS UnitId, unit.nombre_visible AS UnitName, room.id AS RoomId, room.nombre_visible AS RoomName,
                   place.id AS PlaceId, place.nombre_visible AS PlaceName
              FROM dbo.cuentas account
              JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id
                  AND profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
                  AND profile.perfil_codigo IN ('ADMINISTRACION', 'ENFERMERIA') AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                  AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = profile.centro_id AND unit.estado = 'ACTIVE'
              JOIN dbo.habitaciones room ON room.centro_id = unit.centro_id AND room.unidad_id = unit.id AND room.estado = 'ACTIVE'
              LEFT JOIN dbo.plazas place ON place.habitacion_id = room.id AND place.estado = 'ACTIVE'
                  AND NOT EXISTS (SELECT 1 FROM dbo.intervalos_ubicacion_residente location
                                   WHERE location.plaza_id = place.id AND location.vigente_hasta IS NULL)
             WHERE account.sujeto_externo = @ExternalSubject AND account.estado = 'ACTIVE'
             ORDER BY unit.nombre_visible, room.nombre_visible, place.nombre_visible
            """, new { ExternalSubject = externalSubject, ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));
        // Cada habitación sale una vez por plaza libre (o una sola vez, con la plaza vacía, si no tiene ninguna); se ofrece ella misma y cada plaza.
        var options = new List<LocationOption>();
        foreach (var group in rows.GroupBy(r => r.RoomId))
        {
            var first = group.First();
            options.Add(new LocationOption(UnitId.From(first.UnitId), first.UnitName, first.RoomId, first.RoomName, null, null));
            options.AddRange(group.Where(r => r.PlaceId is not null).Select(r =>
                new LocationOption(UnitId.From(r.UnitId), r.UnitName, r.RoomId, r.RoomName, r.PlaceId, r.PlaceName)));
        }

        return options;
    }

    private sealed record Row(Guid UnitId, string UnitName, Guid RoomId, string RoomName, Guid? PlaceId, string? PlaceName);
}
