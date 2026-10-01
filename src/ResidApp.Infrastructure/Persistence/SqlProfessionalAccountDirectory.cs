using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-12/ADM-13 (0023): las cuentas con algún perfil (vigente o revocado) en el centro de quien gestiona, con sus perfiles,
/// unidades, residentes y permisos (0024) de este centro. De otros centros solo se dice si hay perfiles vigentes. Quien
/// concede o revoca se muestra por su nombre o, si no tiene, por su sujeto externo. Las fechas se guardan en UTC.
/// </summary>
public sealed class SqlProfessionalAccountDirectory(SqlConnectionFactory connections) : IProfessionalAccountDirectory
{
    /// <summary>Los residentes que se pueden asignar al perfil Auxiliar @TargetScopeId: activos, en una unidad vigente de
    /// ese perfil, en el ámbito de Administración @ProfileScopeId (la regla de SqlAdministracionResidentDirectory) y sin
    /// asignación vigente.</summary>
    internal const string AssignableResidentsSelect = $"""
        SELECT resident.id AS ResidentId, resident.nombre_visible AS DisplayName, unit.id AS UnitId, unit.nombre_visible AS UnitName
          FROM dbo.ambitos_perfil target
          JOIN dbo.ambitos_perfil_unidad target_unit ON target_unit.ambito_perfil_id = target.id
               AND target_unit.centro_id = target.centro_id AND target_unit.revocado_en IS NULL
          JOIN dbo.unidades unit ON unit.id = target_unit.unidad_id AND unit.centro_id = target.centro_id AND unit.estado = 'ACTIVE'
          JOIN dbo.intervalos_ubicacion_residente location ON location.unidad_id = unit.id
               AND location.centro_id = target.centro_id AND location.vigente_hasta IS NULL
          JOIN dbo.residentes resident ON resident.id = location.residente_id AND resident.centro_id = target.centro_id
               AND resident.estado = 'ACTIVE'
         WHERE target.id = @TargetScopeId AND target.centro_id = @CenterId AND target.perfil_codigo = 'AUXILIAR'
           AND target.estado = 'ACTIVE' AND target.revocado_en IS NULL
           AND NOT EXISTS (
               SELECT 1 FROM dbo.ambitos_perfil_residente assigned
                WHERE assigned.ambito_perfil_id = target.id AND assigned.centro_id = target.centro_id
                  AND assigned.residente_id = resident.id AND assigned.revocado_en IS NULL)
           AND resident.id IN (SELECT scoped.ResidentId FROM ({SqlAdministracionResidentDirectory.ScopedResidentsSelect}) scoped)
        """;

    public async Task<IReadOnlyList<ProfessionalAccountSummary>> ListAsync(
        AccountAdministrationAccess access, CancellationToken ct = default) =>
        await LoadAsync(access.CenterId, null, ct);

