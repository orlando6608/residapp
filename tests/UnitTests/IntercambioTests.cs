using System.Text;
using ResidApp.Application.Errors;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADR 0010: el emparejamiento determinista de columnas, la lectura de fechas y sexo, el CSV de exportación y el planificador de
/// filas de la importación (vista previa, sin base de datos).</summary>
public class IntercambioTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static ExchangeTable Table(string[] headers, params string[][] rows) =>
        new(headers, rows.Select((cells, i) => new ExchangeTableRow(i + 2, cells)).ToList());

    private static ExchangeMapping Mapping(params ExchangeField?[] columns) =>
        new(columns, ExchangeDateFormat.DiaMesAnio, new Dictionary<string, DocumentedSexCode>
        {
            ["h"] = DocumentedSexCode.Hombre, ["m"] = DocumentedSexCode.Mujer,
        });

    [Theory]
    [InlineData("F. Nac.", "f nac")]
    [InlineData("  Nº Habitación ", "no habitacion")]
    [InlineData("AÑO_ALTA", "ano alta")]
    [InlineData("", "")]
    public void Normalizar_QuitaAcentosSignosYMayusculas(string text, string expected) =>
        Assert.Equal(expected, ExchangeText.Normalize(text));

    [Theory]
    [InlineData("Unidad Psicogeriátrica 2", "UNIDAD-PSICOGERIATRICA-2")]
    [InlineData("Ala B", "ALA-B")]
    [InlineData("1", "U-1")]
    [InlineData("¿?", "U-1")]
    public void CodigoDeUnidad_SeDerivaDelNombre(string name, string expected) => Assert.Equal(expected, ExchangeText.UnitCode(name));

    [Fact]
    public void Emparejar_PorNombre_ReconoceLasCabecerasHabituales_YDescartaLoQueResidAppNoGuarda()
    {
        var table = Table(
            ["Nombre", "Apellidos", "F. Nac.", "Sexo", "Unidad", "Hab.", "Nº Cama", "Nº Expediente", "DNI", "Diagnóstico principal"],
            ["Ana", "Pérez", "05/03/1940", "M", "Norte", "101", "A", "E-1", "00000000T", "Texto clínico"]);

        var matches = ColumnMatcher.Match(ExchangeEntity.Residentes, table, Today);

        Assert.Equal(
            [
                ExchangeField.Nombre, ExchangeField.Apellidos, ExchangeField.FechaNacimiento, ExchangeField.Sexo, ExchangeField.Unidad,
                ExchangeField.Habitacion, ExchangeField.Plaza, ExchangeField.ReferenciaInterna, null, null,
            ],
            matches.Select(m => m.Field));
        Assert.All(matches.Where(m => m.Field is not null), m => Assert.Equal(MatchOrigin.Nombre, m.Origin));
    }

    [Fact]
    public void Emparejar_PorFormato_LaUnicaColumnaDeFechasYLaUnicaDeSexo()
    {
        var table = Table(["Residente", "Dato 1", "Dato 2", "Unidad"],
            ["Ana Pérez", "1940-03-05", "H", "Norte"], ["Luis Gil", "12/11/1938", "M", "Norte"]);

        var matches = ColumnMatcher.Match(ExchangeEntity.Residentes, table, Today);

        Assert.Equal(new ColumnMatch(ExchangeField.FechaNacimiento, MatchOrigin.Formato), matches[1]);
        Assert.Equal(new ColumnMatch(ExchangeField.Sexo, MatchOrigin.Formato), matches[2]);
    }

    [Fact]
    public void Emparejar_SinDecidir_SiDosCamposEmpatan_YUnCampoDeUnaColumnaSeQuedaConLaMejor()
    {
        var table = Table(["id unidad", "Unidad", "Unidad asistencial"], ["1", "Norte", "Norte"]);

        var matches = ColumnMatcher.Match(ExchangeEntity.Residentes, table, Today);

        Assert.Equal([null, ExchangeField.Unidad, null], matches.Select(m => m.Field));
    }

    [Theory]
    [InlineData(ExchangeEntity.Estructura)]
    [InlineData(ExchangeEntity.Residentes)]
    public void LaPlantillaDeResidApp_SeEmparejaSola(ExchangeEntity entity)
    {
        var fields = ExchangeCatalog.ExportFields(entity);
        var table = Table(fields.Select(f => ExchangeCatalog.Label(f)).ToArray());

        Assert.Equal(fields.Select(f => (ExchangeField?)f), ColumnMatcher.Match(entity, table, Today).Select(m => m.Field));
    }

    [Theory]
    [InlineData("05/03/1940", ExchangeDateFormat.DiaMesAnio, 1940, 3, 5)]
    [InlineData("05/03/1940", ExchangeDateFormat.MesDiaAnio, 1940, 5, 3)]
    [InlineData("5-3-40", ExchangeDateFormat.DiaMesAnio, 1940, 3, 5)]
    [InlineData("05.03.1940 0:00:00", ExchangeDateFormat.DiaMesAnio, 1940, 3, 5)]
    [InlineData("1940-03-05", ExchangeDateFormat.MesDiaAnio, 1940, 3, 5)]
    [InlineData("1940-03-05T00:00:00", ExchangeDateFormat.AnioMesDia, 1940, 3, 5)]
    [InlineData("01/01/25", ExchangeDateFormat.DiaMesAnio, 2025, 1, 1)]
    public void Fechas_SeLeenConElFormato_YElAnioDeDosCifrasNoQuedaEnElFuturo(string value, ExchangeDateFormat format, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), ExchangeDates.Parse(value, format, Today));

    [Theory]
    [InlineData("31/12/1940", ExchangeDateFormat.MesDiaAnio)]
    [InlineData("05/03/1940", ExchangeDateFormat.AnioMesDia)]
    [InlineData("30/02/1940", ExchangeDateFormat.DiaMesAnio)]
    [InlineData("hace mucho", ExchangeDateFormat.DiaMesAnio)]
    [InlineData("1940", ExchangeDateFormat.DiaMesAnio)]
    public void Fechas_QueNoSeEntienden_SonNull(string value, ExchangeDateFormat format) => Assert.Null(ExchangeDates.Parse(value, format, Today));

    [Fact]
    public void Fechas_CandidatosYAmbiguedad()
    {
        Assert.Equal([ExchangeDateFormat.DiaMesAnio], ExchangeDates.Candidates(["31/12/1940", "05/03/1941"], Today));
        Assert.Equal([ExchangeDateFormat.MesDiaAnio], ExchangeDates.Candidates(["12/31/1940"], Today));
        Assert.True(ExchangeDates.Ambiguous(["05/03/1940"], Today));
        Assert.False(ExchangeDates.Ambiguous(["01/01/1940", "1940-03-05", "31/12/1940"], Today));
        Assert.Equal("1940-03-05", ExchangeDates.Format(new DateOnly(1940, 3, 5), ExchangeDateFormat.AnioMesDia));
        Assert.Equal("05/03/1940", ExchangeDates.Format(new DateOnly(1940, 3, 5), ExchangeDateFormat.DiaMesAnio));
    }

    [Fact]
    public void Sexo_MEsMujerSalvoQueLaColumnaUseF()
    {
        Assert.Equal(DocumentedSexCode.Mujer, ExchangeSex.Suggest("m", ["h", "m"]));
        Assert.Equal(DocumentedSexCode.Hombre, ExchangeSex.Suggest("m", ["m", "f"]));
        Assert.Equal(DocumentedSexCode.Hombre, ExchangeSex.Suggest("varon", ["varon", "mujer"]));
        Assert.Equal(DocumentedSexCode.NoConsta, ExchangeSex.Suggest("nc", ["nc"]));
        Assert.Null(ExchangeSex.Suggest("1", ["1", "2"]));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("-5", "'-5")]
    [InlineData("+34 600", "'+34 600")]
    [InlineData("@SUM", "'@SUM")]
    [InlineData("Pérez; Ana", "\"Pérez; Ana\"")]
    [InlineData("Normal", "Normal")]
    [InlineData(null, "")]
    public void Csv_ProtegeContraFormulasYEntrecomillaLoNecesario(string? value, string expected) => Assert.Equal(expected, CsvExport.Cell(value));

    [Fact]
    public void Csv_LlevaBomYPuntoYComaYCrlf()
    {
        var bytes = CsvExport.Write(["A", "B"], [["1", null]]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("A;B\r\n1;\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Theory]
    [InlineData("EXCHANGE_FILE_INVALID", "No se puede leer el fichero")]
    [InlineData("EXCHANGE_FILE_TOO_LARGE", "demasiado grande")]
    [InlineData("EXCHANGE_MAPPING_INVALID", "Analizar el fichero")]
    [InlineData("EXCHANGE_TEMPLATE_NAME_INVALID", "nombre para la plantilla")]
    [InlineData("EXCHANGE_EXPORT_EMPTY", "al menos una columna")]
    public void LosFallosDeIntercambio_SonEntradaInvalidaConSuMotivo(string code, string text)
    {
        var failure = ApplicationResultRunner.Normalize(new DomainValidationException(code));

        Assert.Equal(ApplicationFailureCode.InvalidInput, failure.Code);
        Assert.Contains(text, failure.Message);
    }

    [Fact]
    public async Task Estructura_VistaPrevia_CreaLoQueFalta_ReutilizaLoYaVisto_YRechazaLoIncoherente()
    {
        var existingUnit = new StructureUnit(UnitId.From(Guid.NewGuid()), "SUR", "Unidad Sur", true, 0);
        var existingRoom = new LayoutRoom(Guid.NewGuid(), "201", true, 0, [new LayoutPlace(Guid.NewGuid(), "A", true, false)]);
        var import = new StructureImport([existingUnit], [], _ => Task.FromResult<IReadOnlyList<LayoutRoom>>([existingRoom]), writer: null);
        var mapping = Mapping(ExchangeField.Unidad, ExchangeField.Edificio, ExchangeField.Planta, ExchangeField.Habitacion, ExchangeField.Plaza);

        var reports = new List<ImportRowReport>();
        foreach (var row in new[]
                 {
                     new[] { "Unidad Norte", "Principal", "Planta 1", "101", "A" },
                     ["unidad norte", "Principal", "Planta 1", "101", "B"],
                     ["Unidad Norte", "Anexo", "", "102", ""],
                     ["Unidad Sur", "", "", "201", "A"],
                     ["", "Principal", "", "", ""],
                     ["Unidad Este", "", "Planta 2", "", ""],
                     ["Unidad Este", "", "", "", "C"],
                 }.Select((cells, i) => new ExchangeTableRow(i + 2, cells)))
        {
            reports.Add(await import.ApplyAsync(row, mapping));
        }

        Assert.Equal(
            [ImportOutcome.Nueva, ImportOutcome.Nueva, ImportOutcome.Error, ImportOutcome.YaExiste, ImportOutcome.Error, ImportOutcome.Error, ImportOutcome.Error],
            reports.Select(r => r.Outcome));
        Assert.Contains("la unidad «Unidad Norte»", reports[0].Detail);
        Assert.Contains("la plaza «A»", reports[0].Detail);
        Assert.Equal("Se crea la plaza «B».", reports[1].Detail);
        Assert.Contains("otro edificio o planta", reports[2].Detail);
        Assert.Equal("Falta la unidad.", reports[4].Detail);
        Assert.Equal("Para la planta hace falta el edificio.", reports[5].Detail);
        Assert.Equal("Para la plaza hace falta la habitación.", reports[6].Detail);
    }

    [Fact]
    public async Task Estructura_ConEscritor_CreaEnOrden_YUnaFilaConErrorNoEscribeNada()
    {
        var writer = new RecordingWriter();
        var inactive = new StructureUnit(UnitId.From(Guid.NewGuid()), "VIEJA", "Unidad vieja", false, 0);
        var import = new StructureImport([inactive], [], _ => Task.FromResult<IReadOnlyList<LayoutRoom>>([]), writer);
        var mapping = Mapping(ExchangeField.Unidad, ExchangeField.Edificio, ExchangeField.Habitacion);

        var created = await import.ApplyAsync(new ExchangeTableRow(2, ["Norte", "Principal", "101"]), mapping);
        var refused = await import.ApplyAsync(new ExchangeTableRow(3, ["Unidad vieja", "", "1"]), mapping);

        Assert.Equal(ImportOutcome.Nueva, created.Outcome);
        Assert.Equal(["unit NORTE Norte (derived)", "building Principal", "locate", "room 101"], writer.Calls);
        Assert.Equal((ImportOutcome.Error, "La unidad «Unidad vieja» está inactiva."), (refused.Outcome, refused.Detail));
    }

    [Fact]
    public async Task Residentes_Planificador_DetectaDuplicadosPlazasOcupadasYDatosQueFaltan()
    {
        var unit = new StructureUnit(UnitId.From(Guid.NewGuid()), "NORTE", "Unidad Norte", true, 0);
        var free = new LayoutPlace(Guid.NewGuid(), "A", true, false);
        var occupied = new LayoutPlace(Guid.NewGuid(), "B", true, true);
        var room = new LayoutRoom(Guid.NewGuid(), "101", true, 1, [free, occupied]);
        var census = new List<CensusEntry>
        {
            new("Luis Gil", new DateOnly(1938, 11, 12), DocumentedSexCode.Hombre, "Unidad Norte", null, null, "E-7"),
        };
        var planner = new ResidentImportPlanner([unit], _ => Task.FromResult<IReadOnlyList<LayoutRoom>>([room]), census, Today);
        var mapping = Mapping(
            ExchangeField.Nombre, ExchangeField.FechaNacimiento, ExchangeField.Sexo, ExchangeField.Unidad, ExchangeField.Habitacion,
            ExchangeField.Plaza, ExchangeField.ReferenciaInterna);

        var rows = new[]
        {
            new[] { "Ana Pérez", "05/03/1940", "M", "NORTE", "101", "A", "E-1" },
            ["Otra Persona", "01/01/1941", "H", "Unidad Norte", "101", "A", "E-2"],
            ["Pedro Ruiz", "01/01/1941", "H", "Unidad Norte", "101", "B", ""],
            ["Luis Gil", "12/11/1938", "H", "Unidad Norte", "", "", ""],
            ["Nuevo", "01/01/1941", "H", "Unidad Norte", "", "", "e 7"],
            ["ana perez", "05/03/1940", "M", "Unidad Norte", "", "", ""],
            ["Sin unidad", "01/01/1941", "H", "Oeste", "", "", ""],
            ["Futuro", "01/01/2030", "H", "Unidad Norte", "", "", ""],
            ["Sexo raro", "01/01/1941", "Q", "Unidad Norte", "", "", ""],
            ["Sin sexo", "01/01/1941", "?", "Unidad Norte", "", "", ""],
        };
        var results = new List<(ImportRowReport Report, ResidentImportPlan? Plan)>();
        foreach (var (cells, i) in rows.Select((cells, i) => (cells, i)))
        {
            results.Add(await planner.PlanAsync(new ExchangeTableRow(i + 2, cells), mapping));
        }

        Assert.Equal(
            [
                ImportOutcome.Nueva, ImportOutcome.Error, ImportOutcome.Error, ImportOutcome.PosibleDuplicado, ImportOutcome.YaExiste,
                ImportOutcome.PosibleDuplicado, ImportOutcome.Error, ImportOutcome.Error, ImportOutcome.Error, ImportOutcome.Nueva,
            ],
            results.Select(r => r.Report.Outcome));
        Assert.Equal(new ResidentImportPlan(unit.UnitId, room.RoomId, free.PlaceId, "Ana Pérez", new DateOnly(1940, 3, 5), DocumentedSexCode.Mujer, "E-1"),
            results[0].Plan);
        Assert.Equal("La plaza «A» ya está ocupada.", results[1].Report.Detail);
        Assert.Equal("La plaza «B» ya está ocupada.", results[2].Report.Detail);
        Assert.Contains("residente activo de la unidad «Unidad Norte»", results[3].Report.Detail);
        Assert.Contains("con la fila 2 del fichero", results[5].Report.Detail);
        Assert.All(new[] { results[3], results[5] }, r => Assert.True(r.Report.CanConfirm && !r.Report.Confirmed));
        Assert.Contains("no está en tu ámbito", results[6].Report.Detail);
        Assert.Equal("La fecha de nacimiento es posterior a hoy.", results[7].Report.Detail);
        Assert.Equal("Falta decir qué sexo es «Q».", results[8].Report.Detail);
        Assert.Equal(DocumentedSexCode.NoConsta, results[9].Plan!.Sex);
        Assert.All(results.Where(r => r.Report.Outcome != ImportOutcome.Nueva), r => Assert.Null(r.Plan));
    }

    private static (ResidentImportPlanner Planner, ExchangeMapping Mapping) HomonymPlanner(params int[] confirmed)
    {
        var unit = new StructureUnit(UnitId.From(Guid.NewGuid()), "NORTE", "Unidad Norte", true, 0);
        var census = new List<CensusEntry>
        {
            new("Luis Gil", new DateOnly(1938, 11, 12), DocumentedSexCode.Hombre, "Unidad Norte", null, null, "E-7"),
        };
        return (new ResidentImportPlanner([unit], _ => Task.FromResult<IReadOnlyList<LayoutRoom>>([]), census, Today, confirmed.ToHashSet()),
            Mapping(ExchangeField.Nombre, ExchangeField.FechaNacimiento, ExchangeField.Unidad, ExchangeField.ReferenciaInterna));
    }

    [Fact]
    public async Task Residentes_MismoNombreYFechaConOtraReferencia_EsPosibleDuplicadoYSeConfirmaPorFila()
    {
        var (planner, mapping) = HomonymPlanner(confirmed: 3);

        var unconfirmed = await planner.PlanAsync(new ExchangeTableRow(2, ["Luis Gil", "12/11/1938", "Norte", "E-99"]), mapping);
        var confirmed = await planner.PlanAsync(new ExchangeTableRow(3, ["Luis Gil", "12/11/1938", "Norte", "E-98"]), mapping);

        Assert.Equal(ImportOutcome.PosibleDuplicado, unconfirmed.Report.Outcome);
        Assert.Null(unconfirmed.Plan);
        Assert.Equal((ImportOutcome.Nueva, true, true), (confirmed.Report.Outcome, confirmed.Report.CanConfirm, confirmed.Report.Confirmed));
        Assert.Equal(3, confirmed.Plan!.ConfirmedRow);
    }

    [Fact]
    public async Task Residentes_MismaReferenciaYaExiste_AunqueCambienNombreYFecha_YConfirmarNoLoCambia()
    {
        var (planner, mapping) = HomonymPlanner(confirmed: 2);

        var (report, plan) = await planner.PlanAsync(new ExchangeTableRow(2, ["Otro Nombre", "01/01/1950", "Norte", "e 7"]), mapping);

        Assert.Equal(ImportOutcome.YaExiste, report.Outcome);
        Assert.Null(plan);
    }

    [Fact]
    public async Task Residentes_DosFilasConLaMismaReferencia_SegundaEsError()
    {
        var (planner, mapping) = HomonymPlanner();

        await planner.PlanAsync(new ExchangeTableRow(2, ["Ana Pérez", "05/03/1940", "Norte", "E-1"]), mapping);
        var (report, _) = await planner.PlanAsync(new ExchangeTableRow(3, ["Otra Distinta", "06/04/1941", "Norte", "E-1"]), mapping);

        Assert.Equal((ImportOutcome.Error, "Repite la referencia interna de la fila 2."), (report.Outcome, report.Detail));
    }

    [Fact]
    public async Task Residentes_FechaDeIngreso_SeLeeYSeValida()
    {
        var unit = new StructureUnit(UnitId.From(Guid.NewGuid()), "NORTE", "Norte", true, 0);
        var planner = new ResidentImportPlanner([unit], _ => Task.FromResult<IReadOnlyList<LayoutRoom>>([]), [], Today);
        var mapping = Mapping(ExchangeField.Nombre, ExchangeField.FechaNacimiento, ExchangeField.FechaIngreso, ExchangeField.Unidad);

        var good = await planner.PlanAsync(new ExchangeTableRow(2, ["Ana", "05/03/1940", "10/01/2024", "Norte"]), mapping);
        var empty = await planner.PlanAsync(new ExchangeTableRow(3, ["Beatriz", "05/03/1941", "", "Norte"]), mapping);
        var future = await planner.PlanAsync(new ExchangeTableRow(4, ["Carla", "05/03/1942", "01/01/2030", "Norte"]), mapping);
        var beforeBirth = await planner.PlanAsync(new ExchangeTableRow(5, ["Dora", "05/03/1943", "01/01/1940", "Norte"]), mapping);
        var garbage = await planner.PlanAsync(new ExchangeTableRow(6, ["Eva", "05/03/1944", "ayer", "Norte"]), mapping);

        Assert.Equal(new DateOnly(2024, 1, 10), good.Plan!.AdmissionDate);
        Assert.Null(empty.Plan!.AdmissionDate);
        Assert.Equal("La fecha de ingreso es posterior a hoy.", future.Report.Detail);
        Assert.Equal("La fecha de ingreso es anterior a la de nacimiento.", beforeBirth.Report.Detail);
        Assert.Equal("La fecha de ingreso «ayer» no se entiende con el formato elegido.", garbage.Report.Detail);
    }

    [Theory]
    [InlineData("Fecha de ingreso")]
    [InlineData("F. Ingreso")]
    [InlineData("Fecha alta")]
    public void Emparejador_ReconoceLaFechaDeIngresoPorElNombre(string header)
    {
        var table = new ExchangeTable(["Nombre", "Fecha nacimiento", header, "Unidad"],
            [new ExchangeTableRow(2, ["Ana", "05/03/1940", "10/01/2024", "Norte"])]);

        var matches = ColumnMatcher.Match(ExchangeEntity.Residentes, table, Today);

        Assert.Equal(new ColumnMatch(ExchangeField.FechaIngreso, MatchOrigin.Nombre), matches[2]);
        Assert.Equal(ExchangeField.FechaNacimiento, matches[1].Field);
    }

    [Fact]
    public async Task Residentes_NombreYApellidosEnVariasColumnas_SeUnen()
    {
        var unit = new StructureUnit(UnitId.From(Guid.NewGuid()), "NORTE", "Norte", true, 0);
        var planner = new ResidentImportPlanner([unit], _ => Task.FromResult<IReadOnlyList<LayoutRoom>>([]), [], Today);
        var mapping = Mapping(ExchangeField.Apellidos, ExchangeField.Apellidos, ExchangeField.Nombre, ExchangeField.FechaNacimiento, ExchangeField.Unidad);

        var (_, plan) = await planner.PlanAsync(new ExchangeTableRow(2, ["Pérez", "López ", " Ana  María", "1940-03-05", "Norte"]), mapping);

        Assert.Equal("Ana María Pérez López", plan!.Name);
        Assert.Equal(DocumentedSexCode.NoConsta, plan.Sex);
    }

    private sealed class RecordingWriter : IStructureWriter
    {
        public List<string> Calls { get; } = [];

        public Task<Guid> CreateUnitAsync(string code, string name, bool derivedCode) => Record($"unit {code} {name}{(derivedCode ? " (derived)" : "")}");

        public Task<Guid> CreateBuildingAsync(string name) => Record($"building {name}");

        public Task<Guid> CreateFloorAsync(Guid buildingId, string name) => Record($"floor {name}");

        public Task SetUnitLocationAsync(Guid unitId, Guid buildingId, Guid? floorId) => Record("locate");

        public Task<Guid> CreateRoomAsync(Guid unitId, string name) => Record($"room {name}");

        public Task<Guid> CreatePlaceAsync(Guid roomId, string name) => Record($"place {name}");

        private Task<Guid> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
