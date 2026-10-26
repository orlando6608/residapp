using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Domain.Structure;
using ResidApp.Shared;

namespace ResidApp.Application.Intercambio;

public enum ImportOutcome
{
    [Display(Name = "Nueva")] Nueva,
    [Display(Name = "Ya existe")] YaExiste,
    [Display(Name = "Posible duplicado")] PosibleDuplicado,
    [Display(Name = "Error")] Error,
}

/// <summary>Lo que pasa (en la vista previa) o ha pasado (al importar) con una fila del fichero. Label la identifica para quien importa;
/// Detail dice qué se crea, por qué ya existe o qué hay que corregir. CanConfirm es una fila con un posible duplicado que quien importa
/// puede decidir dar de alta de todos modos (CJ, importación 1.2); Confirmed, que ya lo ha decidido (entonces el resultado es Nueva).</summary>
public sealed record ImportRowReport(int Number, string Label, ImportOutcome Outcome, string Detail, bool CanConfirm = false, bool Confirmed = false);

/// <summary>Un paso de la importación que no se ha podido hacer, con el motivo para quien importa.</summary>
public sealed class ImportStepException(string message) : Exception(message);

/// <summary>Las escrituras de la importación de estructura. En la vista previa no hay escritor: lo nuevo recibe un id provisional y
/// no se guarda nada.</summary>
public interface IStructureWriter
{
    Task<Guid> CreateUnitAsync(string code, string name, bool derivedCode);
    Task<Guid> CreateBuildingAsync(string name);
    Task<Guid> CreateFloorAsync(Guid buildingId, string name);
    Task SetUnitLocationAsync(Guid unitId, Guid buildingId, Guid? floorId);
    Task<Guid> CreateRoomAsync(Guid unitId, string name);
    Task<Guid> CreatePlaceAsync(Guid roomId, string name);
}

/// <summary>Los valores de una fila según el emparejamiento: un campo de varias columnas las une con un espacio.</summary>
public static class ImportRowValues
{
    public static string? Value(ExchangeTableRow row, ExchangeMapping mapping, ExchangeField field)
    {
        var parts = mapping.Columns.Select((f, i) => (Field: f, Index: i)).Where(c => c.Field == field).Select(c => row.Cells[c.Index]);
        var text = string.Join(' ', string.Join(' ', parts).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : text;
    }

    public static bool TooLong(string? value) => value is { Length: > CenterLayout.MaxNameLength };
}

/// <summary>
/// ADR 0010: importación de la estructura, fila a fila y en orden jerárquico (unidad → edificio → planta → habitación → plaza). Lo que
/// ya existe se reconoce por el nombre normalizado (y la unidad, también por su código) y no se duplica; lo que falta se crea con los
/// casos de uso de siempre. Primero se comprueba la fila entera y después se escribe, así que una fila con errores no deja nada a
/// medias. La misma clase hace la vista previa (sin escritor) y la importación, para que digan lo mismo.
/// Reglas: una unidad que ya existe no se cambia de edificio ni de planta (solo se coloca si no tenía); la planta exige edificio y la
/// plaza exige habitación; lo inactivo no se reactiva ni se usa.
/// </summary>
public sealed class StructureImport
{
    private sealed class Unit(Guid id, string code, string name, bool active, Building? building, Floor? floor)
    {
        public Guid Id { get; } = id;
        public string Code { get; } = code;
        public string Name { get; } = name;
        public bool Active { get; } = active;
        public Building? Building { get; set; } = building;
        public Floor? Floor { get; set; } = floor;
        public List<Room>? Rooms { get; set; }
    }

    private sealed record Building(Guid Id, string Name, bool Active, List<Floor> Floors);

    private sealed record Floor(Guid Id, string Name, bool Active);

    private sealed record Room(Guid Id, string Name, bool Active, List<Place> Places);

    private sealed record Place(Guid Id, string Name, bool Active);

    private readonly List<Unit> _units;
    private readonly List<Building> _buildings;
    private readonly Func<Guid, Task<IReadOnlyList<LayoutRoom>>> _loadRooms;
    private readonly IStructureWriter? _writer;

