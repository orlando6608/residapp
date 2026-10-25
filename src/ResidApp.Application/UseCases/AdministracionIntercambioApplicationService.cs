using System.Security.Cryptography;
using System.Text;
using ResidApp.Application.Errors;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Lo que se elige en la pantalla: el código de campo de cada columna («» = no se usa), el formato de las fechas y el sexo de
/// cada valor de la columna de sexo (por su clave normalizada).</summary>
public sealed record ExchangeMappingInput(IReadOnlyList<string?> Fields, string? DateFormat, IReadOnlyList<SexValueInput> SexValues);

public sealed record SexValueInput(string Key, string? Code);

/// <summary>Una columna del fichero: qué parece contener (sin enseñar sus valores), cuántos valores distintos tiene y a qué campo va.
/// Origin dice por qué se propuso el campo, si nadie lo cambió.</summary>
public sealed record ColumnAnalysis(int Index, string Header, ColumnKind Kind, int DistinctValues, ExchangeField? Field, MatchOrigin? Origin);

/// <summary>
/// El emparejamiento de un fichero que se va a importar. Problems impiden seguir (falta un campo obligatorio, un campo en dos
/// columnas, un valor de sexo sin asignar); Notices son avisos que no lo impiden. DateFormats son los formatos con que se leen todas
/// las fechas; si no hay ninguno, las filas con fechas que no se entienden saldrán con error.
/// </summary>
public sealed record ImportAnalysis(
    ExchangeEntity Entity, int Rows, IReadOnlyList<ColumnAnalysis> Columns, string? TemplateName, IReadOnlyList<ExchangeDateFormat> DateFormats,
    ExchangeDateFormat DateFormat, IReadOnlyList<SexValue> SexValues, IReadOnlyList<string> Problems, IReadOnlyList<string> Notices)
{
    public bool Ready => Problems.Count == 0;

    public bool UsesDates => Columns.Any(c => c.Field == ExchangeField.FechaNacimiento);

    public ExchangeMapping Mapping => new(
        Columns.Select(c => c.Field).ToList(), DateFormat,
        SexValues.Where(v => v.Code is not null).ToDictionary(v => v.Key, v => v.Code!.Value));
}

/// <summary>La vista previa (Executed = false) o el resultado (Executed = true) de una importación, fila a fila.</summary>
public sealed record ImportReport(ExchangeEntity Entity, IReadOnlyList<ImportRowReport> Rows, bool Executed)
{
    public int Count(ImportOutcome outcome) => Rows.Count(r => r.Outcome == outcome);
}

/// <summary>El emparejamiento de un fichero de ejemplo de otra aplicación para exportar en su formato.</summary>
public sealed record ExportTemplateAnalysis(
    ExchangeEntity Entity, IReadOnlyList<ColumnAnalysis> Columns, string? TemplateName, ExchangeDateFormat DateFormat);

public sealed record ExportFile(string FileName, byte[] Content);

