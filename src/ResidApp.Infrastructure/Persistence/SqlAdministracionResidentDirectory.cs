using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADM-02/ADM-03: residentes del ámbito de Administración. La regla de ámbito es la de SqlEnfermeriaResidentDirectory
/// (unidades concedidas y, si el ámbito los restringe, sus residentes) para un ámbito activo de ADMINISTRACION, así que
/// «sale en la lista» y «se abre su ficha» son lo mismo. Solo lee identidad, ubicación, episodio, correcciones de
/// identidad y, en la ficha, familiares, autorizaciones y contacto urgente (0022): nunca basal ni contenido clínico. Las fechas se guardan en UTC.
/// </summary>
public sealed class SqlAdministracionResidentDirectory(SqlConnectionFactory connections) : IAdministracionResidentDirectory
{
    internal const string ScopedResidentsSelect = """
        SELECT resident.id AS ResidentId, resident.nombre_visible AS DisplayName, resident.fecha_nacimiento AS BirthDate,
               resident.sexo_documentado_codigo AS SexCode, unit.id AS UnitId, unit.nombre_visible AS UnitName,
               episode.vigente_desde AS AdmittedAt
          FROM dbo.ambitos_perfil profile
          JOIN dbo.ambitos_perfil_unidad unit_scope ON unit_scope.ambito_perfil_id = profile.id
               AND unit_scope.centro_id = profile.centro_id AND unit_scope.revocado_en IS NULL
          JOIN dbo.unidades unit ON unit.id = unit_scope.unidad_id AND unit.centro_id = profile.centro_id AND unit.estado = 'ACTIVE'
          JOIN dbo.residentes resident ON resident.centro_id = profile.centro_id AND resident.estado = 'ACTIVE'
          JOIN dbo.intervalos_ubicacion_residente location ON location.residente_id = resident.id
               AND location.centro_id = profile.centro_id AND location.unidad_id = unit.id AND location.vigente_hasta IS NULL
          JOIN dbo.episodios_residente_centro episode ON episode.id = location.episodio_id
               AND episode.residente_id = resident.id AND episode.centro_id = profile.centro_id
          LEFT JOIN dbo.ambitos_perfil_residente resident_scope ON resident_scope.ambito_perfil_id = profile.id
               AND resident_scope.centro_id = profile.centro_id AND resident_scope.residente_id = resident.id
               AND resident_scope.revocado_en IS NULL
         WHERE profile.id = @ProfileScopeId AND profile.centro_id = @CenterId
           AND profile.perfil_codigo = 'ADMINISTRACION' AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL
           AND (resident_scope.id IS NOT NULL OR NOT EXISTS (
               SELECT 1 FROM dbo.ambitos_perfil_residente restriction
                WHERE restriction.ambito_perfil_id = profile.id AND restriction.centro_id = profile.centro_id))
        """;

    /// <summary>Familiares vinculables al residente @ResidentId: ya vinculados a algún residente del ámbito @ProfileScopeId (así nadie descubre
    /// a personas ligadas solo a residentes ajenos) y todavía no a este. Lo usa la lista y lo repite la escritura dentro de su transacción.</summary>
    internal static readonly string LinkableFamilySelect = $"""
        SELECT f.id AS FamilyId, f.nombre_visible AS DisplayName, f.telefono AS Phone
          FROM dbo.familiares f
         WHERE f.centro_id = @CenterId
           AND EXISTS (SELECT 1 FROM dbo.residentes_familiares other
                         JOIN ({ScopedResidentsSelect}) scoped ON scoped.ResidentId = other.residente_id
                        WHERE other.familiar_id = f.id AND other.centro_id = f.centro_id AND other.desvinculado_en IS NULL)
           AND NOT EXISTS (SELECT 1 FROM dbo.residentes_familiares mine
                            WHERE mine.familiar_id = f.id AND mine.centro_id = f.centro_id AND mine.residente_id = @ResidentId AND mine.desvinculado_en IS NULL)
        """;