    public StructureImport(
        IReadOnlyList<StructureUnit> units, IReadOnlyList<LayoutBuilding> buildings, Func<Guid, Task<IReadOnlyList<LayoutRoom>>> loadRooms,
        IStructureWriter? writer)
    {
        _buildings = buildings.Select(b => new Building(
            b.BuildingId, b.Name, b.Active, b.Floors.Select(f => new Floor(f.FloorId, f.Name, f.Active)).ToList())).ToList();
        _units = units.Select(u =>
        {
            var building = _buildings.FirstOrDefault(b => b.Id == u.BuildingId);
            return new Unit(u.UnitId.Value, u.Code, u.Name, u.Active, building, building?.Floors.FirstOrDefault(f => f.Id == u.FloorId));
        }).ToList();
        _loadRooms = loadRooms;
        _writer = writer;
    }

    public async Task<ImportRowReport> ApplyAsync(ExchangeTableRow row, ExchangeMapping mapping)
    {
        var unitName = ImportRowValues.Value(row, mapping, ExchangeField.Unidad);
        var unitCode = ImportRowValues.Value(row, mapping, ExchangeField.CodigoUnidad);
        var buildingName = ImportRowValues.Value(row, mapping, ExchangeField.Edificio);
        var floorName = ImportRowValues.Value(row, mapping, ExchangeField.Planta);
        var roomName = ImportRowValues.Value(row, mapping, ExchangeField.Habitacion);
        var placeName = ImportRowValues.Value(row, mapping, ExchangeField.Plaza);
        var label = string.Join(" · ", new[] { unitName, buildingName, floorName, roomName, placeName }.Where(v => v is not null));
        ImportRowReport Error(string detail) => new(row.Number, label.Length == 0 ? "(sin unidad)" : label, ImportOutcome.Error, detail);

        if (unitName is null)
        {
            return Error("Falta la unidad.");
        }

        if (new[] { unitName, buildingName, floorName, roomName, placeName }.Any(ImportRowValues.TooLong))
        {
            return Error($"Un nombre pasa de {CenterLayout.MaxNameLength} caracteres.");
        }

        if (unitCode is not null && !ValidCode(unitCode))
        {
            return Error("El código de unidad lleva de 2 a 64 caracteres: letras sin acentos, cifras, «-» o «_».");
        }

        if (floorName is not null && buildingName is null)
        {
            return Error("Para la planta hace falta el edificio.");
        }

        if (placeName is not null && roomName is null)
        {
            return Error("Para la plaza hace falta la habitación.");
        }

        // 1. Comprobar la fila entera sin escribir nada.
        var unit = FindUnit(unitName, unitCode, out var unitProblem);
        if (unitProblem is not null)
        {
            return Error(unitProblem);
        }

        var building = buildingName is null ? null : _buildings.FirstOrDefault(b => Same(b.Name, buildingName));
        var floor = building is null || floorName is null ? null : building.Floors.FirstOrDefault(f => Same(f.Name, floorName));
        var problem = unit is { Active: false } ? $"La unidad «{unit.Name}» está inactiva."
            : building is { Active: false } ? $"El edificio «{building.Name}» está inactivo."
            : floor is { Active: false } ? $"La planta «{floor.Name}» está inactiva."
            : unit?.Building is not null && buildingName is not null
              && (!Same(unit.Building.Name, buildingName) || (floorName is not null && !Same(unit.Floor?.Name, floorName)))
                ? $"La unidad «{unit.Name}» ya está en otro edificio o planta; si hay que cambiarla, hazlo desde «Estructura»."
            : null;
        var rooms = unit is null ? new List<Room>() : await RoomsAsync(unit);
        var room = roomName is null ? null : rooms.FirstOrDefault(r => Same(r.Name, roomName));
        var place = room is null || placeName is null ? null : room.Places.FirstOrDefault(p => Same(p.Name, placeName));
        problem ??= room is { Active: false } ? $"La habitación «{room.Name}» está inactiva."
            : place is { Active: false } ? $"La plaza «{place.Name}» está inactiva."
            : null;
        if (problem is not null)
        {
            return Error(problem);
        }

        // 2. Crear lo que falta, en orden.
        var created = new List<string>();
        try
        {
            if (unit is null)
            {
                var derived = unitCode is null;
                var code = unitCode?.ToUpperInvariant() ?? FreeCode(ExchangeText.UnitCode(unitName));
                unit = new Unit(await Create(w => w.CreateUnitAsync(code, unitName, derived)), code, unitName, true, null, null) { Rooms = rooms };
                _units.Add(unit);
                created.Add($"la unidad «{unitName}»");
            }

            if (buildingName is not null)
            {
                if (building is null)
                {
                    building = new Building(await Create(w => w.CreateBuildingAsync(buildingName)), buildingName, true, []);
                    _buildings.Add(building);
                    created.Add($"el edificio «{buildingName}»");
                }

                if (floorName is not null && floor is null)
                {
                    var buildingId = building.Id;
                    floor = new Floor(await Create(w => w.CreateFloorAsync(buildingId, floorName)), floorName, true);
                    building.Floors.Add(floor);
                    created.Add($"la planta «{floorName}»");
                }

                if (unit.Building is null)
                {
                    if (_writer is not null)
                    {
                        await _writer.SetUnitLocationAsync(unit.Id, building.Id, floor?.Id);
                    }

                    (unit.Building, unit.Floor) = (building, floor);
                    created.Add("su colocación en el edificio");
                }
            }

            if (roomName is not null)
            {
                if (room is null)
                {
                    var unitId = unit.Id;
                    room = new Room(await Create(w => w.CreateRoomAsync(unitId, roomName)), roomName, true, []);
                    rooms.Add(room);
                    created.Add($"la habitación «{roomName}»");
                }

                if (placeName is not null && place is null)
                {
                    var roomId = room.Id;
                    room.Places.Add(new Place(await Create(w => w.CreatePlaceAsync(roomId, placeName)), placeName, true));
                    created.Add($"la plaza «{placeName}»");
                }
            }
        }
        catch (ImportStepException failure)
        {
            return Error(created.Count == 0 ? failure.Message : $"{failure.Message} Sí se creó: {string.Join(", ", created)}.");
        }

        return created.Count == 0
            ? new ImportRowReport(row.Number, label, ImportOutcome.YaExiste, "Ya existe todo.")
            : new ImportRowReport(row.Number, label, ImportOutcome.Nueva, $"Se crea {string.Join(", ", created)}.");
    }

