using System.Text;
using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Intercambio;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>ADR 0010: importar estructura y residentes desde un fichero con los casos de uso de siempre, las plantillas del centro y la
/// exportación auditada. Cada prueba crea su propio centro.</summary>
public class IntercambioTests
{
    private static readonly SqlConnectionFactory Connections = TestDatabase.ConnectionFactory;

    private static AdministracionIntercambioApplicationService Build(string externalSubject)
    {
        var session = new FixedIntercambioSessionIdentityProvider(externalSubject);
        return new AdministracionIntercambioApplicationService(
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(Connections), session), new ExcelTableFileReader(),
            new SqlExchangeDirectory(Connections), new SqlExchangeRepository(Connections), AdministracionResidentesTests.BuildEstructura(externalSubject),
            new CreateResident(new SqlAuthorizationEvidenceProvider(Connections), session, new SqlResidentRepository(Connections)));
    }

    private static AdministracionQuery Query(SeededProfile profile) => new(profile.ProfileScopeId, profile.CenterId);

    private static byte[] Csv(params string[] lines) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")];

    /// <summary>Lo que la pantalla devuelve tras el análisis: lo propuesto, sin cambios.</summary>
    private static ExchangeMappingInput Accept(ImportAnalysis analysis) => new(
        analysis.Columns.Select(c => c.Field?.ToCode()).ToList(), analysis.DateFormat.ToCode(),
        analysis.SexValues.Select(v => new SexValueInput(v.Key, v.Code?.ToCode())).ToList());

    private static async Task<ImportReport> ImportAsync(SeededProfile admin, ExchangeEntity entity, byte[] file, bool write = true, string? template = null)
    {
        var service = Build(admin.ExternalSubject);
        var analysis = await service.AnalyzeImportAsync(Query(admin), entity, file, null);
        Assert.True(analysis.Ok, analysis.Error?.Message);
        Assert.True(analysis.Value!.Ready, string.Join(" ", analysis.Value.Problems));
        var result = write
            ? await service.RunImportAsync(Query(admin), entity, file, Accept(analysis.Value), template)
            : await service.PreviewImportAsync(Query(admin), entity, file, Accept(analysis.Value));
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value!;
    }

    private static async Task<int> CountAuditAsync(SeededProfile admin, string action)
    {
        using var connection = await Connections.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @Account AND centro_id = @Center AND accion_codigo = @Action",
            new { Account = admin.AccountId.Value, Center = admin.CenterId.Value, Action = action });
    }

    private static async Task<string> UnitNameAsync(SeededProfile admin)
    {
        using var connection = await Connections.OpenAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT nombre_visible FROM dbo.unidades WHERE id = @Id", new { Id = admin.UnitId.Value }) ?? "";
    }

    private static byte[] StructureFile(string seededUnit) => Csv(
        "Unidad;Edificio;Planta;Habitación;Cama",
        "Norte;Principal;Planta 1;101;A",
        "Norte;Principal;Planta 1;101;B",
        "Norte;Principal;Planta 1;102;",
        $"{seededUnit};;;201;A");

    private static readonly byte[] ResidentsFile = Csv(
        "Nombre;Apellidos;F. Nacimiento;Sexo;Unidad;Hab.;Cama;Nº Expediente;Diagnóstico",
        "Ana;Pérez López;05/03/1940;M;Norte;101;A;E-1;Texto que no se importa",
        "Luis;Gil;12/11/1938;H;Norte;101;A;E-2;",
        "Rosa;Martín;1941-01-01;M;norte;102;;E-3;");

    [Fact]
    public async Task Estructura_LaVistaPreviaNoEscribe_ImportarCreaConAuditoria_YReimportarNoDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var file = StructureFile(await UnitNameAsync(admin));

        var preview = await ImportAsync(admin, ExchangeEntity.Estructura, file, write: false);
        var unitsAfterPreview = (await AdministracionResidentesTests.BuildEstructura(admin.ExternalSubject).ListStructureUnitsAsync(Query(admin))).Value!;
        var run = await ImportAsync(admin, ExchangeEntity.Estructura, file);
        var again = await ImportAsync(admin, ExchangeEntity.Estructura, file);

        Assert.False(preview.Executed);
        Assert.All(preview.Rows, r => Assert.Equal(ImportOutcome.Nueva, r.Outcome));
        Assert.Single(unitsAfterPreview);
        Assert.True(run.Executed);
        Assert.All(run.Rows, r => Assert.Equal(ImportOutcome.Nueva, r.Outcome));
        Assert.All(again.Rows, r => Assert.Equal(ImportOutcome.YaExiste, r.Outcome));

        var estructura = AdministracionResidentesTests.BuildEstructura(admin.ExternalSubject);
        var north = Assert.Single((await estructura.ListStructureUnitsAsync(Query(admin))).Value!, u => u.Name == "Norte");
        Assert.Equal(("NORTE", "Principal", "Planta 1"), (north.Code, north.BuildingName, north.FloorName));
        var rooms = (await estructura.ListRoomsAsync(new ListRoomsQuery(admin.ProfileScopeId, admin.CenterId, north.UnitId))).Value!;
        Assert.Equal([("101", "A,B"), ("102", "")], rooms.OrderBy(r => r.Name).Select(r => (r.Name, string.Join(",", r.Places.Select(p => p.Name).Order()))));
        var seededRooms = (await estructura.ListRoomsAsync(new ListRoomsQuery(admin.ProfileScopeId, admin.CenterId, admin.UnitId))).Value!;
        Assert.Equal("A", Assert.Single(Assert.Single(seededRooms).Places).Name);
        Assert.Equal((1, 1, 1, 3, 3, 2), (await CountAuditAsync(admin, "UNIT_CREATE"), await CountAuditAsync(admin, "BUILDING_CREATE"),
            await CountAuditAsync(admin, "FLOOR_CREATE"), await CountAuditAsync(admin, "ROOM_CREATE"), await CountAuditAsync(admin, "PLACE_CREATE"),
            await CountAuditAsync(admin, "IMPORT_RUN")));
    }

    [Fact]
    public async Task Residentes_SeDanDeAltaEnSuCama_SinLasColumnasQueNoSeUsan_YLaSegundaVezYaExisten()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await ImportAsync(admin, ExchangeEntity.Estructura, StructureFile(await UnitNameAsync(admin)));

        var analysis = (await Build(admin.ExternalSubject).AnalyzeImportAsync(Query(admin), ExchangeEntity.Residentes, ResidentsFile, null)).Value!;
        var run = await ImportAsync(admin, ExchangeEntity.Residentes, ResidentsFile);
        var again = await ImportAsync(admin, ExchangeEntity.Residentes, ResidentsFile);

        Assert.Null(analysis.Columns[^1].Field);
        Assert.Equal([("m", DocumentedSexCode.Mujer), ("h", DocumentedSexCode.Hombre)], analysis.SexValues.Select(v => (v.Key, v.Code!.Value)));
        Assert.Equal([ImportOutcome.Nueva, ImportOutcome.Error, ImportOutcome.Nueva], run.Rows.Select(r => r.Outcome));
        Assert.Equal("La plaza «A» ya está ocupada.", run.Rows[1].Detail);
        Assert.Equal([ImportOutcome.YaExiste, ImportOutcome.Error, ImportOutcome.YaExiste], again.Rows.Select(r => r.Outcome));

        var census = await new SqlExchangeDirectory(Connections).ListCensusAsync(
            new AccountAdministrationAccess(admin.ProfileScopeId, admin.AccountId, admin.CenterId));
        Assert.Equal(
            [
                new CensusEntry("Ana Pérez López", new DateOnly(1940, 3, 5), DocumentedSexCode.Mujer, "Norte", "101", "A", "E-1"),
                new CensusEntry("Rosa Martín", new DateOnly(1941, 1, 1), DocumentedSexCode.Mujer, "Norte", "102", null, "E-3"),
            ],
            census);
        Assert.Equal(2, await CountAuditAsync(admin, "RESIDENT_CREATE"));
    }

    [Fact]
    public async Task Plantilla_SeGuardaAlImportar_SeAplicaSolaAlMismoFichero_YNoLaVeOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var unit = await UnitNameAsync(admin);
        var file = Csv("Usuario/a;Nacido el;Género;Ubicación", $"Carmen Sanz;02/02/1939;Mujer;{unit}");
        var service = Build(admin.ExternalSubject);

        var first = (await service.AnalyzeImportAsync(Query(admin), ExchangeEntity.Residentes, file, null)).Value!;
        var chosen = new ExchangeMappingInput(["NAME", "BIRTH_DATE", "SEX", "UNIT"], "DMY", [new SexValueInput("mujer", "female")]);
        var run = await service.RunImportAsync(Query(admin), ExchangeEntity.Residentes, file, chosen, "  ERP de prueba ");
        var second = (await service.AnalyzeImportAsync(Query(admin), ExchangeEntity.Residentes, file, null)).Value!;
        var elsewhere = (await Build(other.ExternalSubject).AnalyzeImportAsync(Query(other), ExchangeEntity.Residentes, file, null)).Value!;

        Assert.Equal(
            [(ExchangeField.Nombre, MatchOrigin.Nombre), (ExchangeField.FechaNacimiento, MatchOrigin.Formato), (ExchangeField.Sexo, MatchOrigin.Nombre)],
            first.Columns.Take(3).Select(c => (c.Field!.Value, c.Origin!.Value)));
        Assert.Null(first.Columns[3].Field);
        Assert.False(first.Ready);
        Assert.Equal(ImportOutcome.Nueva, Assert.Single(run.Value!.Rows).Outcome);
        Assert.Equal("ERP de prueba", second.TemplateName);
        Assert.True(second.Ready);
        Assert.Equal((ExchangeField.Unidad, MatchOrigin.Plantilla), (second.Columns[3].Field!.Value, second.Columns[3].Origin!.Value));
        Assert.Null(elsewhere.TemplateName);
        Assert.Equal(1, await CountAuditAsync(admin, "EXCHANGE_TEMPLATE_SAVE"));
    }

    [Fact]
    public async Task Exportar_SoloDatosAdministrativos_AuditadoAntes_YConLaPlantillaDeOtraAplicacion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await ImportAsync(admin, ExchangeEntity.Estructura, StructureFile(await UnitNameAsync(admin)));
        await ImportAsync(admin, ExchangeEntity.Residentes, ResidentsFile);
        var service = Build(admin.ExternalSubject);
        var sample = Csv("NOMBRE COMPLETO,FECHA_NAC,CAMA,OTRA");

        var census = (await service.ExportAsync(Query(admin), ExchangeEntity.Residentes, null)).Value!;
        var structure = (await service.ExportAsync(Query(admin), ExchangeEntity.Estructura, null)).Value!;
        var analysis = (await service.AnalyzeExportTemplateAsync(Query(admin), ExchangeEntity.Residentes, sample, null)).Value!;
        var saved = await service.SaveExportTemplateAsync(Query(admin), ExchangeEntity.Residentes, sample,
            new ExchangeMappingInput(analysis.Columns.Select(c => c.Field?.ToCode()).ToList(), "YMD", []), "Destino");
        var templates = (await service.ListExportTemplatesAsync(Query(admin))).Value!;
        var withTemplate = (await service.ExportAsync(Query(admin), ExchangeEntity.Estructura, saved.Value)).Value!;

        string Text(ExportFile file) => Encoding.UTF8.GetString(file.Content[3..]);
        Assert.StartsWith("residapp-residentes-", census.FileName);
        Assert.Equal(
            "Nombre;Fecha de nacimiento;Sexo;Unidad;Habitación;Plaza (cama);Referencia interna\r\n"
            + "Ana Pérez López;05/03/1940;Mujer;Norte;101;A;E-1\r\nRosa Martín;01/01/1941;Mujer;Norte;102;;E-3\r\n",
            Text(census));
        Assert.Contains("Norte;NORTE;Principal;Planta 1;101;B\r\n", Text(structure));
        Assert.Equal([ExchangeField.Nombre, ExchangeField.FechaNacimiento, ExchangeField.Plaza, null], analysis.Columns.Select(c => c.Field));
        Assert.Equal("Destino", Assert.Single(templates).Name);
        Assert.Equal("NOMBRE COMPLETO;FECHA_NAC;CAMA;OTRA\r\nAna Pérez López;1940-03-05;A;\r\nRosa Martín;1941-01-01;;\r\n", Text(withTemplate));
        Assert.Equal(3, await CountAuditAsync(admin, "EXPORT_RUN"));
    }

    [Fact]
    public async Task Enfermeria_NoPuedeImportarNiExportar()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var service = Build(nurse.ExternalSubject);

        var analysis = await service.AnalyzeImportAsync(Query(nurse), ExchangeEntity.Estructura, StructureFile("x"), null);
        var export = await service.ExportAsync(Query(nurse), ExchangeEntity.Residentes, null);

        Assert.Equal(ApplicationFailureCode.AccessDenied, analysis.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, export.Error!.Code);
    }
}

file sealed class FixedIntercambioSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
