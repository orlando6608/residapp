using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Lecturas de Administración sobre bajas y suspensiones (script 0041). Un residente dado de baja ya no tiene ubicación vigente, así que
/// su ámbito es el de la última unidad que tuvo (dbo.bajas_residente.unidad_id): la misma regla que la ficha (unidades concedidas y, si el
/// ámbito restringe residentes, solo esos). Solo identidad administrativa y datos de la baja: nada clínico.
/// </summary>
public sealed class SqlResidentStatusDirectory(SqlConnectionFactory connections) : IResidentStatusDirectory
{
    private const string ScopedDischargesSelect = """
        SELECT resident.id AS ResidentId, resident.nombre_visible AS DisplayName, resident.fecha_nacimiento AS BirthDate,
               resident.sexo_documentado_codigo AS SexCode, unit.nombre_visible AS LastUnitName, discharge.motivo_codigo AS ReasonCode,
               discharge.motivo_texto AS ReasonText, discharge.dada_en AS DischargedAt, discharge.conservar_hasta AS RetainUntil
          FROM dbo.ambitos_perfil profile
          JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
               AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
          JOIN dbo.bajas_residente discharge ON discharge.centro_id = profile.centro_id AND discharge.unidad_id = unit_scope.unidad_id
               AND discharge.reactivada_en IS NULL
          JOIN dbo.unidades unit ON unit.id = discharge.unidad_id AND unit.centro_id = profile.centro_id
          JOIN dbo.residentes resident ON resident.id = discharge.residente_id AND resident.centro_id = profile.centro_id
               AND resident.estado = 'INACTIVE'
          LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
               AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = resident.id
               AND resident_scope.revocado_en IS NULL
         WHERE profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
           AND profile.perfil_codigo = 'ADMINISTRACION' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
           AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
               SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
        """;

    public async Task<IReadOnlyList<DischargedResident>> ListDischargedAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<DischargedRow>(new CommandDefinition(
            ScopedDischargesSelect + " ORDER BY discharge.dada_en DESC, resident.nombre_visible",
            new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));
        return rows.Select(ToDischarged).ToList();
    }

    public async Task<DischargedResident?> FindDischargedAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<DischargedRow>(new CommandDefinition(
            ScopedDischargesSelect + " AND resident.id = @ResidentId",
            new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value }, cancellationToken: ct));
        return row is null ? null : ToDischarged(row);
    }

    public async Task<ResidentSuspension?> FindSuspensionAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<SuspensionRow>(new CommandDefinition("""
            SELECT id AS Id, iniciada_en AS Since, nota AS Note FROM dbo.suspensiones_residente
             WHERE residente_id = @ResidentId AND centro_id = @CenterId AND finalizada_en IS NULL
            """, new { ResidentId = residentId.Value, CenterId = centerId.Value }, cancellationToken: ct));
        return row is null ? null : new ResidentSuspension(row.Id, new DateTimeOffset(DateTime.SpecifyKind(row.Since, DateTimeKind.Utc)), row.Note);
    }

    private static DischargedResident ToDischarged(DischargedRow r) => new(
        ResidentId.From(r.ResidentId), r.DisplayName, DateOnly.FromDateTime(r.BirthDate), EnumCode.ParseCode<DocumentedSexCode>(r.SexCode),
        r.LastUnitName, EnumCode.ParseCode<ResidentDischargeReason>(r.ReasonCode), r.ReasonText,
        new DateTimeOffset(DateTime.SpecifyKind(r.DischargedAt, DateTimeKind.Utc)), DateOnly.FromDateTime(r.RetainUntil));

    private sealed record DischargedRow(
        Guid ResidentId, string DisplayName, DateTime BirthDate, string SexCode, string LastUnitName, string ReasonCode, string? ReasonText,
        DateTime DischargedAt, DateTime RetainUntil);

    private sealed record SuspensionRow(Guid Id, DateTime Since, string? Note);
}