    private Unit? FindUnit(string name, string? code, out string? problem)
    {
        problem = null;
        var byName = _units.FirstOrDefault(u => Same(u.Name, name));
        var byCode = code is null ? null : _units.FirstOrDefault(u => string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null && byCode != byName)
        {
            problem = $"El código «{code}» ya es de la unidad «{byCode.Name}».";
        }
        else if (byName is not null && code is not null && byCode is null)
        {
            problem = $"La unidad «{byName.Name}» ya existe con el código «{byName.Code}».";
        }

        return byName;
    }

    private async Task<List<Room>> RoomsAsync(Unit unit) =>
        unit.Rooms ??= (await _loadRooms(unit.Id)).Select(r => new Room(
            r.RoomId, r.Name, r.Active, r.Places.Select(p => new Place(p.PlaceId, p.Name, p.Active)).ToList())).ToList();

    private async Task<Guid> Create(Func<IStructureWriter, Task<Guid>> create) => _writer is null ? Guid.NewGuid() : await create(_writer);

    /// <summary>Un código libre entre las unidades conocidas: el derivado del nombre o, si ya está, con «-2», «-3»…</summary>
    private string FreeCode(string code)
    {
        var candidate = code;
        for (var suffix = 2; _units.Any(u => string.Equals(u.Code, candidate, StringComparison.OrdinalIgnoreCase)); suffix++)
        {
            candidate = $"{code}-{suffix}";
        }

        return candidate;
    }

    private static bool ValidCode(string code) =>
        code.Length is >= CenterUnit.MinCodeLength and <= CenterUnit.MaxCodeLength
        && code.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_');