    public async Task<ProfessionalAccountDetail?> FindAsync(
        AccountAdministrationAccess access, AccountId accountId, CancellationToken ct = default)
    {
        var account = (await LoadAsync(access.CenterId, accountId, ct)).SingleOrDefault();
        if (account is null)
        {
            return null;
        }

        using var connection = await connections.OpenAsync(ct);
        var elsewhere = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM dbo.ambitos_perfil
             WHERE cuenta_id = @AccountId AND centro_id <> @CenterId AND estado = 'ACTIVE'
            """, new { AccountId = accountId.Value, CenterId = access.CenterId.Value }, cancellationToken: ct));
        return new ProfessionalAccountDetail(account, elsewhere > 0, accountId == access.AccountId);
    }

    public async Task<IReadOnlyList<AssignableResident>> ListAssignableResidentsAsync(
        AccountAdministrationAccess access, Guid profileScopeId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<AssignableRow>(new CommandDefinition(
            AssignableResidentsSelect + " ORDER BY resident.nombre_visible",
            new { TargetScopeId = profileScopeId, access.ProfileScopeId, CenterId = access.CenterId.Value }, cancellationToken: ct));
        return rows.Select(r => new AssignableResident(ResidentId.From(r.ResidentId), r.DisplayName, r.UnitName)).ToList();
    }

    private async Task<IReadOnlyList<ProfessionalAccountSummary>> LoadAsync(CenterId centerId, AccountId? accountId, CancellationToken ct)
    {
        using var connection = await connections.OpenAsync(ct);
        var parameters = new { CenterId = centerId.Value, AccountId = accountId?.Value };
        const string accountFilter = "(@AccountId IS NULL OR profile.cuenta_id = @AccountId)";
        var accounts = await connection.QueryAsync<AccountRow>(new CommandDefinition($"""
            SELECT account.id AS AccountId, account.sujeto_externo AS Subject, account.nombre_visible AS DisplayName,
                   account.estado AS StatusCode
              FROM dbo.cuentas account
             WHERE EXISTS (SELECT 1 FROM dbo.ambitos_perfil profile
                            WHERE profile.cuenta_id = account.id AND profile.centro_id = @CenterId AND {accountFilter})
             ORDER BY COALESCE(account.nombre_visible, account.sujeto_externo)
            """, parameters, cancellationToken: ct));
        var profiles = (await connection.QueryAsync<GrantRow>(new CommandDefinition($"""
            SELECT profile.id AS Id, profile.cuenta_id AS OwnerId, profile.perfil_codigo AS Code, profile.id AS TargetId,
                   profile.concedido_en AS GrantedAt, COALESCE(granter.nombre_visible, granter.sujeto_externo) AS GrantedBy,
                   profile.revocado_en AS RevokedAt, COALESCE(revoker.nombre_visible, revoker.sujeto_externo) AS RevokedBy
              FROM dbo.ambitos_perfil profile
              JOIN dbo.cuentas granter ON granter.id = profile.concedido_por_cuenta_id
              LEFT JOIN dbo.cuentas revoker ON revoker.id = profile.revocado_por_cuenta_id
             WHERE profile.centro_id = @CenterId AND {accountFilter}
             ORDER BY CASE profile.estado WHEN 'ACTIVE' THEN 0 ELSE 1 END, profile.concedido_en DESC
            """, parameters, cancellationToken: ct))).ToList();
        var units = (await connection.QueryAsync<GrantRow>(new CommandDefinition($"""
            SELECT grant_row.ambito_perfil_id AS Id, profile.cuenta_id AS OwnerId, unit.nombre_visible AS Code,
                   grant_row.unidad_id AS TargetId, grant_row.concedido_en AS GrantedAt,
                   COALESCE(granter.nombre_visible, granter.sujeto_externo) AS GrantedBy, grant_row.revocado_en AS RevokedAt,
                   COALESCE(revoker.nombre_visible, revoker.sujeto_externo) AS RevokedBy
              FROM dbo.ambitos_perfil_unidad grant_row
              JOIN dbo.ambitos_perfil profile ON profile.id = grant_row.ambito_perfil_id AND profile.centro_id = grant_row.centro_id
              JOIN dbo.unidades unit ON unit.id = grant_row.unidad_id AND unit.centro_id = grant_row.centro_id
              JOIN dbo.cuentas granter ON granter.id = grant_row.concedido_por_cuenta_id
              LEFT JOIN dbo.cuentas revoker ON revoker.id = grant_row.revocado_por_cuenta_id
             WHERE grant_row.centro_id = @CenterId AND {accountFilter}
             ORDER BY unit.nombre_visible, grant_row.concedido_en
            """, parameters, cancellationToken: ct))).ToLookup(u => u.Id);
        var residents = (await connection.QueryAsync<GrantRow>(new CommandDefinition($"""
            SELECT grant_row.ambito_perfil_id AS Id, profile.cuenta_id AS OwnerId, resident.nombre_visible AS Code,
                   grant_row.residente_id AS TargetId, grant_row.concedido_en AS GrantedAt,
                   COALESCE(granter.nombre_visible, granter.sujeto_externo) AS GrantedBy, grant_row.revocado_en AS RevokedAt,
                   COALESCE(revoker.nombre_visible, revoker.sujeto_externo) AS RevokedBy
              FROM dbo.ambitos_perfil_residente grant_row
              JOIN dbo.ambitos_perfil profile ON profile.id = grant_row.ambito_perfil_id AND profile.centro_id = grant_row.centro_id
              JOIN dbo.residentes resident ON resident.id = grant_row.residente_id AND resident.centro_id = grant_row.centro_id
              JOIN dbo.cuentas granter ON granter.id = grant_row.concedido_por_cuenta_id
              LEFT JOIN dbo.cuentas revoker ON revoker.id = grant_row.revocado_por_cuenta_id
             WHERE grant_row.centro_id = @CenterId AND {accountFilter}
             ORDER BY resident.nombre_visible, grant_row.concedido_en
            """, parameters, cancellationToken: ct))).ToLookup(r => r.Id);
        var permissions = (await connection.QueryAsync<GrantRow>(new CommandDefinition($"""
            SELECT grant_row.ambito_perfil_id AS Id, profile.cuenta_id AS OwnerId, grant_row.permiso_codigo AS Code,
                   grant_row.id AS TargetId, grant_row.concedido_en AS GrantedAt,
                   COALESCE(granter.nombre_visible, granter.sujeto_externo) AS GrantedBy, grant_row.revocado_en AS RevokedAt,
                   COALESCE(revoker.nombre_visible, revoker.sujeto_externo) AS RevokedBy
              FROM dbo.permisos_perfil grant_row
              JOIN dbo.ambitos_perfil profile ON profile.id = grant_row.ambito_perfil_id AND profile.centro_id = grant_row.centro_id
              JOIN dbo.cuentas granter ON granter.id = grant_row.concedido_por_cuenta_id
              LEFT JOIN dbo.cuentas revoker ON revoker.id = grant_row.revocado_por_cuenta_id
             WHERE grant_row.centro_id = @CenterId AND {accountFilter}
             ORDER BY grant_row.permiso_codigo, grant_row.concedido_en
            """, parameters, cancellationToken: ct))).ToLookup(p => p.Id);

        var byAccount = profiles.ToLookup(p => p.OwnerId);
        return accounts.Select(a => new ProfessionalAccountSummary(
                AccountId.From(a.AccountId), a.Subject, a.DisplayName, EnumCode.ParseCode<AccountStatus>(a.StatusCode),
                byAccount[a.AccountId].Select(p => new AccountProfileScope(
                    p.Id, EnumCode.ParseCode<SystemProfile>(p.Code), Utc(p.GrantedAt), p.GrantedBy,
                    p.RevokedAt is { } revoked ? Utc(revoked) : null, p.RevokedBy,
                    units[p.Id].Select(ToGrant).ToList(), residents[p.Id].Select(ToGrant).ToList(),
                    permissions[p.Id].Select(ToGrant).ToList())).ToList()))
            .ToList();
    }

    private static AccountScopeGrant ToGrant(GrantRow row) => new(
        row.TargetId, row.Code, Utc(row.GrantedAt), row.GrantedBy, row.RevokedAt is { } revoked ? Utc(revoked) : null, row.RevokedBy);

    private static DateTimeOffset Utc(DateTime value) => new(value, TimeSpan.Zero);

    private sealed record AccountRow(Guid AccountId, string Subject, string? DisplayName, string StatusCode);

    /// <summary>Una fila de perfil, unidad, residente o permiso. Id es el ámbito de perfil; Code, el código del perfil o del
    /// permiso, o el nombre de la unidad o del residente; TargetId, el id de la unidad, del residente o de la fila del
    /// permiso.</summary>
    private sealed record GrantRow(
        Guid Id, Guid OwnerId, string Code, Guid TargetId, DateTime GrantedAt, string GrantedBy, DateTime? RevokedAt, string? RevokedBy);

    private sealed record AssignableRow(Guid ResidentId, string DisplayName, Guid UnitId, string UnitName);
}
