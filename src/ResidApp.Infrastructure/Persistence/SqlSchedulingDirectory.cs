using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-14 (0027): lectura de los turnos del centro y de los equipos de las unidades concedidas al ámbito de Administración. Cada
/// consulta repite que quien consulta tiene su ámbito vigente. Los equipos de otras unidades no se ven.
/// </summary>
public sealed class SqlSchedulingDirectory(SqlConnectionFactory connections) : ISchedulingDirectory
{
    /// <summary>Los perfiles con los que se puede estar en un equipo.</summary>
    internal const string TeamProfileCodes = "'AUXILIAR', 'ENFERMERIA', 'MEDICINA'";

    public async Task<IReadOnlyList<ShiftInfo>> ListShiftsAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<ShiftRow>(new CommandDefinition("""
            SELECT id AS ShiftId, nombre_visible AS Name, hora_inicio AS Start, hora_fin AS [End],
                   CAST(CASE WHEN estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active
              FROM dbo.turnos_catalogo
             WHERE centro_id = @CenterId
             ORDER BY estado, hora_inicio, nombre_visible
            """, new { CenterId = access.CenterId.Value }, cancellationToken: ct));
        return rows.Select(r => new ShiftInfo(r.ShiftId, r.Name, TimeOnly.FromTimeSpan(r.Start), TimeOnly.FromTimeSpan(r.End), r.Active)).ToList();
    }

    public async Task<IReadOnlyList<TeamInfo>> ListTeamsAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var teams = (await connection.QueryAsync<TeamRow>(new CommandDefinition($"""
            SELECT team.id AS TeamId, team.unidad_id AS UnitId, unit.nombre_visible AS UnitName, team.nombre_visible AS Name,
                   CAST(CASE WHEN team.estado = 'ACTIVE' THEN 1 ELSE 0 END AS BIT) AS Active
              FROM dbo.equipos team
              JOIN dbo.unidades unit ON unit.centro_id = team.centro_id AND unit.id = team.unidad_id
             WHERE team.centro_id = @CenterId AND {AdministratorUnitGrant("team.unidad_id")}
             ORDER BY unit.nombre_visible, team.estado, team.nombre_visible
            """, new { CenterId = access.CenterId.Value, access.ProfileScopeId }, cancellationToken: ct))).ToList();
        var members = teams.Count == 0
            ? []
            : (await connection.QueryAsync<MemberRow>(new CommandDefinition($"""
                SELECT member.equipo_id AS TeamId, member.cuenta_id AS AccountId, COALESCE(account.nombre_visible, account.sujeto_externo) AS Name,
                       member.concedido_en AS Since,
                       (SELECT STRING_AGG(profile.perfil_codigo, ',')
                          FROM dbo.ambitos_perfil profile
                          JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id AND unit_scope.centro_id = profile.centro_id
                               AND unit_scope.unidad_id = team.unidad_id AND unit_scope.revocado_en IS NULL
                         WHERE profile.cuenta_id = member.cuenta_id AND profile.centro_id = member.centro_id AND account.estado = 'ACTIVE'
                           AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL AND profile.perfil_codigo IN ({TeamProfileCodes})) AS ProfileCodes
                  FROM dbo.equipos_miembros member
                  JOIN dbo.equipos team ON team.id = member.equipo_id AND team.centro_id = member.centro_id
                  JOIN dbo.cuentas account ON account.id = member.cuenta_id
                 WHERE member.revocado_en IS NULL AND member.centro_id = @CenterId AND member.equipo_id IN @TeamIds
                 ORDER BY COALESCE(account.nombre_visible, account.sujeto_externo)
                """, new { CenterId = access.CenterId.Value, TeamIds = teams.Select(t => t.TeamId).ToList() }, cancellationToken: ct))).ToList();
        return teams.Select(team => new TeamInfo(
            team.TeamId, UnitId.From(team.UnitId), team.UnitName, team.Name, team.Active,
            members.Where(m => m.TeamId == team.TeamId).Select(m => new TeamMemberInfo(
                AccountId.From(m.AccountId), m.Name, ParseProfiles(m.ProfileCodes), m.ProfileCodes is not null,
                new DateTimeOffset(DateTime.SpecifyKind(m.Since, DateTimeKind.Utc)))).ToList())).ToList();
    }

    public async Task<IReadOnlyList<EligibleTeamMember>> ListEligibleMembersAsync(
        AccountAdministrationAccess access, Guid teamId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<EligibleRow>(new CommandDefinition($"""
            SELECT account.id AS AccountId, COALESCE(account.nombre_visible, account.sujeto_externo) AS Name,
                   STRING_AGG(profile.perfil_codigo, ',') AS ProfileCodes
              FROM dbo.equipos team
              JOIN dbo.ambitos_perfil profile ON profile.centro_id = team.centro_id AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
                   AND profile.perfil_codigo IN ({TeamProfileCodes})
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id AND unit_scope.centro_id = profile.centro_id
                   AND unit_scope.unidad_id = team.unidad_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.cuentas account ON account.id = profile.cuenta_id AND account.estado = 'ACTIVE'
             WHERE team.id = @TeamId AND team.centro_id = @CenterId AND {AdministratorUnitGrant("team.unidad_id")}
               AND NOT EXISTS (SELECT 1 FROM dbo.equipos_miembros member
                                WHERE member.equipo_id = team.id AND member.cuenta_id = account.id AND member.revocado_en IS NULL)
             GROUP BY account.id, account.nombre_visible, account.sujeto_externo
             ORDER BY COALESCE(account.nombre_visible, account.sujeto_externo)
            """, new { TeamId = teamId, CenterId = access.CenterId.Value, access.ProfileScopeId }, cancellationToken: ct));
        return rows.Select(r => new EligibleTeamMember(AccountId.From(r.AccountId), r.Name, ParseProfiles(r.ProfileCodes))).ToList();
    }

    /// <summary>La condición SQL de que la unidad (una expresión) está concedida, sin revocar, al ámbito de Administración (@ProfileScopeId).</summary>
    internal static string AdministratorUnitGrant(string unitExpression) => $"""
        EXISTS (SELECT 1 FROM dbo.ambitos_perfil_unidad admin_unit
                 WHERE admin_unit.ambito_perfil_id = @ProfileScopeId AND admin_unit.centro_id = @CenterId
                   AND admin_unit.unidad_id = {unitExpression} AND admin_unit.revocado_en IS NULL)
        """;

    private static IReadOnlyList<SystemProfile> ParseProfiles(string? codes) =>
        string.IsNullOrEmpty(codes)
            ? []
            : codes.Split(',').Distinct().Select(EnumCode.ParseCode<SystemProfile>).OrderBy(p => p).ToList();

    private sealed record ShiftRow(Guid ShiftId, string Name, TimeSpan Start, TimeSpan End, bool Active);

    private sealed record TeamRow(Guid TeamId, Guid UnitId, string UnitName, string Name, bool Active);

    private sealed record MemberRow(Guid TeamId, Guid AccountId, string Name, DateTime Since, string? ProfileCodes);

    private sealed record EligibleRow(Guid AccountId, string Name, string ProfileCodes);
}
