using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, estructura del centro (historia 2, ADM-05, script 0025): crear, renombrar e inactivar unidades.
/// Cada prueba crea su propio centro.</summary>
public class AdministracionEstructuraTests
{
    private static CreateUnitCommand Create(SeededProfile admin, string code, string name, Guid? operationId = null) =>
        new(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), code, name);

    private static string NewCode() => $"est-{Guid.NewGuid():N}"[..20];

    private static async Task<IReadOnlyList<StructureUnit>> ListAsync(SeededProfile admin) =>
        (await Build(admin.ExternalSubject).ListStructureUnitsAsync(new AdministracionQuery(admin.ProfileScopeId, admin.CenterId))).Value!;

    private static async Task<int> CountAuditAsync(Guid resourceId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE recurso_id = @Id AND accion_codigo = @Action AND perfil_activo = 'ADMINISTRACION'",
            new { Id = resourceId, Action = action });
    }

    [Fact]
    public async Task Crear_LaDejaActivaEnElAmbito_LaOfreceElSelector_YElReenvioNoLaDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var operationId = Guid.NewGuid();
        var code = NewCode();

        var created = await service.CreateUnitAsync(Create(admin, code, "  Planta Norte  ", operationId));
        var again = await service.CreateUnitAsync(Create(admin, code.ToUpperInvariant(), "Planta Norte", operationId));
        var reused = await service.CreateUnitAsync(Create(admin, code, "Otro nombre", operationId));

        Assert.True(created.Ok, created.Error?.Message);
        Assert.Equal(operationId, created.Value.Value);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        var unit = Assert.Single(await ListAsync(admin), u => u.UnitId.Value == operationId);
        Assert.Equal(code.ToUpperInvariant(), unit.Code);
        Assert.Equal("Planta Norte", unit.Name);
        Assert.True(unit.Active);
        Assert.Equal(0, unit.CurrentResidents);
        var selector = await new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory)
            .ListUnitsAsync(admin.ExternalSubject, admin.ProfileScopeId, admin.CenterId);
        Assert.Contains(selector, u => u.UnitId.Value == operationId);
        Assert.Equal(1, await CountAuditAsync(operationId, "UNIT_CREATE"));
    }

    [Fact]
    public async Task Crear_RechazaCodigoONombreRepetidos_YDatosInvalidos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var code = NewCode();
        Assert.True((await service.CreateUnitAsync(Create(admin, code, "Unidad Uno"))).Ok);

        var sameCode = await service.CreateUnitAsync(Create(admin, code.ToUpperInvariant(), "Unidad Dos"));
        var sameName = await service.CreateUnitAsync(Create(admin, NewCode(), "Unidad Uno"));
        var badCode = await service.CreateUnitAsync(Create(admin, "con espacio", "Unidad Tres"));
        var shortCode = await service.CreateUnitAsync(Create(admin, "a", "Unidad Tres"));
        var noName = await service.CreateUnitAsync(Create(admin, NewCode(), "  "));

        Assert.Equal(ApplicationFailureCode.Conflict, sameCode.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, sameName.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, badCode.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, shortCode.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noName.Error!.Code);
    }

    [Fact]
    public async Task Renombrar_CambiaSoloElNombre_ConAuditoria_YRechazaRepetidoOIgual()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var first = (await service.CreateUnitAsync(Create(admin, NewCode(), "Unidad A"))).Value;
        var second = (await service.CreateUnitAsync(Create(admin, NewCode(), "Unidad B"))).Value;
        RenameUnitCommand Rename(UnitId id, string name) => new(admin.ProfileScopeId, admin.CenterId, id, name);

        var renamed = await service.RenameUnitAsync(Rename(first, "Unidad A renombrada"));
        var same = await service.RenameUnitAsync(Rename(first, "Unidad A renombrada"));
        var taken = await service.RenameUnitAsync(Rename(first, "Unidad B"));
        var blank = await service.RenameUnitAsync(Rename(first, " "));

        Assert.True(renamed.Ok, renamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, taken.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, blank.Error!.Code);
        Assert.Contains(await ListAsync(admin), u => u.UnitId == first && u.Name == "Unidad A renombrada");
        Assert.Contains(await ListAsync(admin), u => u.UnitId == second && u.Name == "Unidad B");
        Assert.Equal(1, await CountAuditAsync(first.Value, "UNIT_RENAME"));
    }

    [Fact]
    public async Task Inactivar_NoValeConResidentesUbicados_SalePorElSelector_YSeReactiva()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var empty = (await service.CreateUnitAsync(Create(admin, NewCode(), "Unidad vacía"))).Value;
        ChangeUnitStatusCommand Status(UnitId id, bool active) => new(admin.ProfileScopeId, admin.CenterId, id, active);
        await CreateResidentAsync(admin, "Residente en la unidad del seed");

        var withResidents = await service.ChangeUnitStatusAsync(Status(admin.UnitId, false));
        var deactivated = await service.ChangeUnitStatusAsync(Status(empty, false));
        var twice = await service.ChangeUnitStatusAsync(Status(empty, false));

        Assert.Equal(ApplicationFailureCode.InvalidInput, withResidents.Error!.Code);
        Assert.True(deactivated.Ok, deactivated.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, twice.Error!.Code);
        var listed = await ListAsync(admin);
        Assert.Contains(listed, u => u.UnitId == empty && !u.Active);
        Assert.Contains(listed, u => u.UnitId == admin.UnitId && u.Active && u.CurrentResidents == 1);
        var selector = await new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory)
            .ListUnitsAsync(admin.ExternalSubject, admin.ProfileScopeId, admin.CenterId);
        Assert.DoesNotContain(selector, u => u.UnitId == empty);

        var reactivated = await service.ChangeUnitStatusAsync(Status(empty, true));

        Assert.True(reactivated.Ok, reactivated.Error?.Message);
        Assert.Contains(await ListAsync(admin), u => u.UnitId == empty && u.Active);
        Assert.Equal(1, await CountAuditAsync(empty.Value, "UNIT_DEACTIVATE"));
        Assert.Equal(1, await CountAuditAsync(empty.Value, "UNIT_ACTIVATE"));
    }

    [Fact]
    public async Task OtroPerfil_OUnaUnidadAjena_DanAccesoDenegado()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var mine = (await Build(admin.ExternalSubject).CreateUnitAsync(Create(admin, NewCode(), "Mi unidad"))).Value;
        var nurseService = Build(nurse.ExternalSubject);
        var otherService = Build(otherAdmin.ExternalSubject);

        var list = await nurseService.ListStructureUnitsAsync(new AdministracionQuery(nurse.ProfileScopeId, nurse.CenterId));
        var create = await nurseService.CreateUnitAsync(Create(nurse, NewCode(), "Intento de Enfermería"));
        var foreignRename = await otherService.RenameUnitAsync(new RenameUnitCommand(otherAdmin.ProfileScopeId, otherAdmin.CenterId, mine, "Robada"));
        var foreignStatus = await otherService.ChangeUnitStatusAsync(new ChangeUnitStatusCommand(otherAdmin.ProfileScopeId, otherAdmin.CenterId, mine, false));
        var crossCenter = await otherService.RenameUnitAsync(new RenameUnitCommand(admin.ProfileScopeId, admin.CenterId, mine, "Robada"));

        Assert.Equal(ApplicationFailureCode.AccessDenied, list.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, create.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignRename.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreignStatus.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, crossCenter.Error!.Code);
        Assert.Contains(await ListAsync(admin), u => u.UnitId == mine && u.Name == "Mi unidad" && u.Active);
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaCambiarElCodigoNiBorrarUnidades()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var parameters = new { Id = admin.UnitId.Value };

        var code = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.unidades SET codigo = codigo + N'X' WHERE id = @Id", parameters));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.unidades WHERE id = @Id", parameters));
        var rename = await connection.ExecuteAsync("UPDATE dbo.unidades SET nombre_visible = nombre_visible + N' bis' WHERE id = @Id", parameters);

        Assert.Contains("UNIT_IMMUTABLE_FIELD", code.Message);
        Assert.Contains("UNIT_DELETE_FORBIDDEN", delete.Message);
        Assert.Equal(1, rename);
    }
}