    internal static bool Same(string? stored, string? fromFile) => ExchangeText.Normalize(stored) == ExchangeText.Normalize(fromFile);
}

/// <summary>Un residente que la importación va a dar de alta, ya resuelto contra la estructura del centro.</summary>
public sealed record ResidentImportPlan(
    UnitId UnitId, Guid? RoomId, Guid? PlaceId, string Name, DateOnly BirthDate, DocumentedSexCode Sex, string? Reference,
    DateOnly? AdmissionDate = null, int? ConfirmedRow = null);

/// <summary>
/// ADR 0010: comprueba cada fila de residentes contra la estructura del ámbito y el censo, sin escribir. La importación de residentes no
/// crea estructura: la unidad (por nombre o código), la habitación y la plaza tienen que existir (se pueden importar antes, incluso
/// desde el mismo fichero). Un residente activo con la misma referencia interna ya existe y no se vuelve a dar de alta; dos filas con la
/// misma referencia en el fichero, o dos residentes en la misma plaza, son un error. Con el mismo nombre y fecha de nacimiento, pero
/// sin esa referencia, la fila es un posible duplicado (CJ, importación 1.2): no se importa salvo que quien importa lo confirme, fila a
/// fila (confirmedRows son los números de fila del fichero), porque dos personas distintas pueden llamarse igual.
/// </summary>
public sealed class ResidentImportPlanner(
    IReadOnlyList<StructureUnit> units, Func<Guid, Task<IReadOnlyList<LayoutRoom>>> loadRooms, IReadOnlyList<CensusEntry> census, DateOnly today,
    IReadOnlySet<int>? confirmedRows = null)
{
    private readonly Dictionary<Guid, IReadOnlyList<LayoutRoom>> _rooms = [];
    private readonly HashSet<string> _censusReferences = census.Where(c => c.InternalReference is not null)
        .Select(c => ExchangeText.Normalize(c.InternalReference)).ToHashSet();
    private readonly Dictionary<string, CensusEntry> _censusIdentities = census.GroupBy(c => Identity(c.DisplayName, c.BirthDate))
        .ToDictionary(g => g.Key, g => g.First());
    private readonly Dictionary<string, int> _fileKeys = [];
    private readonly HashSet<Guid> _placesTaken = [];

    public async Task<(ImportRowReport Report, ResidentImportPlan? Plan)> PlanAsync(ExchangeTableRow row, ExchangeMapping mapping)
    {
        var first = ImportRowValues.Value(row, mapping, ExchangeField.Nombre);
        var surnames = ImportRowValues.Value(row, mapping, ExchangeField.Apellidos);
        var name = string.Join(' ', new[] { first, surnames }.Where(v => v is not null));
        var birthText = ImportRowValues.Value(row, mapping, ExchangeField.FechaNacimiento);
        var sexText = ImportRowValues.Value(row, mapping, ExchangeField.Sexo);
        var unitText = ImportRowValues.Value(row, mapping, ExchangeField.Unidad);
        var roomText = ImportRowValues.Value(row, mapping, ExchangeField.Habitacion);
        var placeText = ImportRowValues.Value(row, mapping, ExchangeField.Plaza);
        var reference = ImportRowValues.Value(row, mapping, ExchangeField.ReferenciaInterna);
        var birth = ExchangeDates.Parse(birthText, mapping.DateFormat, today);
        var admissionText = ImportRowValues.Value(row, mapping, ExchangeField.FechaIngreso);
        var admission = ExchangeDates.Parse(admissionText, mapping.DateFormat, today);
        var label = string.Join(" · ", new[]
        {
            name.Length == 0 ? "(sin nombre)" : name, birth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? birthText,
            string.Join(", ", new[] { unitText, roomText, placeText }.Where(v => v is not null)),
        }.Where(v => !string.IsNullOrEmpty(v)));
        (ImportRowReport, ResidentImportPlan?) Report(ImportOutcome outcome, string detail) => (new ImportRowReport(row.Number, label, outcome, detail), null);

        // Una celda de sexo vacía (o solo con signos, como «?») es «No consta».
        var sexKey = ExchangeText.Normalize(sexText);
        var sex = DocumentedSexCode.NoConsta;
        var problem = name.Length == 0 ? "Falta el nombre."
            : name.Length > 200 ? "El nombre pasa de 200 caracteres."
            : birthText is null ? "Falta la fecha de nacimiento."
            : birth is null ? $"La fecha de nacimiento «{birthText}» no se entiende con el formato elegido."
            : birth > today ? "La fecha de nacimiento es posterior a hoy."
            : admissionText is not null && admission is null ? $"La fecha de ingreso «{admissionText}» no se entiende con el formato elegido."
            : admission > today ? "La fecha de ingreso es posterior a hoy."
            : admission < birth ? "La fecha de ingreso es anterior a la de nacimiento."
            : sexKey.Length > 0 && !mapping.SexValues.TryGetValue(sexKey, out sex) ? $"Falta decir qué sexo es «{sexText}»."
            : unitText is null ? "Falta la unidad."
            : placeText is not null && roomText is null ? "Para la plaza hace falta la habitación."
            : reference is { Length: > 200 } ? "La referencia interna pasa de 200 caracteres."
            : null;
        if (problem is not null)
        {
            return Report(ImportOutcome.Error, problem);
        }

        var referenceKey = reference is null ? null : "ref:" + ExchangeText.Normalize(reference);
        var identityKey = Identity(name, birth!.Value);
        if (referenceKey is not null && _censusReferences.Contains(referenceKey[4..]))
        {
            return Report(ImportOutcome.YaExiste, $"Ya hay un residente activo con la referencia interna «{reference}».");
        }

        if (referenceKey is not null && _fileKeys.TryGetValue(referenceKey, out var sameReference))
        {
            return Report(ImportOutcome.Error, $"Repite la referencia interna de la fila {sameReference}.");
        }

        // Mismo nombre y fecha de nacimiento sin la misma referencia: puede ser otra persona. Se avisa y decide quien importa.
        var confirmed = confirmedRows?.Contains(row.Number) == true;
        var homonym = _censusIdentities.TryGetValue(identityKey, out var resident)
            ? $"Coincide en nombre y fecha de nacimiento con un residente activo de la unidad «{resident.UnitName}», pero no en la referencia interna."
            : _fileKeys.TryGetValue(identityKey, out var earlier) ? $"Coincide en nombre y fecha de nacimiento con la fila {earlier} del fichero, pero no en la referencia interna."
            : null;
        if (homonym is not null && !confirmed)
        {
            return (new ImportRowReport(row.Number, label, ImportOutcome.PosibleDuplicado,
                $"{homonym} Marca «Importar de todos modos» si es otra persona.", CanConfirm: true), null);
        }

        var unit = units.FirstOrDefault(u => StructureImport.Same(u.Name, unitText))
                   ?? units.FirstOrDefault(u => string.Equals(u.Code, unitText, StringComparison.OrdinalIgnoreCase));
        if (unit is null || !unit.Active)
        {
            return Report(ImportOutcome.Error, unit is null
                ? $"La unidad «{unitText}» no está en tu ámbito. Si es nueva, impórtala antes como estructura (puede ser con este mismo fichero)."
                : $"La unidad «{unit.Name}» está inactiva.");
        }

        LayoutRoom? room = null;
        LayoutPlace? place = null;
        if (roomText is not null)
        {
            if (!_rooms.TryGetValue(unit.UnitId.Value, out var rooms))
            {
                _rooms[unit.UnitId.Value] = rooms = await loadRooms(unit.UnitId.Value);
            }

            room = rooms.FirstOrDefault(r => r.Active && StructureImport.Same(r.Name, roomText));
            place = room is null || placeText is null ? null : room.Places.FirstOrDefault(p => p.Active && StructureImport.Same(p.Name, placeText));
            problem = room is null ? $"La habitación «{roomText}» no existe (o está inactiva) en la unidad «{unit.Name}»."
                : placeText is not null && place is null ? $"La plaza «{placeText}» no existe (o está inactiva) en la habitación «{room.Name}»."
                : place is not null && (place.Occupied || _placesTaken.Contains(place.PlaceId)) ? $"La plaza «{place.Name}» ya está ocupada."
                : null;
            if (problem is not null)
            {
                return Report(ImportOutcome.Error, problem);
            }
        }

        foreach (var key in new[] { referenceKey, identityKey }.OfType<string>())
        {
            _fileKeys.TryAdd(key, row.Number);
        }

        if (place is not null)
        {
            _placesTaken.Add(place.PlaceId);
        }

        var detail = $"Se da de alta ({ExchangeCatalog.Label(sex)})." + (homonym is null ? "" : " Has marcado que es otra persona.");
        return (new ImportRowReport(row.Number, label, ImportOutcome.Nueva, detail, CanConfirm: homonym is not null, Confirmed: homonym is not null),
            new ResidentImportPlan(unit.UnitId, room?.RoomId, place?.PlaceId, name, birth.Value, sex, reference, admission,
                homonym is null ? null : row.Number));
    }

    private static string Identity(string name, DateOnly birth) => $"id:{ExchangeText.Normalize(name)}|{birth:yyyy-MM-dd}";
}