    public async Task<IReadOnlyList<LinkableFamilyMember>> ListLinkableFamilyAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<LinkableRow>(new CommandDefinition(
            LinkableFamilySelect + " ORDER BY f.nombre_visible, f.telefono",
            new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value }, cancellationToken: ct));
        return rows.Select(r => new LinkableFamilyMember(r.FamilyId, r.DisplayName, r.Phone)).ToList();
    }

    public async Task<IReadOnlyList<AdministrativeResidentSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<ResidentRow>(new CommandDefinition(
            ScopedResidentsSelect + " ORDER BY resident.nombre_visible",
            new { ProfileScopeId = profileScopeId, CenterId = centerId.Value }, cancellationToken: ct));
        return rows.Select(ToSummary).ToList();
    }

    public async Task<AdministrativeResidentDetail?> FindAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var parameters = new { ProfileScopeId = profileScopeId, CenterId = centerId.Value, ResidentId = residentId.Value };
        var row = await connection.QuerySingleOrDefaultAsync<ResidentRow>(new CommandDefinition(
            ScopedResidentsSelect + " AND resident.id = @ResidentId", parameters, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        // El ámbito ya está comprobado con la consulta anterior; desde aquí solo se lee de ese residente.
        var locations = (await connection.QueryAsync<LocationRow>(new CommandDefinition("""
            SELECT unit.nombre_visible AS UnitName, i.vigente_desde AS StartedAt, i.vigente_hasta AS EndedAt,
                   i.modificado_por_perfil AS ProfileCode, room.nombre_visible AS RoomName, place.nombre_visible AS PlaceName
              FROM dbo.intervalos_ubicacion_residente i
              JOIN dbo.unidades unit ON unit.id = i.unidad_id AND unit.centro_id = i.centro_id
              LEFT JOIN dbo.habitaciones room ON room.id = i.habitacion_id AND room.centro_id = i.centro_id
              LEFT JOIN dbo.plazas place ON place.id = i.plaza_id AND place.centro_id = i.centro_id
             WHERE i.residente_id = @ResidentId AND i.centro_id = @CenterId
             ORDER BY i.vigente_desde DESC
            """, parameters, cancellationToken: ct)))
            .Select(l => new ResidentLocationInterval(
                l.UnitName, Utc(l.StartedAt), l.EndedAt is { } ended ? Utc(ended) : null, EnumCode.ParseCode<SystemProfile>(l.ProfileCode),
                l.RoomName, l.PlaceName))
            .ToList();
        var corrections = (await connection.QueryAsync<CorrectionRow>(new CommandDefinition("""
            SELECT numero AS Number, nombre_anterior AS NameBefore, fecha_nacimiento_anterior AS BirthDateBefore,
                   sexo_anterior_codigo AS SexBefore, nombre_nuevo AS NameAfter, fecha_nacimiento_nueva AS BirthDateAfter,
                   sexo_nuevo_codigo AS SexAfter, motivo AS Reason, corregido_en AS CorrectedAt
              FROM dbo.residentes_identidad_correcciones
             WHERE residente_id = @ResidentId AND centro_id = @CenterId
             ORDER BY numero
            """, parameters, cancellationToken: ct)))
            .Select(c => new ResidentIdentityCorrectionEntry(
                c.Number,
                new ResidentIdentity(c.NameBefore, DateOnly.FromDateTime(c.BirthDateBefore), EnumCode.ParseCode<DocumentedSexCode>(c.SexBefore)),
                new ResidentIdentity(c.NameAfter, DateOnly.FromDateTime(c.BirthDateAfter), EnumCode.ParseCode<DocumentedSexCode>(c.SexAfter)),
                c.Reason, Utc(c.CorrectedAt)))
            .ToList();
        var changes = (await connection.QueryAsync<AuthorizationChangeRow>(new CommandDefinition("""
            SELECT c.vinculo_id AS LinkId, c.numero AS Number, c.estado_codigo AS StatusCode, c.valida_hasta AS ValidUntil,
                   c.motivo AS Reason, c.registrado_en AS At
              FROM dbo.familiares_autorizaciones_cambios c
              JOIN dbo.residentes_familiares link ON link.id = c.vinculo_id AND link.centro_id = c.centro_id
             WHERE link.residente_id = @ResidentId AND link.centro_id = @CenterId AND link.desvinculado_en IS NULL
             ORDER BY c.numero
            """, parameters, cancellationToken: ct)))
            .ToLookup(c => c.LinkId, c => new FamilyAuthorizationChangeEntry(
                c.Number, EnumCode.ParseCode<FamilyAuthorizationStatus>(c.StatusCode),
                c.ValidUntil is { } until ? DateOnly.FromDateTime(until) : null, c.Reason, Utc(c.At)));
        var family = (await connection.QueryAsync<FamilyRow>(new CommandDefinition("""
            SELECT link.id AS LinkId, f.nombre_visible AS DisplayName, link.relacion AS Relationship, f.telefono AS Phone, f.correo AS Email,
                   link.es_referente AS IsReferent, link.es_tutor_legal AS IsLegalGuardian,
                   (SELECT COUNT(*) FROM dbo.residentes_familiares o
                     WHERE o.familiar_id = link.familiar_id AND o.centro_id = link.centro_id AND o.id <> link.id AND o.desvinculado_en IS NULL) AS OtherResidentLinks
              FROM dbo.residentes_familiares link
              JOIN dbo.familiares f ON f.id = link.familiar_id AND f.centro_id = link.centro_id
             WHERE link.residente_id = @ResidentId AND link.centro_id = @CenterId AND link.desvinculado_en IS NULL
             ORDER BY f.nombre_visible
            """, parameters, cancellationToken: ct)))
            .Select(f => new ResidentFamilyMember(
                f.LinkId, f.DisplayName, f.Relationship, f.Phone, f.Email, changes[f.LinkId].ToList(), f.OtherResidentLinks,
                f.IsReferent, f.IsLegalGuardian))
            .ToList();
        var contacts = (await connection.QueryAsync<ContactRow>(new CommandDefinition("""
            SELECT d.numero AS Number, d.accion_codigo AS Action, d.vinculo_id AS LinkId, f.nombre_visible AS DisplayName, d.designado_en AS At
              FROM dbo.residentes_contacto_urgente d
              LEFT JOIN dbo.residentes_familiares link ON link.id = d.vinculo_id AND link.centro_id = d.centro_id
              LEFT JOIN dbo.familiares f ON f.id = link.familiar_id AND f.centro_id = link.centro_id
             WHERE d.residente_id = @ResidentId AND d.centro_id = @CenterId
             ORDER BY d.numero
            """, parameters, cancellationToken: ct)))
            .Select(d => new EmergencyContactDesignation(d.Number, d.Action, d.LinkId, d.DisplayName, Utc(d.At)))
            .ToList();
        var former = (await connection.QueryAsync<FormerRow>(new CommandDefinition("""
            SELECT f.nombre_visible AS DisplayName, link.relacion AS Relationship, link.vinculado_en AS LinkedAt,
                   link.desvinculado_en AS UnlinkedAt, link.desvinculado_motivo AS Reason
              FROM dbo.residentes_familiares link
              JOIN dbo.familiares f ON f.id = link.familiar_id AND f.centro_id = link.centro_id
             WHERE link.residente_id = @ResidentId AND link.centro_id = @CenterId AND link.desvinculado_en IS NOT NULL
             ORDER BY link.desvinculado_en DESC
            """, parameters, cancellationToken: ct)))
            .Select(f => new FormerFamilyMember(f.DisplayName, f.Relationship, Utc(f.LinkedAt), Utc(f.UnlinkedAt), f.Reason))
            .ToList();
        return new AdministrativeResidentDetail(ToSummary(row), locations, corrections, family, contacts, former);
    }

    private static AdministrativeResidentSummary ToSummary(ResidentRow r) => new(
        ResidentId.From(r.ResidentId), r.DisplayName, DateOnly.FromDateTime(r.BirthDate), EnumCode.ParseCode<DocumentedSexCode>(r.SexCode),
        UnitId.From(r.UnitId), r.UnitName, Utc(r.AdmittedAt));

    private static DateTimeOffset Utc(DateTime value) => new(value, TimeSpan.Zero);

    private sealed record ResidentRow(
        Guid ResidentId, string DisplayName, DateTime BirthDate, string SexCode, Guid UnitId, string UnitName, DateTime AdmittedAt);

    private sealed record LocationRow(string UnitName, DateTime StartedAt, DateTime? EndedAt, string ProfileCode, string? RoomName, string? PlaceName);

    private sealed record CorrectionRow(
        int Number, string NameBefore, DateTime BirthDateBefore, string SexBefore, string NameAfter, DateTime BirthDateAfter,
        string SexAfter, string Reason, DateTime CorrectedAt);

    private sealed record AuthorizationChangeRow(Guid LinkId, int Number, string StatusCode, DateTime? ValidUntil, string? Reason, DateTime At);

    private sealed record FamilyRow(
        Guid LinkId, string DisplayName, string Relationship, string Phone, string? Email, bool IsReferent, bool IsLegalGuardian, int OtherResidentLinks);

    private sealed record LinkableRow(Guid FamilyId, string DisplayName, string Phone);

    private sealed record FormerRow(string DisplayName, string Relationship, DateTime LinkedAt, DateTime UnlinkedAt, string Reason);

    private sealed record ContactRow(int Number, string Action, Guid? LinkId, string? DisplayName, DateTime At);
}