/// <summary>
/// ADR 0010 (estudio de mercado R1): importación y exportación por ficheros para Administración, sin IA y sin guardar el fichero.
/// Importar reutiliza los casos de uso de siempre fila a fila (las mismas validaciones, disparadores, seguridad por filas y auditoría que
/// el alta a mano), así que nunca hace nada que Administración no pueda hacer desde sus pantallas. La vista previa y la importación
/// recalculan todo en el servidor a partir del fichero: no se fían de lo que la pantalla enseñó antes. Exportar es solo estructura y
/// censo administrativo (nunca contenido clínico), se descarga y queda auditado antes de entregar el fichero.
/// </summary>
public sealed class AdministracionIntercambioApplicationService(
    AdministrationAccessResolver access, ITableFileReader reader, IExchangeDirectory directory, IExchangeRepository repository,
    AdministracionEstructuraApplicationService estructura, CreateResident createResident)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public Task<ApplicationResult<ImportAnalysis>> AnalyzeImportAsync(
        AdministracionQuery query, ExchangeEntity entity, byte[] file, ExchangeMappingInput? input, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await AnalyzeAsync(grant, entity, reader.Read(file), input, ct);
        });

    public Task<ApplicationResult<ImportReport>> PreviewImportAsync(
        AdministracionQuery query, ExchangeEntity entity, byte[] file, ExchangeMappingInput input, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(() => ImportAsync(query, entity, file, input, write: false, templateName: null, ct));

    /// <summary>Importa las filas que la vista previa da por buenas. Si se da un nombre, guarda además el emparejamiento como plantilla
    /// del centro para la próxima vez.</summary>
    public Task<ApplicationResult<ImportReport>> RunImportAsync(
        AdministracionQuery query, ExchangeEntity entity, byte[] file, ExchangeMappingInput input, string? templateName,
        CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(() => ImportAsync(query, entity, file, input, write: true, templateName, ct));

    public Task<ApplicationResult<ExportTemplateAnalysis>> AnalyzeExportTemplateAsync(
        AdministracionQuery query, ExchangeEntity entity, byte[] file, ExchangeMappingInput? input, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await AnalyzeExportAsync(grant, entity, reader.Read(file), input, ct);
        });

    public Task<ApplicationResult<Guid>> SaveExportTemplateAsync(
        AdministracionQuery query, ExchangeEntity entity, byte[] file, ExchangeMappingInput input, string? name, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var table = reader.Read(file);
            var analysis = await AnalyzeExportAsync(grant, entity, table, input, ct);
            var fields = analysis.Columns.Select(c => c.Field).ToList();
            if (fields.All(f => f is null))
            {
                throw new DomainValidationException("EXCHANGE_EXPORT_EMPTY");
            }

            return await repository.SaveTemplateAsync(grant, ExchangeDirection.Exportar, entity, TemplateName(name), table.Headers,
                new ExchangeMapping(fields, analysis.DateFormat, new Dictionary<string, DocumentedSexCode>()), ct);
        });

    public Task<ApplicationResult<IReadOnlyList<ExchangeTemplate>>> ListExportTemplatesAsync(AdministracionQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            return await directory.ListTemplatesAsync(grant, ExchangeDirection.Exportar, ct);
        });

    /// <summary>La estructura o el censo en CSV, con las columnas de ResidApp o con las de una plantilla de exportación del centro. La
    /// exportación queda auditada antes de entregar el fichero: si no se puede auditar, no se exporta.</summary>
    public Task<ApplicationResult<ExportFile>> ExportAsync(
        AdministracionQuery query, ExchangeEntity entity, Guid? templateId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
            var template = templateId is { } id
                ? await directory.FindTemplateAsync(grant, id, ct) is { Direction: ExchangeDirection.Exportar } found
                    ? found
                    : throw new DomainValidationException("APPLICATION_INPUT_INVALID")
                : null;
            entity = template?.Entity ?? entity;
            var fields = template?.Mapping.Columns ?? ExchangeCatalog.ExportFields(entity).Select(f => (ExchangeField?)f).ToList();
            var headers = template?.Headers ?? ExchangeCatalog.ExportFields(entity).Select(f => ExchangeCatalog.Label(f)).ToList();
            var dateFormat = template?.Mapping.DateFormat ?? ExchangeDateFormat.DiaMesAnio;
            var rows = entity == ExchangeEntity.Estructura
                ? await StructureRowsAsync(query, ct)
                : (await directory.ListCensusAsync(grant, ct)).Select(r => CensusRow(r, dateFormat)).ToList();

            await repository.RecordRunAsync(grant, ExchangeDirection.Exportar, ct);
            var content = CsvExport.Write(headers, rows.Select(values => fields
                .Select(f => f is { } field && values.TryGetValue(field, out var value) ? value : null).ToList()));
            return new ExportFile($"residapp-{(entity == ExchangeEntity.Estructura ? "estructura" : "residentes")}-{Today:yyyyMMdd}.csv", content);
        });

    /// <summary>La plantilla vacía de ResidApp para rellenar e importar: las mismas columnas que la exportación, cuyos nombres ya se
    /// emparejan solos.</summary>
    public static ExportFile ImportTemplate(ExchangeEntity entity) => new(
        $"plantilla-{(entity == ExchangeEntity.Estructura ? "estructura" : "residentes")}.csv",
        CsvExport.Write(ExchangeCatalog.ExportFields(entity).Select(f => ExchangeCatalog.Label(f)).ToList(), []));

    private async Task<ImportReport> ImportAsync(
        AdministracionQuery query, ExchangeEntity entity, byte[] file, ExchangeMappingInput input, bool write, string? templateName,
        CancellationToken ct)
    {
        var (grant, _) = await access.ResolveAsync(query.AmbitoPerfilId, query.CentroId, ct);
        var table = reader.Read(file);
        var analysis = await AnalyzeAsync(grant, entity, table, input, ct);
        if (!analysis.Ready)
        {
            throw new DomainValidationException("EXCHANGE_MAPPING_INVALID");
        }

        var name = write && !string.IsNullOrWhiteSpace(templateName) ? TemplateName(templateName) : null;
        var rows = entity == ExchangeEntity.Estructura
            ? await ImportStructureAsync(query, table, analysis.Mapping, write, ct)
            : await ImportResidentsAsync(query, grant, table, analysis.Mapping, write, ct);
        if (write)
        {
            await repository.RecordRunAsync(grant, ExchangeDirection.Importar, ct);
            if (name is not null)
            {
                await repository.SaveTemplateAsync(grant, ExchangeDirection.Importar, entity, name, table.Headers, analysis.Mapping, ct);
            }
        }

        return new ImportReport(entity, rows, write);
    }

    private async Task<ImportAnalysis> AnalyzeAsync(
        AccountAdministrationAccess grant, ExchangeEntity entity, ExchangeTable table, ExchangeMappingInput? input, CancellationToken ct)
    {
        var today = Today;
        var auto = ColumnMatcher.Match(entity, table, today);
        var template = await directory.FindTemplateAsync(grant, ExchangeDirection.Importar, entity, ExchangeCatalog.Signature(table.Headers), ct);
        if (template is not null && template.Mapping.Columns.Count != table.Headers.Count)
        {
            template = null;
        }

        var specs = ExchangeCatalog.Fields(entity);
        var fields = input is null
            ? template?.Mapping.Columns ?? auto.Select(m => m.Field).ToList()
            : ParseFields(input.Fields, table.Headers.Count, specs.Select(s => s.Field).ToList());
        var columns = Columns(table, fields, auto, template, today);

        var problems = new List<string>();
        var notices = new List<string>();
        foreach (var spec in specs)
        {
            var count = fields.Count(f => f == spec.Field);
            if (spec.Required && count == 0)
            {
                problems.Add($"Falta elegir la columna de «{ExchangeCatalog.Label(spec.Field)}».");
            }
            else if (!spec.Joinable && count > 1)
            {
                problems.Add($"«{ExchangeCatalog.Label(spec.Field)}» está en más de una columna: deja solo una.");
            }
        }

        // Fechas: el formato elegido, el de la plantilla o el primero con que se leen todas.
        var dateValues = ColumnValues(table, fields, ExchangeField.FechaNacimiento);
        var dateFormats = ExchangeDates.Candidates(dateValues, today);
        var dateFormat = input?.DateFormat is { } code && EnumCode.TryParseCode<ExchangeDateFormat>(code, out var chosen) ? chosen
            : input is null && template is not null ? template.Mapping.DateFormat
            : dateFormats.Count > 0 ? dateFormats[0] : ExchangeDateFormat.DiaMesAnio;
        if (fields.Contains(ExchangeField.FechaNacimiento))
        {
            if (dateFormats.Count == 0)
            {
                notices.Add("Hay fechas de nacimiento que no se entienden con ningún formato: esas filas saldrán con error en la vista previa.");
            }
            else if (ExchangeDates.Ambiguous(dateValues, today))
            {
                notices.Add("Las fechas se pueden leer como día/mes/año y como mes/día/año: comprueba el formato y las fechas de la vista previa.");
            }
        }

        // Sexo: lo elegido, lo de la plantilla o lo que se reconoce; lo que falte, a mano.
        var sexValues = new List<SexValue>();
        if (entity == ExchangeEntity.Residentes && !fields.Contains(ExchangeField.Sexo))
        {
            notices.Add("No hay columna de sexo: todos los residentes quedarán con «No consta».");
        }
        else if (fields.Contains(ExchangeField.Sexo))
        {
            var distinct = ColumnValues(table, fields, ExchangeField.Sexo)
                .Select(v => (Value: v, Key: ExchangeText.Normalize(v))).Where(v => v.Key.Length > 0)
                .GroupBy(v => v.Key).Select(g => (g.First().Value, g.Key)).ToList();
            if (distinct.Count > ExchangeLimits.MaxSexValues)
            {
                problems.Add($"La columna de sexo tiene más de {ExchangeLimits.MaxSexValues} valores distintos: comprueba que es la columna correcta.");
            }
            else
            {
                var keys = distinct.Select(v => v.Key).ToList();
                foreach (var (value, key) in distinct)
                {
                    var given = input?.SexValues.FirstOrDefault(s => s.Key == key);
                    var sex = given is not null
                        ? given.Code is { Length: > 0 } c && EnumCode.TryParseCode<DocumentedSexCode>(c, out var picked) ? picked : (DocumentedSexCode?)null
                        : input is null && template is not null && template.Mapping.SexValues.TryGetValue(key, out var stored) ? stored
                        : ExchangeSex.Suggest(key, keys);
                    sexValues.Add(new SexValue(value, key, sex));
                    if (sex is null)
                    {
                        problems.Add($"Falta decir qué sexo es «{value}».");
                    }
                }
            }
        }

        return new ImportAnalysis(entity, table.Rows.Count, columns, template?.Name, dateFormats, dateFormat, sexValues, problems, notices);
    }

    private async Task<ExportTemplateAnalysis> AnalyzeExportAsync(
        AccountAdministrationAccess grant, ExchangeEntity entity, ExchangeTable table, ExchangeMappingInput? input, CancellationToken ct)
    {
        var today = Today;
        var allowed = ExchangeCatalog.ExportFields(entity);
        // Al exportar no hay «Apellidos» (ResidApp guarda el nombre completo) ni se mira el formato de los datos de ejemplo.
        var auto = ColumnMatcher.Match(entity, table, today)
            .Select(m => m is { Field: { } f, Origin: MatchOrigin.Nombre } && allowed.Contains(f) ? m : new ColumnMatch(null, null)).ToList();
        var template = await directory.FindTemplateAsync(grant, ExchangeDirection.Exportar, entity, ExchangeCatalog.Signature(table.Headers), ct);
        if (template is not null && template.Mapping.Columns.Count != table.Headers.Count)
        {
            template = null;
        }

        var fields = input is null
            ? template?.Mapping.Columns ?? auto.Select(m => m.Field).ToList()
            : ParseFields(input.Fields, table.Headers.Count, allowed);
        var dateFormat = input?.DateFormat is { } code && EnumCode.TryParseCode<ExchangeDateFormat>(code, out var chosen) ? chosen
            : template?.Mapping.DateFormat ?? ExchangeDateFormat.DiaMesAnio;
        return new ExportTemplateAnalysis(entity, Columns(table, fields, auto, template, today), template?.Name, dateFormat);
    }

    private static List<ColumnAnalysis> Columns(
        ExchangeTable table, IReadOnlyList<ExchangeField?> fields, IReadOnlyList<ColumnMatch> auto, ExchangeTemplate? template, DateOnly today) =>
        table.Headers.Select((header, i) =>
        {
            var values = table.Column(i).ToList();
            var origin = fields[i] is null ? null
                : template is not null && template.Mapping.Columns[i] == fields[i] ? MatchOrigin.Plantilla
                : auto[i].Field == fields[i] ? auto[i].Origin
                : null;
            return new ColumnAnalysis(i, header, ColumnMatcher.Kind(values, today), values.Where(v => v.Length > 0).Distinct().Count(), fields[i], origin);
        }).ToList();

    /// <summary>Los campos elegidos en la pantalla: uno por columna del fichero y del catálogo de la entidad; si no, la pantalla y el
    /// fichero no coinciden (otro fichero) y es entrada inválida.</summary>
    private static List<ExchangeField?> ParseFields(IReadOnlyList<string?> codes, int columns, IReadOnlyList<ExchangeField> allowed) =>
        codes.Count != columns
            ? throw new DomainValidationException("EXCHANGE_MAPPING_INVALID")
            : codes.Select(code => string.IsNullOrEmpty(code) ? (ExchangeField?)null
                : EnumCode.TryParseCode<ExchangeField>(code, out var field) && allowed.Contains(field) ? field
                : throw new DomainValidationException("EXCHANGE_MAPPING_INVALID")).ToList();

    private static List<string> ColumnValues(ExchangeTable table, IReadOnlyList<ExchangeField?> fields, ExchangeField field) =>
        fields.Select((f, i) => (f, i)).Where(c => c.f == field).SelectMany(c => table.Column(c.i)).Where(v => v.Length > 0).ToList();

    private static string TemplateName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > ExchangeLimits.MaxTemplateNameLength
            ? throw new DomainValidationException("EXCHANGE_TEMPLATE_NAME_INVALID")
            : text;
    }

    private async Task<List<ImportRowReport>> ImportStructureAsync(
        AdministracionQuery query, ExchangeTable table, ExchangeMapping mapping, bool write, CancellationToken ct)
    {
        var import = new StructureImport(
            Unwrap(await estructura.ListStructureUnitsAsync(query, ct)), Unwrap(await estructura.ListBuildingsAsync(query, ct)),
            unitId => LoadRoomsAsync(query, unitId, ct), write ? new StructureWriter(estructura, query, ct) : null);
        var reports = new List<ImportRowReport>();
        foreach (var row in table.Rows)
        {
            reports.Add(await import.ApplyAsync(row, mapping));
        }

        return reports;
    }

    private async Task<List<ImportRowReport>> ImportResidentsAsync(
        AdministracionQuery query, AccountAdministrationAccess grant, ExchangeTable table, ExchangeMapping mapping, bool write, CancellationToken ct)
    {
        var planner = new ResidentImportPlanner(
            Unwrap(await estructura.ListStructureUnitsAsync(query, ct)), unitId => LoadRoomsAsync(query, unitId, ct),
            await directory.ListCensusAsync(grant, ct), Today);
        var reports = new List<ImportRowReport>();
        foreach (var row in table.Rows)
        {
            var (report, plan) = await planner.PlanAsync(row, mapping);
            reports.Add(write && plan is not null ? await CreateResidentAsync(query, report, plan, ct) : report);
        }

        return reports;
    }

    private async Task<ImportRowReport> CreateResidentAsync(AdministracionQuery query, ImportRowReport report, ResidentImportPlan plan, CancellationToken ct)
    {
        var result = await createResident.ExecuteAsync(new CreateResidentCommand(
            query.AmbitoPerfilId, query.CentroId, plan.UnitId, plan.Name, plan.BirthDate, plan.Sex, plan.Reference, EdificioId: null, PlantaId: null,
            plan.RoomId, plan.PlaceId, ResidentOperationId(query.CentroId, plan), UbicacionPendiente: true), ct);
        return result.Ok
            ? report with { Detail = $"Dado de alta ({ExchangeCatalog.Label(plan.Sex)})." }
            : report with
            {
                Outcome = ImportOutcome.Error,
                Detail = result.Error!.Code switch
                {
                    ApplicationFailureCode.Conflict => "No se ha dado de alta: la plaza ya está ocupada o el alta choca con otra anterior.",
                    ApplicationFailureCode.AccessDenied => "No se ha dado de alta: no puedes dar de alta residentes en esa unidad.",
                    ApplicationFailureCode.InvalidInput => "No se ha dado de alta: revisa los datos de la fila.",
                    _ => "No se ha podido dar de alta ahora mismo. Vuelve a importar el fichero más tarde: lo ya dado de alta no se repite.",
                },
            };
    }

    /// <summary>El identificador de operación del alta: el mismo para la misma fila resuelta, así que un segundo envío de la misma
    /// importación (doble clic, reintento) devuelve el alta ya hecha en lugar de duplicarla.</summary>
    private static Guid ResidentOperationId(CenterId centerId, ResidentImportPlan plan) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', "RESIDENT_IMPORT", centerId.Value, plan.UnitId.Value, plan.RoomId, plan.PlaceId,
            plan.Name, plan.BirthDate.ToString("yyyy-MM-dd"), plan.Sex.ToCode(), plan.Reference)))[..16]);

    private async Task<IReadOnlyList<LayoutRoom>> LoadRoomsAsync(AdministracionQuery query, Guid unitId, CancellationToken ct) =>
        Unwrap(await estructura.ListRoomsAsync(new ListRoomsQuery(query.AmbitoPerfilId, query.CentroId, UnitId.From(unitId)), ct));

    private async Task<List<Dictionary<ExchangeField, string?>>> StructureRowsAsync(AdministracionQuery query, CancellationToken ct)
    {
        var rows = new List<Dictionary<ExchangeField, string?>>();
        foreach (var unit in Unwrap(await estructura.ListStructureUnitsAsync(query, ct)).Where(u => u.Active).OrderBy(u => u.Name))
        {
            Dictionary<ExchangeField, string?> Row(string? room = null, string? place = null) => new()
            {
                [ExchangeField.Unidad] = unit.Name, [ExchangeField.CodigoUnidad] = unit.Code, [ExchangeField.Edificio] = unit.BuildingName,
                [ExchangeField.Planta] = unit.FloorName, [ExchangeField.Habitacion] = room, [ExchangeField.Plaza] = place,
            };

            var rooms = (await LoadRoomsAsync(query, unit.UnitId.Value, ct)).Where(r => r.Active).ToList();
            if (rooms.Count == 0)
            {
                rows.Add(Row());
            }

            foreach (var room in rooms)
            {
                var places = room.Places.Where(p => p.Active).ToList();
                rows.AddRange(places.Count == 0 ? [Row(room.Name)] : places.Select(p => Row(room.Name, p.Name)));
            }
        }

        return rows;
    }

    private static Dictionary<ExchangeField, string?> CensusRow(CensusEntry entry, ExchangeDateFormat dateFormat) => new()
    {
        [ExchangeField.Nombre] = entry.DisplayName, [ExchangeField.FechaNacimiento] = ExchangeDates.Format(entry.BirthDate, dateFormat),
        [ExchangeField.Sexo] = ExchangeCatalog.Label(entry.Sex), [ExchangeField.Unidad] = entry.UnitName, [ExchangeField.Habitacion] = entry.RoomName,
        [ExchangeField.Plaza] = entry.PlaceName, [ExchangeField.ReferenciaInterna] = entry.InternalReference,
    };

    /// <summary>El valor de una lectura de Administración, o su mismo fallo (acceso denegado, entrada inválida o no disponible).</summary>
    private static T Unwrap<T>(ApplicationResult<T> result) => result.Ok
        ? result.Value!
        : result.Error!.Code switch
        {
            ApplicationFailureCode.AccessDenied => throw new AccessDeniedException(),
            ApplicationFailureCode.InvalidInput => throw new DomainValidationException("APPLICATION_INPUT_INVALID"),
            _ => throw new InvalidOperationException("EXCHANGE_READ_UNAVAILABLE"),
        };

    /// <summary>Las escrituras de la importación de estructura con los casos de uso de Administración. Un fallo se traduce en el motivo
    /// para quien importa.</summary>
    private sealed class StructureWriter(AdministracionEstructuraApplicationService estructura, AdministracionQuery query, CancellationToken ct)
        : IStructureWriter
    {
        /// <summary>Un código derivado del nombre puede estar ya en una unidad fuera del ámbito: se prueba con «-2»… «-5».</summary>
        public async Task<Guid> CreateUnitAsync(string code, string name, bool derivedCode)
        {
            ApplicationResult<UnitId>? result = null;
            for (var attempt = 1; attempt <= (derivedCode ? 5 : 1); attempt++)
            {
                result = await estructura.CreateUnitAsync(new CreateUnitCommand(
                    query.AmbitoPerfilId, query.CentroId, Guid.NewGuid(), attempt == 1 ? code : $"{code}-{attempt}", name), ct);
                if (result.Ok || result.Error!.Code != ApplicationFailureCode.Conflict)
                {
                    break;
                }
            }

            return Value(result!, "la unidad").Value;
        }

        public async Task<Guid> CreateBuildingAsync(string name) =>
            Value(await estructura.CreateBuildingAsync(new CreateBuildingCommand(query.AmbitoPerfilId, query.CentroId, Guid.NewGuid(), name), ct), "el edificio");

        public async Task<Guid> CreateFloorAsync(Guid buildingId, string name) =>
            Value(await estructura.CreateFloorAsync(
                new CreateFloorCommand(query.AmbitoPerfilId, query.CentroId, Guid.NewGuid(), buildingId, name), ct), "la planta");

        public async Task SetUnitLocationAsync(Guid unitId, Guid buildingId, Guid? floorId)
        {
            var result = await estructura.SetUnitLocationAsync(
                new SetUnitLocationCommand(query.AmbitoPerfilId, query.CentroId, UnitId.From(unitId), buildingId, floorId), ct);
            if (!result.Ok)
            {
                throw new ImportStepException("No se ha podido colocar la unidad en el edificio y la planta.");
            }
        }

        public async Task<Guid> CreateRoomAsync(Guid unitId, string name) =>
            Value(await estructura.CreateRoomAsync(
                new CreateRoomCommand(query.AmbitoPerfilId, query.CentroId, Guid.NewGuid(), UnitId.From(unitId), name), ct), "la habitación");

        public async Task<Guid> CreatePlaceAsync(Guid roomId, string name) =>
            Value(await estructura.CreatePlaceAsync(new CreatePlaceCommand(query.AmbitoPerfilId, query.CentroId, Guid.NewGuid(), roomId, name), ct), "la plaza");

        private static T Value<T>(ApplicationResult<T> result, string what) => result.Ok
            ? result.Value!
            : throw new ImportStepException(result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict => $"No se ha podido crear {what}: ya existe con ese nombre o código en el centro (puede que fuera de tu ámbito).",
                ApplicationFailureCode.InvalidInput => $"No se ha podido crear {what}: revisa el nombre.",
                ApplicationFailureCode.AccessDenied => $"No se ha podido crear {what}: no tienes acceso.",
                _ => $"No se ha podido crear {what} ahora mismo. Vuelve a importar el fichero más tarde: lo ya creado no se repite.",
            });
    }
}
