using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// Lee la marca del centro (script 0068). Por el ámbito, comprueba en SQL que el ámbito de la cookie pertenece a la cuenta de la sesión, está
/// vigente y no es de Plataforma (que se muestra con la marca de ResidApp); el logo no tiene seguridad por filas, así que esta unión es lo que
/// lo protege.
/// </summary>
public sealed class SqlCenterBrandingDirectory(SqlConnectionFactory connections) : ICenterBrandingDirectory
{
    private const string ScopeFrom = """

          FROM dbo.cuentas account
          JOIN dbo.ambitos_perfil profile ON profile.cuenta_id = account.id AND profile.id = @ProfileScopeId
              AND profile.estado = 'ACTIVE' AND profile.revocado_en IS NULL AND profile.perfil_codigo <> 'PLATAFORMA'
          JOIN dbo.centros center ON center.id = profile.centro_id AND center.estado = 'ACTIVE'
        """;

    private const string ScopeWhere = """

         WHERE account.sujeto_externo = @ExternalSubject AND account.estado = 'ACTIVE'
        """;

    public async Task<CenterBranding?> FindForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<BrandingRow>(new CommandDefinition(
            """
            SELECT center.nombre_visible AS Name,
                   (SELECT logo.actualizado_en FROM dbo.logos_centro logo WHERE logo.centro_id = center.id) AS LogoUpdatedAt
            """ + ScopeFrom + ScopeWhere, new { ExternalSubject = externalSubject, ProfileScopeId = profileScopeId }, cancellationToken: ct));
        return row is null ? null : ToBranding(row);
    }

    public async Task<CenterLogoImage?> ReadLogoForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<LogoRow>(new CommandDefinition(
            """
            SELECT logo.contenido AS Content, logo.tipo AS Type
            """ + ScopeFrom + "\n  JOIN dbo.logos_centro logo ON logo.centro_id = center.id" + ScopeWhere,
            new { ExternalSubject = externalSubject, ProfileScopeId = profileScopeId }, cancellationToken: ct));
        return row is null ? null : new CenterLogoImage(row.Content, row.Type);
    }

    public async Task<CenterBranding?> FindAsync(CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<BrandingRow>(new CommandDefinition("""
            SELECT center.nombre_visible AS Name,
                   (SELECT logo.actualizado_en FROM dbo.logos_centro logo WHERE logo.centro_id = center.id) AS LogoUpdatedAt
              FROM dbo.centros center WHERE center.id = @CenterId
            """, new { CenterId = centerId.Value }, cancellationToken: ct));
        return row is null ? null : ToBranding(row);
    }

    public async Task<CenterLogoImage?> ReadLogoAsync(CenterId centerId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<LogoRow>(new CommandDefinition(
            "SELECT contenido AS Content, tipo AS Type FROM dbo.logos_centro WHERE centro_id = @CenterId",
            new { CenterId = centerId.Value }, cancellationToken: ct));
        return row is null ? null : new CenterLogoImage(row.Content, row.Type);
    }

    private static CenterBranding ToBranding(BrandingRow row) => new(
        row.Name, row.LogoUpdatedAt is { } updatedAt ? new DateTimeOffset(DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)) : null);

    private sealed record BrandingRow(string Name, DateTime? LogoUpdatedAt);

    private sealed record LogoRow(byte[] Content, string Type);
}
