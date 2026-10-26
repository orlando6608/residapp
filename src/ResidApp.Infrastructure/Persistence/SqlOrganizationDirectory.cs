using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-07 (0073): lectura del organigrama del centro. La consulta es por centro, no por las unidades del ámbito de quien lee: todos los
/// profesionales del centro ven lo mismo (CJ 1.8). La política de seguridad por filas (seg.pol_centro) ya limita las filas al centro del ámbito
/// activo. El nombre de una cuenta es el que tenga puesto o, si no, su identificador de acceso; sin cuenta, el nombre que se escribió.
/// </summary>
public sealed class SqlOrganizationDirectory(SqlConnectionFactory connections) : IOrganizationDirectory
{
    /// <summary>Los perfiles profesionales: quien tiene uno vigente en el centro puede ocupar un cargo. La familia no.</summary>
    internal const string ProfessionalProfileCodes = "'AUXILIAR', 'ENFERMERIA', 'MEDICINA', 'ADMINISTRACION', 'DIRECCION_CLINICA'";

    public async Task<IReadOnlyList<PositionInfo>> ListAsync(CenterId centerId, bool includeInactive, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var positions = (await connection.QueryAsync<PositionRow>(new CommandDefinition("""
            SELECT id AS PositionId, nombre AS Name, una_persona AS OnePerson, superior_id AS SuperiorId, nota_coordinacion AS Note,
                   CAST(CASE WHEN estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active
              FROM dbo.cargos
             WHERE centro_id = @CenterId AND (@IncludeInactive = 1 OR estado = 'ACTIVE')
             ORDER BY nombre
            """, new { CenterId = centerId.Value, IncludeInactive = includeInactive }, cancellationToken: ct))).ToList();
        if (positions.Count == 0)
        {
            return [];
        }

        var occupants = (await connection.QueryAsync<OccupantRow>(new CommandDefinition("""
            SELECT occupant.id AS OccupantId, occupant.cargo_id AS PositionId, occupant.cuenta_id AS AccountId,
                   COALESCE(account.nombre_visible, account.sujeto_externo, occupant.nombre_texto COLLATE DATABASE_DEFAULT) AS Name
              FROM dbo.cargos_ocupantes occupant
              LEFT JOIN dbo.cuentas account ON account.id = occupant.cuenta_id
             WHERE occupant.centro_id = @CenterId
             ORDER BY COALESCE(account.nombre_visible, account.sujeto_externo, occupant.nombre_texto COLLATE DATABASE_DEFAULT)
            """, new { CenterId = centerId.Value }, cancellationToken: ct))).ToLookup(o => o.PositionId);
        var units = (await connection.QueryAsync<UnitRow>(new CommandDefinition("""
            SELECT link.cargo_id AS PositionId, unit.id AS UnitId, unit.nombre_visible AS Name
              FROM dbo.cargos_unidades link
              JOIN dbo.unidades unit ON unit.centro_id = link.centro_id AND unit.id = link.unidad_id
             WHERE link.centro_id = @CenterId
             ORDER BY unit.nombre_visible
            """, new { CenterId = centerId.Value }, cancellationToken: ct))).ToLookup(u => u.PositionId);
        return positions.Select(p => new PositionInfo(
            p.PositionId, p.Name, p.OnePerson, p.SuperiorId, p.Note, p.Active,
            occupants[p.PositionId].Select(o => new PositionOccupant(o.OccupantId, o.AccountId is { } id ? AccountId.From(id) : null, o.Name)).ToList(),
            units[p.PositionId].Select(u => new PositionUnit(UnitId.From(u.UnitId), u.Name)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<PositionOccupantCandidate>> ListCandidatesAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<CandidateRow>(new CommandDefinition($"""
            SELECT account.id AS AccountId, COALESCE(account.nombre_visible, account.sujeto_externo) AS Name
              FROM dbo.cuentas account
             WHERE account.estado = 'ACTIVE'
               AND EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile
                            WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId AND profile.estado = 'ACTIVE'
                              AND profile.revocado_en IS NULL AND profile.perfil_codigo IN ({ProfessionalProfileCodes}))
             ORDER BY COALESCE(account.nombre_visible, account.sujeto_externo)
            """, new { CenterId = access.CenterId.Value }, cancellationToken: ct));
        return rows.Select(r => new PositionOccupantCandidate(AccountId.From(r.AccountId), r.Name)).ToList();
    }

    private sealed record PositionRow(Guid PositionId, string Name, bool OnePerson, Guid? SuperiorId, string? Note, bool Active);

    private sealed record OccupantRow(Guid OccupantId, Guid PositionId, Guid? AccountId, string Name);

    private sealed record UnitRow(Guid PositionId, Guid UnitId, string Name);

    private sealed record CandidateRow(Guid AccountId, string Name);
}
