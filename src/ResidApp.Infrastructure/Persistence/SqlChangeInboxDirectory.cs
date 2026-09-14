using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>Traduce las bandejas ENF-02/ENF-03 y el detalle ENF-04. Mismo predicado de ámbito "por defecto
/// o restringido" que SqlEnfermeriaResidentDirectory, aplicado sobre dbo.cierres_cotidianos_residente en
/// vez de dbo.residentes: solo cambios CAMBIO_ENVIADO de residentes visibles para este ámbito.</summary>
public sealed class SqlChangeInboxDirectory(SqlConnectionFactory connections) : IChangeInboxDirectory
{
    public async Task<IReadOnlyList<PendingChangeSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, DailyChangeClassification classification, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var closureCode = classification.ToCode();

        var closures = (await connection.QueryAsync<ClosureRow>(new CommandDefinition("""
            SELECT closure.id AS ClosureId, closure.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   closure.unidad_id AS UnitId, unit.nombre_visible AS UnitName, closure.registrado_por_perfil AS AuthorProfileCode,
                   closure.ocurrido_en AS OccurredAt, closure.motivo_prioritario_codigo AS PriorityReasonCode,
                   closure.aviso_directo_documentado AS DirectNoticeNotes
              FROM dbo.cierres_cotidianos_residente closure
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
                   AND profile.perfil_codigo = 'ENFERMERIA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                   AND unit_scope.centro_id = profile.centro_id AND unit_scope.unidad_id = closure.unidad_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = closure.unidad_id AND unit.centro_id = profile.centro_id
              JOIN dbo.residentes resident ON resident.id = closure.residente_id AND resident.centro_id = profile.centro_id
              LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
                   AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = closure.residente_id
                   AND resident_scope.revocado_en IS NULL
             WHERE closure.centro_id = @CenterId AND closure.tipo_codigo = 'CAMBIO_ENVIADO' AND closure.clasificacion_codigo = @ClosureCode
               AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
                   SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                    WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
             ORDER BY closure.ocurrido_en ASC
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ClosureCode = closureCode }, cancellationToken: ct)))
            .ToList();
        if (closures.Count == 0)
        {
            return [];
        }

        var closureIds = closures.Select(c => c.ClosureId).ToList();
        var areaRows = await connection.QueryAsync<AreaCodeRow>(new CommandDefinition("""
            SELECT cierre_id AS ClosureId, area_codigo AS AreaCode FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id IN @ClosureIds
            """, new { ClosureIds = closureIds }, cancellationToken: ct));
        var areasByClosureId = areaRows
            .GroupBy(a => a.ClosureId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<DailyChangeAreaCode>)g.Select(a => EnumCode.ParseCode<DailyChangeAreaCode>(a.AreaCode)).ToList());

        return closures.Select(c => new PendingChangeSummary(
            c.ClosureId, ResidentId.From(c.ResidentId), c.ResidentDisplayName, UnitId.From(c.UnitId), c.UnitName,
            areasByClosureId.GetValueOrDefault(c.ClosureId, []), EnumCode.ParseCode<SystemProfile>(c.AuthorProfileCode),
            new DateTimeOffset(c.OccurredAt, TimeSpan.Zero),
            c.PriorityReasonCode is null ? null : EnumCode.ParseCode<DailyChangePriorityReason>(c.PriorityReasonCode),
            c.DirectNoticeNotes)).ToList();
    }

    public async Task<PendingChangeDetail?> FindAsync(
        Guid profileScopeId, CenterId centerId, Guid closureId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);

        var closure = await connection.QuerySingleOrDefaultAsync<ClosureDetailRow>(new CommandDefinition("""
            SELECT closure.id AS ClosureId, closure.residente_id AS ResidentId, resident.nombre_visible AS ResidentDisplayName,
                   closure.unidad_id AS UnitId, unit.nombre_visible AS UnitName, closure.clasificacion_codigo AS ClassificationCode,
                   closure.temperatura_celsius AS TemperatureCelsius, closure.registrado_por_perfil AS AuthorProfileCode,
                   closure.motivo_prioritario_codigo AS PriorityReasonCode,
                   closure.aviso_directo_documentado AS DirectNoticeNotes, closure.ocurrido_en AS OccurredAt
              FROM dbo.cierres_cotidianos_residente closure
              JOIN dbo.ambitos_perfil profile ON profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
                   AND profile.perfil_codigo = 'ENFERMERIA' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
              JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
                   AND unit_scope.centro_id = profile.centro_id AND unit_scope.unidad_id = closure.unidad_id AND unit_scope.revocado_en IS NULL
              JOIN dbo.unidades unit ON unit.id = closure.unidad_id AND unit.centro_id = profile.centro_id
              JOIN dbo.residentes resident ON resident.id = closure.residente_id AND resident.centro_id = profile.centro_id
              LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
                   AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = closure.residente_id
                   AND resident_scope.revocado_en IS NULL
             WHERE closure.id = @ClosureId AND closure.centro_id = @CenterId AND closure.tipo_codigo = 'CAMBIO_ENVIADO'
               AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
                   SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                    WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
            """, new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ClosureId = closureId }, cancellationToken: ct));
        if (closure is null)
        {
            return null;
        }

        var areaOptionRows = await connection.QueryAsync<AreaOptionRow>(new CommandDefinition("""
            SELECT area.id AS AreaId, area.area_codigo AS AreaCode, area.texto_libre AS [FreeText], opt.opcion_codigo AS OptionCode
              FROM dbo.cierres_cotidianos_cambio_areas area
              LEFT JOIN dbo.cierres_cotidianos_cambio_area_opciones opt ON opt.area_id = area.id
             WHERE area.cierre_id = @ClosureId
             ORDER BY area.area_codigo
            """, new { ClosureId = closureId }, cancellationToken: ct));
        var areas = areaOptionRows
            .GroupBy(r => (r.AreaId, r.AreaCode, r.FreeText))
            .Select(g => new PendingChangeAreaSummary(
                EnumCode.ParseCode<DailyChangeAreaCode>(g.Key.AreaCode),
                g.Where(r => r.OptionCode is not null).Select(r => EnumCode.ParseCode<DailyChangeAreaOptionCode>(r.OptionCode!)).ToList(),
                g.Key.FreeText))
            .ToList();

        return new PendingChangeDetail(
            closure.ClosureId, ResidentId.From(closure.ResidentId), closure.ResidentDisplayName, UnitId.From(closure.UnitId), closure.UnitName,
            EnumCode.ParseCode<DailyChangeClassification>(closure.ClassificationCode), areas, closure.TemperatureCelsius,
            EnumCode.ParseCode<SystemProfile>(closure.AuthorProfileCode),
            closure.PriorityReasonCode is null ? null : EnumCode.ParseCode<DailyChangePriorityReason>(closure.PriorityReasonCode),
            closure.DirectNoticeNotes, new DateTimeOffset(closure.OccurredAt, TimeSpan.Zero));
    }

    /// <summary>OccurredAt es DateTime, no DateTimeOffset: Dapper 2.1.79 no materializa DateTimeOffset en
    /// constructores de record leídos de DATETIME2 (ver SqlBaselineRepository, mismo hallazgo). Se
    /// convierte explícitamente al construir el record de puerto.</summary>
    private sealed record ClosureRow(
        Guid ClosureId, Guid ResidentId, string ResidentDisplayName, Guid UnitId, string? UnitName, string AuthorProfileCode,
        DateTime OccurredAt, string? PriorityReasonCode, string? DirectNoticeNotes);

    private sealed record ClosureDetailRow(
        Guid ClosureId, Guid ResidentId, string ResidentDisplayName, Guid UnitId, string? UnitName, string ClassificationCode,
        decimal? TemperatureCelsius, string AuthorProfileCode, string? PriorityReasonCode, string? DirectNoticeNotes, DateTime OccurredAt);

    private sealed record AreaCodeRow(Guid ClosureId, string AreaCode);

    private sealed record AreaOptionRow(Guid AreaId, string AreaCode, string? FreeText, string? OptionCode);
}
