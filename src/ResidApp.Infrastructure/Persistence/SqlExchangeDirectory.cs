using System.Text.Json;
using Dapper;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Persistence;

/// <summary>
/// ADR 0010: lecturas de la importación y la exportación. Las plantillas (script 0060) son del centro, como el catálogo de turnos: se
/// filtran por el centro del ámbito (y la seguridad por filas lo repite) después de comprobar que quien consulta tiene su ámbito de
/// Administración vigente. El censo usa la misma regla de ámbito que la lista de residentes de Administración (ADM-02) y añade solo la
/// referencia interna del episodio vigente: nada de basal ni contenido clínico.
/// </summary>
public sealed class SqlExchangeDirectory(SqlConnectionFactory connections) : IExchangeDirectory
{
    private const string TemplateSelect = """
        SELECT id AS TemplateId, direccion AS DirectionCode, entidad AS EntityCode, nombre AS Name, mapeo_json AS MappingJson
          FROM dbo.plantillas_intercambio
         WHERE centro_id = @CenterId
        """;

    public async Task<ExchangeTemplate?> FindTemplateAsync(
        AccountAdministrationAccess access, ExchangeDirection direction, ExchangeEntity entity, string signature, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var row = await connection.QueryFirstOrDefaultAsync<TemplateRow>(new CommandDefinition(TemplateSelect + """
               AND direccion = @Direction AND entidad = @Entity AND firma_cabeceras = @Signature
             ORDER BY guardada_en DESC
            """, new { CenterId = access.CenterId.Value, Direction = direction.ToCode(), Entity = entity.ToCode(), Signature = signature },
            cancellationToken: ct));
        return row is null ? null : ToTemplate(row);
    }

    public async Task<ExchangeTemplate?> FindTemplateAsync(AccountAdministrationAccess access, Guid templateId, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var row = await connection.QuerySingleOrDefaultAsync<TemplateRow>(new CommandDefinition(
            TemplateSelect + " AND id = @TemplateId", new { CenterId = access.CenterId.Value, TemplateId = templateId }, cancellationToken: ct));
        return row is null ? null : ToTemplate(row);
    }

    public async Task<IReadOnlyList<ExchangeTemplate>> ListTemplatesAsync(
        AccountAdministrationAccess access, ExchangeDirection direction, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        await SqlProfessionalAccountRepository.EnsureAdministratorAsync(connection, null, access, ct);
        var rows = await connection.QueryAsync<TemplateRow>(new CommandDefinition(
            TemplateSelect + " AND direccion = @Direction ORDER BY entidad, nombre",
            new { CenterId = access.CenterId.Value, Direction = direction.ToCode() }, cancellationToken: ct));
        return rows.Select(ToTemplate).ToList();
    }

    public async Task<IReadOnlyList<CensusEntry>> ListCensusAsync(AccountAdministrationAccess access, CancellationToken ct = default)
    {
        using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<CensusRow>(new CommandDefinition($"""
            SELECT scoped.DisplayName, scoped.BirthDate, scoped.SexCode, scoped.UnitName, scoped.RoomName, scoped.PlaceName,
                   episode.referencia_interna AS InternalReference
              FROM ({SqlAdministracionResidentDirectory.ScopedResidentsSelect}) scoped
              LEFT JOIN dbo.episodios_residente_centro episode ON episode.residente_id = scoped.ResidentId
                   AND episode.centro_id = @CenterId AND episode.vigente_hasta IS NULL
             ORDER BY scoped.DisplayName
            """, new { access.ProfileScopeId, CenterId = access.CenterId.Value }, cancellationToken: ct));
        return rows.Select(r => new CensusEntry(
            r.DisplayName, DateOnly.FromDateTime(r.BirthDate), EnumCode.ParseCode<DocumentedSexCode>(r.SexCode), r.UnitName, r.RoomName, r.PlaceName,
            r.InternalReference)).ToList();
    }

    private static ExchangeTemplate ToTemplate(TemplateRow row)
    {
        var stored = JsonSerializer.Deserialize<StoredExchangeMapping>(row.MappingJson)!;
        return new ExchangeTemplate(
            row.TemplateId, EnumCode.ParseCode<ExchangeDirection>(row.DirectionCode), EnumCode.ParseCode<ExchangeEntity>(row.EntityCode), row.Name,
            stored.Headers, stored.ToMapping());
    }

    private sealed record TemplateRow(Guid TemplateId, string DirectionCode, string EntityCode, string Name, string MappingJson);

    private sealed record CensusRow(
        string DisplayName, DateTime BirthDate, string SexCode, string UnitName, string? RoomName, string? PlaceName, string? InternalReference);
}

/// <summary>Cómo se guarda una plantilla en plantillas_intercambio.mapeo_json: códigos estables (EnumCode), nunca nombres de miembros C#,
/// y ningún dato personal (cabeceras, campos y valores de la columna de sexo ya normalizados).</summary>
internal sealed record StoredExchangeMapping(
    List<string> Headers, List<string?> Fields, string DateFormat, Dictionary<string, string> SexValues)
{
    public static StoredExchangeMapping From(IReadOnlyList<string> headers, ExchangeMapping mapping) => new(
        headers.ToList(), mapping.Columns.Select(f => f?.ToCode()).ToList(), mapping.DateFormat.ToCode(),
        mapping.SexValues.ToDictionary(v => v.Key, v => v.Value.ToCode()));

    public ExchangeMapping ToMapping() => new(
        Fields.Select(code => code is null ? (ExchangeField?)null : EnumCode.ParseCode<ExchangeField>(code)).ToList(),
        EnumCode.ParseCode<ExchangeDateFormat>(DateFormat),
        SexValues.ToDictionary(v => v.Key, v => EnumCode.ParseCode<DocumentedSexCode>(v.Value)));
}
