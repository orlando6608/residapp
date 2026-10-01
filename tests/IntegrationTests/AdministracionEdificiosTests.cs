using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, estructura del centro (historia 2, script 0029), fase 1: edificios, plantas y colocar una unidad en ellos.
/// Cada prueba crea su propio centro.</summary>
public class AdministracionEdificiosTests
{
    private static AdministracionQuery Query(SeededProfile admin) => new(admin.ProfileScopeId, admin.CenterId);

    private static async Task<Guid> BuildingAsync(SeededProfile admin, string name, Guid? operationId = null) =>
        (await BuildEstructura(admin.ExternalSubject).CreateBuildingAsync(
            new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), name))).Value;

    private static async Task<Guid> FloorAsync(SeededProfile admin, Guid building, string name, Guid? operationId = null) =>
        (await BuildEstructura(admin.ExternalSubject).CreateFloorAsync(
            new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), building, name))).Value;

    private static async Task<IReadOnlyList<LayoutBuilding>> ListAsync(SeededProfile admin) =>
        (await BuildEstructura(admin.ExternalSubject).ListBuildingsAsync(Query(admin))).Value!;

    private static async Task<StructureUnit> UnitAsync(SeededProfile admin) =>
        (await BuildEstructura(admin.ExternalSubject).ListStructureUnitsAsync(Query(admin))).Value!.Single(u => u.UnitId == admin.UnitId);

    private static async Task<int> CountAuditAsync(Guid resourceId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @Id AND accion_codigo = @Action AND perfil_activo = 'ADMINISTRACION'",
            new { Id = resourceId, Action = action });
    }

    [Fact]
    public async Task CrearEdificioYPlanta_LosDejaActivos_ConAuditoria_YElReenvioNoLosDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var buildingOperation = Guid.NewGuid();
        var floorOperation = Guid.NewGuid();

        var building = await BuildingAsync(admin, "  Edificio Norte  ", buildingOperation);
        var again = await service.CreateBuildingAsync(new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, buildingOperation, "Edificio Norte"));
        var reused = await service.CreateBuildingAsync(new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, buildingOperation, "Otro nombre"));
        var floor = await FloorAsync(admin, building, "Planta 1", floorOperation);
        var floorAgain = await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, floorOperation, building, "Planta 1"));

        Assert.Equal(buildingOperation, building);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        Assert.True(floorAgain.Ok, floorAgain.Error?.Message);
        var listed = Assert.Single(await ListAsync(admin));
        Assert.Equal("Edificio Norte", listed.Name);
        Assert.True(listed.Active);
        var listedFloor = Assert.Single(listed.Floors);
        Assert.Equal((floor, "Planta 1", true), (listedFloor.FloorId, listedFloor.Name, listedFloor.Active));
        Assert.Equal(1, await CountAuditAsync(building, "BUILDING_CREATE"));
        Assert.Equal(1, await CountAuditAsync(floor, "FLOOR_CREATE"));
    }

    [Fact]
    public async Task Crear_RechazaNombresRepetidosOInvalidos_YEdificiosInactivosOAjenos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var building = await BuildingAsync(admin, "Norte");
        var otherBuilding = await BuildingAsync(other, "Norte");
        await FloorAsync(admin, building, "Planta 1");
        var inactive = await BuildingAsync(admin, "Viejo");
        Assert.True((await service.ChangeBuildingStatusAsync(new ChangeBuildingStatusCommand(admin.ProfileScopeId, admin.CenterId, inactive, false))).Ok);

        var results = new[]
        {
            (await service.CreateBuildingAsync(new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), "norte"))).Error,
            (await service.CreateBuildingAsync(new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), "   "))).Error,
            (await service.CreateBuildingAsync(new CreateBuildingCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), new string('a', 201)))).Error,
            (await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), building, "planta 1"))).Error,
            (await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), building, " "))).Error,
            (await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), inactive, "Planta 1"))).Error,
            (await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), otherBuilding, "Planta 1"))).Error,
        };

        Assert.Equal(
            [
                ApplicationFailureCode.Conflict, ApplicationFailureCode.InvalidInput, ApplicationFailureCode.InvalidInput,
                ApplicationFailureCode.Conflict, ApplicationFailureCode.InvalidInput, ApplicationFailureCode.InvalidInput,
                ApplicationFailureCode.AccessDenied,
            ],
            results.Select(e => e!.Code));
        // El mismo nombre en otro centro y en otro edificio sí vale.
        Assert.True((await service.CreateFloorAsync(new CreateFloorCommand(admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), (await BuildingAsync(admin, "Sur")), "Planta 1"))).Ok);
    }

    [Fact]
    public async Task Renombrar_CambiaElNombre_ConAuditoria_YRechazaElMismoOUnoTomado()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var building = await BuildingAsync(admin, "Norte");
        var other = await BuildingAsync(admin, "Sur");
        var floor = await FloorAsync(admin, building, "Planta 1");
        var second = await FloorAsync(admin, building, "Planta 2");

        var renamed = await service.RenameBuildingAsync(new RenameBuildingCommand(admin.ProfileScopeId, admin.CenterId, building, "  Norte nuevo "));
        var same = await service.RenameBuildingAsync(new RenameBuildingCommand(admin.ProfileScopeId, admin.CenterId, building, "Norte nuevo"));
        var taken = await service.RenameBuildingAsync(new RenameBuildingCommand(admin.ProfileScopeId, admin.CenterId, building, "sur"));
        var floorRenamed = await service.RenameFloorAsync(new RenameFloorCommand(admin.ProfileScopeId, admin.CenterId, floor, "Baja"));
        var floorTaken = await service.RenameFloorAsync(new RenameFloorCommand(admin.ProfileScopeId, admin.CenterId, floor, "planta 2"));
        var foreign = await BuildEstructura((await SeedFixture.CreateProfileAsync(SystemProfile.Administracion)).ExternalSubject)
            .RenameBuildingAsync(new RenameBuildingCommand(admin.ProfileScopeId, admin.CenterId, building, "Robado"));

        Assert.True(renamed.Ok, renamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, taken.Error!.Code);
        Assert.True(floorRenamed.Ok, floorRenamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, floorTaken.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        var listed = (await ListAsync(admin)).Single(b => b.BuildingId == building);
        Assert.Equal("Norte nuevo", listed.Name);
        Assert.Contains(listed.Floors, f => f.FloorId == floor && f.Name == "Baja");
        Assert.Contains(listed.Floors, f => f.FloorId == second && f.Name == "Planta 2");
        Assert.Equal(1, await CountAuditAsync(building, "BUILDING_RENAME"));
        Assert.Equal(1, await CountAuditAsync(floor, "FLOOR_RENAME"));
        Assert.NotEqual(other, building);
    }

    [Fact]
    public async Task Inactivar_NoVaConHijosActivos_Reactivar_ExigeElPadreActivo_YRepetirEsUnConflicto()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var building = await BuildingAsync(admin, "Norte");
        var floor = await FloorAsync(admin, building, "Planta 1");
        Task<ApplicationResult<bool>> Building(bool active) =>
            service.ChangeBuildingStatusAsync(new ChangeBuildingStatusCommand(admin.ProfileScopeId, admin.CenterId, building, active));
        Task<ApplicationResult<bool>> Floor(bool active) =>
            service.ChangeFloorStatusAsync(new ChangeFloorStatusCommand(admin.ProfileScopeId, admin.CenterId, floor, active));

        var withFloor = await Building(false);
        var floorOff = await Floor(false);
        var floorOffAgain = await Floor(false);
        var buildingOff = await Building(false);
        var floorOnUnderInactiveBuilding = await Floor(true);
        var buildingOn = await Building(true);
        var floorOn = await Floor(true);

        Assert.Equal(ApplicationFailureCode.InvalidInput, withFloor.Error!.Code);
        Assert.True(floorOff.Ok, floorOff.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, floorOffAgain.Error!.Code);
        Assert.True(buildingOff.Ok, buildingOff.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, floorOnUnderInactiveBuilding.Error!.Code);
        Assert.True(buildingOn.Ok, buildingOn.Error?.Message);
        Assert.True(floorOn.Ok, floorOn.Error?.Message);
        Assert.Equal(1, await CountAuditAsync(building, "BUILDING_DEACTIVATE"));
        Assert.Equal(1, await CountAuditAsync(building, "BUILDING_ACTIVATE"));
        Assert.Equal(1, await CountAuditAsync(floor, "FLOOR_DEACTIVATE"));
        Assert.Equal(1, await CountAuditAsync(floor, "FLOOR_ACTIVATE"));
    }

    [Fact]
    public async Task ColocarUnaUnidad_LaMuestraEnLaLista_ImpideInactivarSuEdificioOPlanta_YSeAudita()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var building = await BuildingAsync(admin, "Norte");
        var floor = await FloorAsync(admin, building, "Planta 1");
        var otherBuilding = await BuildingAsync(admin, "Sur");
        var otherFloor = await FloorAsync(admin, otherBuilding, "Planta A");
        Task<ApplicationResult<bool>> Locate(Guid? b, Guid? f) =>
            service.SetUnitLocationAsync(new SetUnitLocationCommand(admin.ProfileScopeId, admin.CenterId, admin.UnitId, b, f));

        var onlyBuilding = await Locate(building, null);
        var withFloor = await Locate(building, floor);
        var unchanged = await Locate(building, floor);
        var wrongFloor = await Locate(building, otherFloor);
        var floorWithoutBuilding = await Locate(null, floor);
        var unknownBuilding = await Locate(Guid.NewGuid(), null);
        var unit = await UnitAsync(admin);
        var buildingOff = await service.ChangeBuildingStatusAsync(new ChangeBuildingStatusCommand(admin.ProfileScopeId, admin.CenterId, building, false));
        var floorOff = await service.ChangeFloorStatusAsync(new ChangeFloorStatusCommand(admin.ProfileScopeId, admin.CenterId, floor, false));
        var removed = await Locate(null, null);
        var floorOffNow = await service.ChangeFloorStatusAsync(new ChangeFloorStatusCommand(admin.ProfileScopeId, admin.CenterId, floor, false));
        var inactiveTarget = await Locate(building, floor);

        Assert.True(onlyBuilding.Ok, onlyBuilding.Error?.Message);
        Assert.True(withFloor.Ok, withFloor.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, unchanged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, wrongFloor.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, floorWithoutBuilding.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unknownBuilding.Error!.Code);
        Assert.Equal((building, "Norte", floor, "Planta 1"), (unit.BuildingId, unit.BuildingName, unit.FloorId, unit.FloorName));
        Assert.Equal(ApplicationFailureCode.InvalidInput, buildingOff.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, floorOff.Error!.Code);
        Assert.True(removed.Ok, removed.Error?.Message);
        Assert.True(floorOffNow.Ok, floorOffNow.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, inactiveTarget.Error!.Code);
        Assert.Null((await UnitAsync(admin)).BuildingId);
        Assert.Equal(3, await CountAuditAsync(admin.UnitId.Value, "UNIT_LOCATE"));
    }

    [Fact]
    public async Task EnfermeriaOtroAmbitoYUnidadesAjenas_ReturnsAccessDenied()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var building = await BuildingAsync(admin, "Norte");
        var nurseService = BuildEstructura(nurse.ExternalSubject);
        var otherService = BuildEstructura(other.ExternalSubject);

        var errors = new[]
        {
            (await nurseService.ListBuildingsAsync(Query(nurse))).Error,
            (await nurseService.CreateBuildingAsync(new CreateBuildingCommand(nurse.ProfileScopeId, nurse.CenterId, Guid.NewGuid(), "Intento"))).Error,
            (await nurseService.ChangeBuildingStatusAsync(new ChangeBuildingStatusCommand(nurse.ProfileScopeId, nurse.CenterId, building, false))).Error,
            (await nurseService.SetUnitLocationAsync(new SetUnitLocationCommand(nurse.ProfileScopeId, nurse.CenterId, nurse.UnitId, building, null))).Error,
            // Una unidad del centro que no está en el ámbito de quien gestiona no se puede colocar.
            (await otherService.SetUnitLocationAsync(new SetUnitLocationCommand(other.ProfileScopeId, other.CenterId, admin.UnitId, null, null))).Error,
            (await otherService.RenameBuildingAsync(new RenameBuildingCommand(other.ProfileScopeId, other.CenterId, building, "Robado"))).Error,
        };

        Assert.All(errors, e => Assert.Equal(ApplicationFailureCode.AccessDenied, e!.Code));
        Assert.Empty((await ListAsync(other)));
        Assert.Equal("Norte", Assert.Single(await ListAsync(admin)).Name);
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaMoverNiBorrar_YExigeQueLaPlantaSeaDelEdificio()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var building = await BuildingAsync(admin, "Norte");
        var otherBuilding = await BuildingAsync(admin, "Sur");
        var floor = await FloorAsync(admin, building, "Planta 1");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var moveFloor = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.plantas SET edificio_id = @OtherBuilding WHERE id = @Floor", new { OtherBuilding = otherBuilding, Floor = floor }));
        var deleteFloor = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE FROM dbo.plantas WHERE id = @Floor", new { Floor = floor }));
        var deleteBuilding = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("DELETE FROM dbo.edificios WHERE id = @Building", new { Building = building }));
        var changeCenter = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.edificios SET centro_id = (SELECT TOP 1 id FROM dbo.centros WHERE id <> @Center) WHERE id = @Building",
            new { Center = admin.CenterId.Value, Building = building }));
        var wrongFloor = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.unidades SET edificio_id = @OtherBuilding, planta_id = @Floor WHERE id = @Unit",
            new { OtherBuilding = otherBuilding, Floor = floor, Unit = admin.UnitId.Value }));
        var floorWithoutBuilding = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.unidades SET edificio_id = NULL, planta_id = @Floor WHERE id = @Unit", new { Floor = floor, Unit = admin.UnitId.Value }));
        var code = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.unidades SET codigo = codigo + N'X' WHERE id = @Unit", new { Unit = admin.UnitId.Value }));
        var moved = await connection.ExecuteAsync(
            "UPDATE dbo.unidades SET edificio_id = @Building, planta_id = @Floor WHERE id = @Unit",
            new { Building = building, Floor = floor, Unit = admin.UnitId.Value });

        Assert.Contains("FLOOR_IMMUTABLE_FIELD", moveFloor.Message);
        Assert.Contains("FLOOR_DELETE_FORBIDDEN", deleteFloor.Message);
        Assert.Contains("BUILDING_DELETE_FORBIDDEN", deleteBuilding.Message);
        // Con plantas colgando, la clave foránea compuesta lo rechaza antes que el trigger.
        Assert.Contains("FK_floors_building", changeCenter.Message);
        Assert.Contains("FK_units_floor", wrongFloor.Message);
        Assert.Contains("CK_units_floor_requires_building", floorWithoutBuilding.Message);
        Assert.Contains("UNIT_IMMUTABLE_FIELD", code.Message);
        Assert.Equal(1, moved);
    }
}
