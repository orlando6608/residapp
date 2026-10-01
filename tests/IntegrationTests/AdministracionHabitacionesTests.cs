using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, estructura del centro (historia 2, script 0029), fase 2: habitaciones y plazas de una unidad, y elegirlas (opcional) al dar
/// de alta a un residente. Cada prueba crea su propio centro.</summary>
public class AdministracionHabitacionesTests
{
    private static async Task<Guid> RoomAsync(SeededProfile admin, string name, UnitId? unit = null, Guid? operationId = null) =>
        (await BuildEstructura(admin.ExternalSubject).CreateRoomAsync(
            new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), unit ?? admin.UnitId, name))).Value;

    private static async Task<Guid> PlaceAsync(SeededProfile admin, Guid room, string name, Guid? operationId = null) =>
        (await BuildEstructura(admin.ExternalSubject).CreatePlaceAsync(
            new CreatePlaceCommand(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), room, name))).Value;

    private static async Task<IReadOnlyList<LayoutRoom>> ListAsync(SeededProfile admin, UnitId? unit = null) =>
        (await BuildEstructura(admin.ExternalSubject).ListRoomsAsync(new ListRoomsQuery(admin.ProfileScopeId, admin.CenterId, unit ?? admin.UnitId))).Value!;

    private static async Task<int> CountAuditAsync(Guid resourceId, string action, UnitId unit)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @Id AND accion_codigo = @Action AND unidad_id = @Unit AND perfil_activo = 'ADMINISTRACION'",
            new { Id = resourceId, Action = action, Unit = unit.Value });
    }

    private static CreateResidentInput Input(
        SeededProfile admin, Guid? room, Guid? place, UnitId? unit = null, Guid? building = null, Guid? floor = null, Guid? operationId = null) =>
        new(admin.AccountId, SystemProfile.Administracion, admin.CenterId, unit ?? admin.UnitId, "Residente ubicación (ficticio)",
            new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta, null, building, floor, room, place, operationId ?? Guid.NewGuid());

    private static Task<CreateResidentResult> CreateAsync(CreateResidentInput input) =>
        new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(input);

    private static async Task<(Guid? Building, Guid? Floor, Guid? Room, Guid? Place)> LocationOfAsync(Guid intervalId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync("SELECT edificio_id AS B, planta_id AS F, habitacion_id AS R, plaza_id AS P FROM dbo.intervalos_ubicacion_residente WHERE id = @Id", new { Id = intervalId });
        return ((Guid?)row.B, (Guid?)row.F, (Guid?)row.R, (Guid?)row.P);
    }

    [Fact]
    public async Task CrearHabitacionYPlaza_LasDejaActivas_ConAuditoriaDeLaUnidad_YElReenvioNoLasDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var roomOperation = Guid.NewGuid();
        var placeOperation = Guid.NewGuid();

        var room = await RoomAsync(admin, "  Habitación 12 ", operationId: roomOperation);
        var again = await service.CreateRoomAsync(new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, roomOperation, admin.UnitId, "Habitación 12"));
        var reused = await service.CreateRoomAsync(new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, roomOperation, admin.UnitId, "Otra"));
        var place = await PlaceAsync(admin, room, "Cama A", placeOperation);
        var placeAgain = await service.CreatePlaceAsync(new CreatePlaceCommand(admin.ProfileScopeId, admin.CenterId, placeOperation, room, "Cama A"));

        Assert.Equal(roomOperation, room);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        Assert.True(placeAgain.Ok, placeAgain.Error?.Message);
        var listed = Assert.Single(await ListAsync(admin));
        Assert.Equal(("Habitación 12", true, 0), (listed.Name, listed.Active, listed.CurrentResidents));
        var listedPlace = Assert.Single(listed.Places);
        Assert.Equal((place, "Cama A", true, false), (listedPlace.PlaceId, listedPlace.Name, listedPlace.Active, listedPlace.Occupied));
        Assert.Equal(1, await CountAuditAsync(room, "ROOM_CREATE", admin.UnitId));
        Assert.Equal(1, await CountAuditAsync(place, "PLACE_CREATE", admin.UnitId));
    }

    [Fact]
    public async Task Crear_RechazaNombresRepetidosOInvalidos_UnidadesInactivasOAjenas_YEnfermeria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var service = BuildEstructura(admin.ExternalSubject);
        var room = await RoomAsync(admin, "Habitación 1");
        await PlaceAsync(admin, room, "Cama A");
        var inactiveRoom = await RoomAsync(admin, "Cerrada");
        Assert.True((await service.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(admin.ProfileScopeId, admin.CenterId, inactiveRoom, false))).Ok);
        var inactiveUnit = (await service.CreateUnitAsync(new CreateUnitCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), $"hab-{Guid.NewGuid():N}"[..14], "Unidad cerrada"))).Value;
        Assert.True((await service.ChangeUnitStatusAsync(new ChangeUnitStatusCommand(admin.ProfileScopeId, admin.CenterId, inactiveUnit, false))).Ok);
        Task<ApplicationResult<Guid>> NewRoom(string? name, UnitId? unit = null) =>
            service.CreateRoomAsync(new CreateRoomCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), unit ?? admin.UnitId, name));
        Task<ApplicationResult<Guid>> NewPlace(Guid roomId, string? name) =>
            service.CreatePlaceAsync(new CreatePlaceCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), roomId, name));

        var results = new[]
        {
            (await NewRoom("habitación 1")).Error,
            (await NewRoom("  ")).Error,
            (await NewRoom(new string('a', 201))).Error,
            (await NewRoom("Nueva", inactiveUnit)).Error,
            (await NewRoom("Nueva", other.UnitId)).Error,
            (await NewPlace(room, "cama a")).Error,
            (await NewPlace(room, "")).Error,
            (await NewPlace(inactiveRoom, "Cama B")).Error,
            (await NewPlace(Guid.NewGuid(), "Cama B")).Error,
            (await BuildEstructura(other.ExternalSubject).CreatePlaceAsync(new CreatePlaceCommand(other.ProfileScopeId, other.CenterId, Guid.NewGuid(), room, "Cama Z"))).Error,
            (await BuildEstructura(nurse.ExternalSubject).CreateRoomAsync(new CreateRoomCommand(nurse.ProfileScopeId, nurse.CenterId, Guid.NewGuid(), nurse.UnitId, "Intento"))).Error,
            (await BuildEstructura(nurse.ExternalSubject).ListRoomsAsync(new ListRoomsQuery(nurse.ProfileScopeId, nurse.CenterId, nurse.UnitId))).Error,
        };

        Assert.Equal(
            [
                ApplicationFailureCode.Conflict, ApplicationFailureCode.InvalidInput, ApplicationFailureCode.InvalidInput,
                ApplicationFailureCode.InvalidInput, ApplicationFailureCode.AccessDenied, ApplicationFailureCode.Conflict,
                ApplicationFailureCode.InvalidInput, ApplicationFailureCode.InvalidInput, ApplicationFailureCode.AccessDenied,
                ApplicationFailureCode.AccessDenied, ApplicationFailureCode.AccessDenied, ApplicationFailureCode.AccessDenied,
            ],
            results.Select(e => e!.Code));
        Assert.Equal(2, (await ListAsync(admin)).Count);
    }

    [Fact]
    public async Task RenombrarYCambiarEstado_ConAuditoria_YReactivarUnaPlazaExigeLaHabitacionActiva()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var room = await RoomAsync(admin, "Habitación 1");
        var place = await PlaceAsync(admin, room, "Cama A");
        await PlaceAsync(admin, room, "Cama B");
        Task<ApplicationResult<bool>> Room(bool active) => service.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(admin.ProfileScopeId, admin.CenterId, room, active));
        Task<ApplicationResult<bool>> Place(bool active) => service.ChangePlaceStatusAsync(new ChangePlaceStatusCommand(admin.ProfileScopeId, admin.CenterId, place, active));

        var renamed = await service.RenameRoomAsync(new RenameRoomCommand(admin.ProfileScopeId, admin.CenterId, room, " Habitación 1 bis "));
        var same = await service.RenameRoomAsync(new RenameRoomCommand(admin.ProfileScopeId, admin.CenterId, room, "Habitación 1 bis"));
        var placeRenamed = await service.RenamePlaceAsync(new RenamePlaceCommand(admin.ProfileScopeId, admin.CenterId, place, "Cama 1"));
        var placeTaken = await service.RenamePlaceAsync(new RenamePlaceCommand(admin.ProfileScopeId, admin.CenterId, place, "cama b"));
        var placeOff = await Place(false);
        var placeOffAgain = await Place(false);
        var roomOff = await Room(false);
        var placeOnUnderInactiveRoom = await Place(true);
        var roomOn = await Room(true);
        var placeOn = await Place(true);

        Assert.True(renamed.Ok, renamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.True(placeRenamed.Ok, placeRenamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, placeTaken.Error!.Code);
        Assert.True(placeOff.Ok, placeOff.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, placeOffAgain.Error!.Code);
        Assert.True(roomOff.Ok, roomOff.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, placeOnUnderInactiveRoom.Error!.Code);
        Assert.True(roomOn.Ok, roomOn.Error?.Message);
        Assert.True(placeOn.Ok, placeOn.Error?.Message);
        Assert.Equal("Habitación 1 bis", Assert.Single(await ListAsync(admin)).Name);
        foreach (var (id, action) in new[]
        {
            (room, "ROOM_RENAME"), (room, "ROOM_DEACTIVATE"), (room, "ROOM_ACTIVATE"),
            (place, "PLACE_RENAME"), (place, "PLACE_DEACTIVATE"), (place, "PLACE_ACTIVATE"),
        })
        {
            Assert.Equal(1, await CountAuditAsync(id, action, admin.UnitId));
        }
    }

    [Fact]
    public async Task Alta_ConPlaza_GuardaHabitacionPlazaYElEdificioYPlantaDeLaUnidad_IgnoraLosDelCliente_YSeVeEnLaFicha()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var building = (await service.CreateBuildingAsync(new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), "Norte"))).Value;
        var floor = (await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), building, "Planta 1"))).Value;
        Assert.True((await service.SetUnitLocationAsync(new SetUnitLocationCommand(admin.ProfileScopeId, admin.CenterId, admin.UnitId, building, floor))).Ok);
        var room = await RoomAsync(admin, "Habitación 12");
        var place = await PlaceAsync(admin, room, "Cama A");

        var withPlace = await CreateAsync(Input(admin, null, place, building: Guid.NewGuid(), floor: Guid.NewGuid()));
        var withRoomOnly = await CreateAsync(Input(admin, room, null));
        var withNothing = await CreateAsync(Input(admin, null, null));

        Assert.Equal((building, floor, room, place), await LocationOfAsync(withPlace.LocationIntervalId));
        Assert.Equal((building, floor, room, (Guid?)null), await LocationOfAsync(withRoomOnly.LocationIntervalId));
        Assert.Equal((building, floor, (Guid?)null, (Guid?)null), await LocationOfAsync(withNothing.LocationIntervalId));
        var detail = (await Build(admin.ExternalSubject).FindResidentAsync(
            new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, withPlace.ResidentId))).Value!;
        var location = Assert.Single(detail.Locations);
        Assert.Equal(("Habitación 12", "Cama A"), (location.RoomName, location.PlaceName));
        var listed = Assert.Single(await ListAsync(admin));
        Assert.Equal(2, listed.CurrentResidents);
        Assert.True(Assert.Single(listed.Places).Occupied);
    }

    [Fact]
    public async Task Alta_ConPlazaOcupada_Inactiva_AjenaOInexistente_SeRechaza_YUnReenvioDevuelveElMismoAlta()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var otherUnit = (await service.CreateUnitAsync(new CreateUnitCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), $"hab-{Guid.NewGuid():N}"[..14], "Otra unidad"))).Value;
        var room = await RoomAsync(admin, "Habitación 1");
        var place = await PlaceAsync(admin, room, "Cama A");
        var inactivePlace = await PlaceAsync(admin, room, "Cama B");
        Assert.True((await service.ChangePlaceStatusAsync(new ChangePlaceStatusCommand(admin.ProfileScopeId, admin.CenterId, inactivePlace, false))).Ok);
        var closedRoom = await RoomAsync(admin, "Cerrada");
        var placeInClosedRoom = await PlaceAsync(admin, closedRoom, "Cama C");
        Assert.True((await service.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(admin.ProfileScopeId, admin.CenterId, closedRoom, false))).Ok);
        var foreignRoom = await RoomAsync(admin, "De la otra unidad", otherUnit);
        var foreignPlace = await PlaceAsync(admin, foreignRoom, "Cama D");
        var operation = Guid.NewGuid();

        var first = await CreateAsync(Input(admin, null, place, operationId: operation));
        var resend = await CreateAsync(Input(admin, null, place, operationId: operation));
        async Task<string> Failure(CreateResidentInput input) => (await Assert.ThrowsAsync<DomainValidationException>(() => CreateAsync(input))).Message;

        Assert.Equal(first, resend);
        Assert.Equal("PLACE_OCCUPIED", await Failure(Input(admin, null, place)));
        Assert.Equal("PLACE_INVALID", await Failure(Input(admin, null, inactivePlace)));
        Assert.Equal("PLACE_INVALID", await Failure(Input(admin, null, placeInClosedRoom)));
        Assert.Equal("PLACE_INVALID", await Failure(Input(admin, null, foreignPlace)));
        Assert.Equal("PLACE_INVALID", await Failure(Input(admin, null, Guid.NewGuid())));
        Assert.Equal("PLACE_INVALID", await Failure(Input(admin, foreignRoom, place)));
        Assert.Equal("ROOM_INVALID", await Failure(Input(admin, closedRoom, null)));
        Assert.Equal("ROOM_INVALID", await Failure(Input(admin, foreignRoom, null)));
        Assert.Equal("ROOM_INVALID", await Failure(Input(admin, Guid.NewGuid(), null)));
        // Un alta rechazada no deja nada a medias.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente WHERE centro_id = @Center", new { Center = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Alta_DosALaVezEnLaMismaPlaza_SoloUnaGana_YLaOtraEsPlazaOcupada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var room = await RoomAsync(admin, "Habitación 1");
        var place = await PlaceAsync(admin, room, "Cama A");

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try
            {
                await CreateAsync(Input(admin, null, place));
                return "ok";
            }
            catch (DomainValidationException error)
            {
                return error.Message;
            }
        }));

        Assert.Equal(1, attempts.Count(a => a == "ok"));
        Assert.All(attempts.Where(a => a != "ok"), a => Assert.Equal("PLACE_OCCUPIED", a));
    }

    [Fact]
    public async Task InactivarHabitacionOPlaza_NoVaConResidentesUbicados()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var room = await RoomAsync(admin, "Habitación 1");
        var place = await PlaceAsync(admin, room, "Cama A");
        var other = await RoomAsync(admin, "Habitación 2");
        await CreateAsync(Input(admin, null, place));
        await CreateAsync(Input(admin, other, null));

        var placeOff = await service.ChangePlaceStatusAsync(new ChangePlaceStatusCommand(admin.ProfileScopeId, admin.CenterId, place, false));
        var roomOff = await service.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(admin.ProfileScopeId, admin.CenterId, room, false));
        var otherOff = await service.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(admin.ProfileScopeId, admin.CenterId, other, false));

        Assert.Equal(ApplicationFailureCode.InvalidInput, placeOff.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, roomOff.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, otherOff.Error!.Code);
    }

    [Fact]
    public async Task OpcionesDelAlta_SoloHabitacionesYPlazasLibresYActivasDeLaUnidadDelAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var auxiliary = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var service = BuildEstructura(admin.ExternalSubject);
        var otherUnit = (await service.CreateUnitAsync(new CreateUnitCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), $"hab-{Guid.NewGuid():N}"[..14], "Otra unidad"))).Value;
        var room = await RoomAsync(admin, "Habitación 1");
        var free = await PlaceAsync(admin, room, "Cama libre");
        var occupied = await PlaceAsync(admin, room, "Cama ocupada");
        var inactive = await PlaceAsync(admin, room, "Cama inactiva");
        Assert.True((await service.ChangePlaceStatusAsync(new ChangePlaceStatusCommand(admin.ProfileScopeId, admin.CenterId, inactive, false))).Ok);
        var closed = await RoomAsync(admin, "Cerrada");
        Assert.True((await service.ChangeRoomStatusAsync(new ChangeRoomStatusCommand(admin.ProfileScopeId, admin.CenterId, closed, false))).Ok);
        await RoomAsync(admin, "Habitación de otra unidad", otherUnit);
        await CreateAsync(Input(admin, null, occupied));
        var directory = new SqlLocationOptionsDirectory(TestDatabase.ConnectionFactory);

        var byAdmin = await directory.ListAsync(admin.ExternalSubject, admin.ProfileScopeId, admin.CenterId);
        var byNurse = await directory.ListAsync(nurse.ExternalSubject, nurse.ProfileScopeId, nurse.CenterId);
        var byAuxiliary = await directory.ListAsync(auxiliary.ExternalSubject, auxiliary.ProfileScopeId, auxiliary.CenterId);
        var foreign = await directory.ListAsync(nurse.ExternalSubject, admin.ProfileScopeId, admin.CenterId);

        Assert.Equal(3, byAdmin.Count);
        Assert.Contains(byAdmin, o => o.RoomName == "Habitación 1" && o.PlaceId is null && o.UnitId == admin.UnitId);
        Assert.Contains(byAdmin, o => o.PlaceId == free && o.PlaceName == "Cama libre");
        Assert.Contains(byAdmin, o => o.RoomName == "Habitación de otra unidad" && o.PlaceId is null && o.UnitId == otherUnit);
        Assert.DoesNotContain(byAdmin, o => o.PlaceId == occupied || o.PlaceId == inactive || o.RoomName == "Cerrada");
        // Enfermería solo tiene la unidad de partida, no la que creó Administración.
        Assert.Equal(2, byNurse.Count);
        Assert.All(byNurse, o => Assert.Equal(admin.UnitId, o.UnitId));
        Assert.Empty(byAuxiliary);
        Assert.Empty(foreign);
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaMoverNiBorrar_NiDosResidentesEnUnaPlaza_NiUnaPlazaDeOtraUnidad()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var otherUnit = (await service.CreateUnitAsync(new CreateUnitCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), $"hab-{Guid.NewGuid():N}"[..14], "Otra unidad"))).Value;
        var room = await RoomAsync(admin, "Habitación 1");
        var place = await PlaceAsync(admin, room, "Cama A");
        var foreignRoom = await RoomAsync(admin, "Ajena", otherUnit);
        var created = await CreateAsync(Input(admin, null, place));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        // Una habitación y una plaza sin hijos ni residentes: ahí responde el trigger; con hijos, la clave foránea compuesta lo rechaza antes.
        var emptyRoom = await RoomAsync(admin, "Vacía");
        var emptyPlaceRoom = await RoomAsync(admin, "Con una plaza");
        var emptyPlace = await PlaceAsync(admin, emptyPlaceRoom, "Cama suelta");
        var moveRoom = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.habitaciones SET unidad_id = @Other WHERE id = @Room", new { Other = otherUnit.Value, Room = emptyRoom }));
        var moveRoomWithChildren = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.habitaciones SET unidad_id = @Other WHERE id = @Room", new { Other = otherUnit.Value, Room = room }));
        var deleteRoom = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE FROM dbo.habitaciones WHERE id = @Room", new { Room = room }));
        var deletePlace = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE FROM dbo.plazas WHERE id = @Place", new { Place = place }));
        var movePlace = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.plazas SET creado_en = DATEADD(DAY, 1, creado_en) WHERE id = @Place", new { Place = emptyPlace }));
        // Dos residentes en una plaza lo impide este índice único filtrado (el caso real, con altas simultáneas, lo cubre otra prueba).
        var placeIndex = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'UX_rli_place_active' AND is_unique = 1 AND has_filter = 1");
        var foreignPlace = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.intervalos_ubicacion_residente SET unidad_id = @Other WHERE id = @Interval", new { Other = otherUnit.Value, Interval = created.LocationIntervalId }));

        Assert.Contains("ROOM_IMMUTABLE_FIELD", moveRoom.Message);
        Assert.Contains("conflicted", moveRoomWithChildren.Message);
        Assert.Contains("ROOM_DELETE_FORBIDDEN", deleteRoom.Message);
        Assert.Contains("PLACE_DELETE_FORBIDDEN", deletePlace.Message);
        Assert.Contains("PLACE_IMMUTABLE_FIELD", movePlace.Message);
        Assert.Equal(1, placeIndex);
        Assert.NotNull(foreignPlace);
    }
}
