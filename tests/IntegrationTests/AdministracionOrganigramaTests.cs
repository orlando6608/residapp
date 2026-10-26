using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Organization;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Organigrama y cargos (ADM-07, script 0073, respuestas de CJ del 2026-10-25): cualquier Administración del centro los gestiona;
/// ocupan un cargo cuentas del centro o solo un nombre; todos los profesionales lo consultan y la familia no. Cada prueba crea su propio centro.</summary>
public class AdministracionOrganigramaTests
{
    private static readonly SqlConnectionFactory Connections = TestDatabase.ConnectionFactory;

    private static AdministracionOrganigramaApplicationService Build(string externalSubject) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(Connections), new FixedOrganigramaSessionIdentityProvider(externalSubject)),
        new SqlProfileScopeDirectoryProvider(Connections), new SqlOrganizationDirectory(Connections), new SqlOrganizationRepository(Connections));

    private static OrganigramaApplicationService BuildReader(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(Connections), new FixedOrganigramaSessionIdentityProvider(externalSubject), new SqlOrganizationDirectory(Connections));

    private static AdministracionQuery Query(SeededProfile admin) => new(admin.ProfileScopeId, admin.CenterId);

    private static string NewName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..24];

    private static CreatePositionCommand NewPosition(
        SeededProfile admin, string? name = null, bool onePerson = false, Guid? superior = null, string? note = null, Guid? operationId = null,
        params UnitId[] units) =>
        new(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), name ?? NewName("Cargo"), onePerson, superior, note, units);

    private static async Task<Guid> CreateAsync(
        AdministracionOrganigramaApplicationService service, SeededProfile admin, string? name = null, bool onePerson = false, Guid? superior = null)
    {
        var created = await service.CreateAsync(NewPosition(admin, name, onePerson, superior));
        Assert.True(created.Ok, created.Error?.Message);
        return created.Value;
    }

    private static UpdatePositionCommand Update(
        SeededProfile admin, Guid id, string name, bool onePerson = false, Guid? superior = null, string? note = null, params UnitId[] units) =>
        new(admin.ProfileScopeId, admin.CenterId, id, name, onePerson, superior, note, units);

    private static AddPositionOccupantCommand AddName(SeededProfile admin, Guid positionId, string? name) =>
        new(admin.ProfileScopeId, admin.CenterId, positionId, null, name);

    private static AddPositionOccupantCommand AddAccount(SeededProfile admin, Guid positionId, AccountId account) =>
        new(admin.ProfileScopeId, admin.CenterId, positionId, account, null);

    private static ChangePositionStatusCommand Status(SeededProfile admin, Guid id, bool active) => new(admin.ProfileScopeId, admin.CenterId, id, active);

    private static async Task<PositionInfo> FindAsync(AdministracionOrganigramaApplicationService service, SeededProfile admin, Guid id) =>
        Assert.Single((await service.ListAsync(Query(admin))).Value!, p => p.PositionId == id);

    private static async Task<int> CountAuditAsync(Guid resourceId, string action)
    {
        using var connection = await Connections.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @Id AND accion_codigo = @Action AND tipo_recurso = 'POSITION' AND perfil_activo = 'ADMINISTRACION'",
            new { Id = resourceId, Action = action });
    }

    private static async Task<string> SqlErrorAsync(string sql, object? parameters = null)
    {
        using var connection = await Connections.OpenAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, parameters));
        return error.Message;
    }

    [Fact]
    public async Task Cargo_SeCreaConSusDatosYUnidades_ElReenvioNoLoDuplica_YQuedaAuditado()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var superior = await CreateAsync(service, admin, "Dirección", onePerson: true);
        var operationId = Guid.NewGuid();
        var command = NewPosition(admin, "  Enfermería ", false, superior, " Coordina con Medicina ", operationId, admin.UnitId);

        var created = await service.CreateAsync(command);
        var again = await service.CreateAsync(command);
        var reused = await service.CreateAsync(command with { Nota = "Otra nota" });

        Assert.True(created.Ok, created.Error?.Message);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        var position = await FindAsync(service, admin, operationId);
        Assert.Equal(("Enfermería", false, superior, "Coordina con Medicina", true), (position.Name, position.OnePerson, position.SuperiorId, position.Note, position.Active));
        Assert.Equal([admin.UnitId], position.Units.Select(u => u.UnitId));
        Assert.Equal(2, (await service.ListAsync(Query(admin))).Value!.Count);
        Assert.Equal(1, await CountAuditAsync(operationId, "POSITION_CREATE"));
    }

    [Fact]
    public async Task Cargo_RechazaNombreRepetidoSinMayusculasNiAcentos_DatosInvalidos_SuperiorAjeno_YUnidadFueraDelAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var foreignPosition = await CreateAsync(Build(other.ExternalSubject), other, "Ajeno");
        var foreignUnit = await AdministracionResidentesTests.AddUnitAsync(admin.CenterId);
        await CreateAsync(service, admin, "Dirección médica");

        var sameName = await service.CreateAsync(NewPosition(admin, "DIRECCION MEDICA"));
        var noName = await service.CreateAsync(NewPosition(admin, " "));
        var longNote = await service.CreateAsync(NewPosition(admin, note: new string('a', Position.MaxNoteLength + 1)));
        var foreignSuperior = await service.CreateAsync(NewPosition(admin, superior: foreignPosition));
        var missingSuperior = await service.CreateAsync(NewPosition(admin, superior: Guid.NewGuid()));
        var unitOutsideScope = await service.CreateAsync(NewPosition(admin, units: foreignUnit));

        Assert.Equal(ApplicationFailureCode.Conflict, sameName.Error!.Code);
        Assert.Contains("Ya existe un cargo", sameName.Error.Message);
        Assert.All(new[] { noName, longNote, foreignSuperior, missingSuperior, unitOutsideScope },
            r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
    }

    [Fact]
    public async Task Superior_NoPermiteCiclos_NiDependerDeSiMismo_NiDeUnCargoDeOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var a = await CreateAsync(service, admin, "Uno");
        var b = await CreateAsync(service, admin, "Dos", superior: a);
        var c = await CreateAsync(service, admin, "Tres", superior: b);

        var selfCycle = await service.UpdateAsync(Update(admin, a, "Uno", superior: a));
        var deepCycle = await service.UpdateAsync(Update(admin, a, "Uno", superior: c));
        var moved = await service.UpdateAsync(Update(admin, c, "Tres", superior: a));

        Assert.Equal(ApplicationFailureCode.InvalidInput, selfCycle.Error!.Code);
        Assert.Contains("subordinados", deepCycle.Error!.Message);
        Assert.True(moved.Ok, moved.Error?.Message);
        Assert.Equal(a, (await FindAsync(service, admin, c)).SuperiorId);
    }

    [Fact]
    public async Task Ocupantes_UnaCuentaOSoloUnNombre_UnaPersonaEnVariosCargos_YSeQuitan()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var service = Build(admin.ExternalSubject);
        var nursing = await CreateAsync(service, admin, "Enfermería");
        var medicine = await CreateAsync(service, admin, "Medicina");

        var byName = await service.AddOccupantAsync(AddName(admin, nursing, "  Rosa Gil "));
        var byAccount = await service.AddOccupantAsync(AddAccount(admin, nursing, nurse.AccountId));
        var sameAccountInOtherPosition = await service.AddOccupantAsync(AddAccount(admin, medicine, nurse.AccountId));
        var sameName = await service.AddOccupantAsync(AddName(admin, nursing, "rosa gil"));
        var sameAccount = await service.AddOccupantAsync(AddAccount(admin, nursing, nurse.AccountId));
        var both = await service.AddOccupantAsync(new AddPositionOccupantCommand(admin.ProfileScopeId, admin.CenterId, nursing, admin.AccountId, "Alguien"));
        var neither = await service.AddOccupantAsync(AddName(admin, nursing, " "));
        var tooLong = await service.AddOccupantAsync(AddName(admin, nursing, new string('a', Position.MaxPersonNameLength + 1)));

        Assert.All(new[] { byName, byAccount, sameAccountInOtherPosition }, r => Assert.True(r.Ok, r.Error?.Message));
        Assert.Equal(ApplicationFailureCode.Conflict, sameName.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, sameAccount.Error!.Code);
        Assert.All(new[] { both, neither, tooLong }, r => Assert.Equal(ApplicationFailureCode.InvalidInput, r.Error!.Code));
        var occupants = (await FindAsync(service, admin, nursing)).Occupants;
        Assert.Equal(["Rosa Gil", nurse.ExternalSubject], occupants.Select(o => o.Name).Order());
        Assert.Equal([false, true], occupants.OrderBy(o => o.HasAccount).Select(o => o.HasAccount));
        Assert.Single((await FindAsync(service, admin, medicine)).Occupants);

        var removed = await service.RemoveOccupantAsync(new RemovePositionOccupantCommand(admin.ProfileScopeId, admin.CenterId, nursing, occupants[0].OccupantId));
        var removedAgain = await service.RemoveOccupantAsync(new RemovePositionOccupantCommand(admin.ProfileScopeId, admin.CenterId, nursing, occupants[0].OccupantId));
        Assert.True(removed.Ok, removed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, removedAgain.Error!.Code);
        Assert.Single((await FindAsync(service, admin, nursing)).Occupants);
        Assert.Equal(2, await CountAuditAsync(nursing, "POSITION_OCCUPANT_ADD"));
        Assert.Equal(1, await CountAuditAsync(nursing, "POSITION_OCCUPANT_REMOVE"));
    }

    [Fact]
    public async Task Ocupantes_LaCuentaTieneQueSerProfesionalActivaDelCentro_YOcuparNoDaPermisos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var elsewhere = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var auxiliary = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var service = Build(admin.ExternalSubject);
        var position = await CreateAsync(service, admin, "Auxiliares");

        var foreign = await service.AddOccupantAsync(AddAccount(admin, position, elsewhere.AccountId));
        var unknown = await service.AddOccupantAsync(AddAccount(admin, position, AccountId.From(Guid.NewGuid())));
        var candidates = (await service.ListCandidatesAsync(Query(admin))).Value!;
        var added = await service.AddOccupantAsync(AddAccount(admin, position, auxiliary.AccountId));

        Assert.Equal(ApplicationFailureCode.InvalidInput, foreign.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unknown.Error!.Code);
        Assert.Equivalent(new[] { admin.AccountId, auxiliary.AccountId }, candidates.Select(c => c.AccountId).ToArray());
        Assert.True(added.Ok, added.Error?.Message);
        // Ocupar un cargo no toca ningún permiso ni ámbito de la cuenta.
        using var connection = await Connections.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.permisos_perfil WHERE ambito_perfil_id = @Id", new { Id = auxiliary.ProfileScopeId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @Id AND revocado_en IS NULL", new { Id = auxiliary.AccountId.Value }));
    }

    [Fact]
    public async Task CargoDeUnaPersona_NoAdmiteUnaSegunda_NiPasaAUnaPersonaConVarias()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var direction = await CreateAsync(service, admin, "Dirección", onePerson: true);
        var team = await CreateAsync(service, admin, "Enfermería", onePerson: false);

        var first = await service.AddOccupantAsync(AddName(admin, direction, "Ana"));
        var second = await service.AddOccupantAsync(AddName(admin, direction, "Luis"));
        await service.AddOccupantAsync(AddName(admin, team, "Ana"));
        await service.AddOccupantAsync(AddName(admin, team, "Luis"));
        var toOne = await service.UpdateAsync(Update(admin, team, "Enfermería", onePerson: true));

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, second.Error!.Code);
        Assert.Contains("una sola persona", second.Error.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, toOne.Error!.Code);
        Assert.False((await FindAsync(service, admin, team)).OnePerson);
    }

    [Fact]
    public async Task Inactivar_ExigeSinOcupantesNiSubordinadosActivos_NoSePuedeOcuparUnoInactivo_YReactivarExigeSuperiorActivo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var direction = await CreateAsync(service, admin, "Dirección", onePerson: true);
        var team = await CreateAsync(service, admin, "Enfermería", superior: direction);
        await service.AddOccupantAsync(AddName(admin, team, "Ana"));

        var withOccupants = await service.ChangeStatusAsync(Status(admin, team, false));
        var withSubordinates = await service.ChangeStatusAsync(Status(admin, direction, false));
        var occupantId = (await FindAsync(service, admin, team)).Occupants.Single().OccupantId;
        await service.RemoveOccupantAsync(new RemovePositionOccupantCommand(admin.ProfileScopeId, admin.CenterId, team, occupantId));
        var teamOff = await service.ChangeStatusAsync(Status(admin, team, false));
        var teamOffAgain = await service.ChangeStatusAsync(Status(admin, team, false));
        var occupyInactive = await service.AddOccupantAsync(AddName(admin, team, "Luis"));
        var directionOff = await service.ChangeStatusAsync(Status(admin, direction, false));
        var teamOnUnderInactive = await service.ChangeStatusAsync(Status(admin, team, true));
        var directionOn = await service.ChangeStatusAsync(Status(admin, direction, true));
        var teamOn = await service.ChangeStatusAsync(Status(admin, team, true));

        Assert.Contains("Quita antes a las personas", withOccupants.Error!.Message);
        Assert.Contains("cargos activos que dependen", withSubordinates.Error!.Message);
        Assert.True(teamOff.Ok, teamOff.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, teamOffAgain.Error!.Code);
        Assert.Contains("inactivo", occupyInactive.Error!.Message);
        Assert.True(directionOff.Ok, directionOff.Error?.Message);
        Assert.Contains("superior está inactivo", teamOnUnderInactive.Error!.Message);
        Assert.True(directionOn.Ok && teamOn.Ok);
        Assert.Equal(1, await CountAuditAsync(team, "POSITION_DEACTIVATE"));
        Assert.Equal(1, await CountAuditAsync(team, "POSITION_REACTIVATE"));
    }

    [Fact]
    public async Task Plantilla_CargaLosOchoCargosDeCJ_SoloSiElCentroNoTieneNinguno()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var withOne = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        await CreateAsync(Build(withOne.ExternalSubject), withOne, "Ya tengo uno");

        var load = await service.LoadTemplateAsync(new LoadPositionTemplateCommand(admin.ProfileScopeId, admin.CenterId));
        var again = await service.LoadTemplateAsync(new LoadPositionTemplateCommand(admin.ProfileScopeId, admin.CenterId));
        var notEmpty = await Build(withOne.ExternalSubject).LoadTemplateAsync(new LoadPositionTemplateCommand(withOne.ProfileScopeId, withOne.CenterId));

        Assert.True(load.Ok, load.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, notEmpty.Error!.Code);
        var positions = (await service.ListAsync(Query(admin))).Value!;
        Assert.Equal(PositionTemplate.Entries.Select(e => e.Name).Order(), positions.Select(p => p.Name).Order());
        foreach (var entry in PositionTemplate.Entries)
        {
            var position = positions.Single(p => p.Name == entry.Name);
            Assert.Equal(entry.OnePerson, position.OnePerson);
            Assert.Equal(entry.Note, position.Note);
            var expectedSuperior = entry.SuperiorKey is null ? null : PositionTemplate.Entries.Single(e => e.Key == entry.SuperiorKey).Name;
            Assert.Equal(expectedSuperior, positions.SingleOrDefault(p => p.PositionId == position.SuperiorId)?.Name);
        }

        Assert.Equal(1, await CountTemplateAuditAsync(admin));
    }

    private static async Task<int> CountTemplateAuditAsync(SeededProfile admin)
    {
        using var connection = await Connections.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE centro_id = @Center AND accion_codigo = 'POSITION_TEMPLATE_LOAD'",
            new { Center = admin.CenterId.Value });
    }

    [Fact]
    public async Task Unidades_SoloSeLiganLasDelAmbito_YGuardarConservaLasLigasDeUnidadesAjenas()
    {
        var first = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var secondUnit = await AdministracionResidentesTests.AddUnitAsync(first.CenterId);
        var second = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, first.CenterId, secondUnit);
        var one = Build(first.ExternalSubject);
        var two = Build(second.ExternalSubject);
        var position = (await one.CreateAsync(NewPosition(first, "Enfermería", units: first.UnitId))).Value;

        var secondAdds = await two.UpdateAsync(Update(second, position, "Enfermería", units: secondUnit));
        var linked = (await FindAsync(one, first, position)).Units.Select(u => u.UnitId).ToList();
        var secondCannotUseFirstUnit = await two.UpdateAsync(Update(second, position, "Enfermería", units: [secondUnit, first.UnitId]));
        var firstClears = await one.UpdateAsync(Update(first, position, "Enfermería"));
        var afterClear = (await FindAsync(one, first, position)).Units.Select(u => u.UnitId).ToList();

        Assert.True(secondAdds.Ok, secondAdds.Error?.Message);
        Assert.Equivalent(new[] { first.UnitId, secondUnit }, linked.ToArray());
        Assert.Equal(ApplicationFailureCode.InvalidInput, secondCannotUseFirstUnit.Error!.Code);
        Assert.True(firstClears.Ok, firstClears.Error?.Message);
        Assert.Equal([secondUnit], afterClear);
        Assert.Equal(2, await CountAuditAsync(position, "POSITION_UPDATE"));
    }

    [Fact]
    public async Task Guardar_SinCambios_NoAudita()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var position = await CreateAsync(service, admin, "Enfermería");

        var saved = await service.UpdateAsync(Update(admin, position, "Enfermería"));

        Assert.True(saved.Ok, saved.Error?.Message);
        Assert.Equal(0, await CountAuditAsync(position, "POSITION_UPDATE"));
    }

    [Fact]
    public async Task Gestionar_SoloAdministracionDelCentro_YNoOtroPerfilNiOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var stranger = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var position = await CreateAsync(Build(admin.ExternalSubject), admin, "Enfermería");

        var asNurse = await Build(nurse.ExternalSubject).CreateAsync(NewPosition(nurse));
        var strangerUpdate = await Build(stranger.ExternalSubject).UpdateAsync(
            new UpdatePositionCommand(stranger.ProfileScopeId, stranger.CenterId, position, "Hackeado", false, null, null, []));
        var strangerOnOurCenter = await Build(stranger.ExternalSubject).ListAsync(new AdministracionQuery(stranger.ProfileScopeId, admin.CenterId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, asNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, strangerUpdate.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, strangerOnOurCenter.Error!.Code);
        Assert.Equal("Enfermería", (await FindAsync(Build(admin.ExternalSubject), admin, position)).Name);
    }

    [Fact]
    public async Task Consulta_LaHacenTodosLosProfesionalesDelCentro_SoloVenLoActivo_YNoVenOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var active = await CreateAsync(service, admin, "Enfermería");
        var inactive = await CreateAsync(service, admin, "Antiguo");
        await service.ChangeStatusAsync(Status(admin, inactive, false));
        await service.AddOccupantAsync(AddName(admin, active, "Rosa Gil"));
        var viewers = new[]
        {
            admin,
            await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId),
            await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId),
            await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId),
            await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId),
        };
        var stranger = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        foreach (var viewer in viewers)
        {
            var read = await BuildReader(viewer.ExternalSubject).ReadAsync(viewer.ProfileScopeId, viewer.CenterId);
            Assert.True(read.Ok, read.Error?.Message);
            var position = Assert.Single(read.Value!);
            Assert.Equal("Enfermería", position.Name);
            Assert.Equal("Rosa Gil", Assert.Single(position.Occupants).Name);
        }

        var strangerRead = await BuildReader(stranger.ExternalSubject).ReadAsync(stranger.ProfileScopeId, stranger.CenterId);
        var strangerOnOurs = await BuildReader(stranger.ExternalSubject).ReadAsync(stranger.ProfileScopeId, admin.CenterId);
        Assert.Empty(strangerRead.Value!);
        Assert.Equal(ApplicationFailureCode.AccessDenied, strangerOnOurs.Error!.Code);
    }

    [Fact]
    public async Task Consulta_LaFamiliaNoLaVe_NiSinSesion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await CreateAsync(Build(admin.ExternalSubject), admin, "Enfermería");
        var family = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Familiar, admin.CenterId, admin.UnitId);

        var asFamily = await BuildReader(family.ExternalSubject).ReadAsync(family.ProfileScopeId, family.CenterId);
        var unknown = await BuildReader("nadie-" + Guid.NewGuid().ToString("N")).ReadAsync(admin.ProfileScopeId, admin.CenterId);

        Assert.Equal(ApplicationFailureCode.AccessDenied, asFamily.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, unknown.Error!.Code);
    }

    [Fact]
    public async Task BaseDeDatos_ImpideBorrarCargos_CiclosDirectos_SegundoOcupanteYCambiarUnOcupante()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var a = await CreateAsync(service, admin, "Uno", onePerson: true);
        var b = await CreateAsync(service, admin, "Dos", superior: a);
        await service.AddOccupantAsync(AddName(admin, a, "Ana"));
        var occupantId = (await FindAsync(service, admin, a)).Occupants.Single().OccupantId;

        Assert.Contains("POSITION_DELETE_FORBIDDEN", await SqlErrorAsync("DELETE FROM dbo.cargos WHERE id = @Id", new { Id = a }));
        Assert.Contains("POSITION_CYCLE", await SqlErrorAsync("UPDATE dbo.cargos SET superior_id = @B WHERE id = @A", new { A = a, B = b }));
        Assert.Contains("POSITION_SINGLE_OCCUPANT", await SqlErrorAsync("""
            INSERT INTO dbo.cargos_ocupantes (id, centro_id, cargo_id, nombre_texto, puesto_en, puesto_por_cuenta_id)
            VALUES (NEWID(), @Center, @A, 'Luis', SYSUTCDATETIME(), @Actor)
            """, new { Center = admin.CenterId.Value, A = a, Actor = admin.AccountId.Value }));
        Assert.Contains("POSITION_OCCUPANT_IMMUTABLE", await SqlErrorAsync(
            "UPDATE dbo.cargos_ocupantes SET nombre_texto = 'Otra' WHERE id = @Id", new { Id = occupantId }));
        Assert.Contains("POSITION_HAS_OCCUPANTS", await SqlErrorAsync("UPDATE dbo.cargos SET estado = 'INACTIVE' WHERE id = @Id", new { Id = a }));
    }

    [Fact]
    public async Task Auditoria_LosEventosDelOrganigramaSalenConElNombreDelCargo_SinNombresDePersonas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var position = await CreateAsync(service, admin, "Coordinación de Enfermería");
        await service.AddOccupantAsync(AddName(admin, position, "Persona Sin Cuenta"));

        var page = await new SqlAdministrativeAuditDirectory(Connections).ListAsync(
            new AccountAdministrationAccess(admin.ProfileScopeId, admin.AccountId, admin.CenterId),
            new AdministrativeAuditQuery(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), null, null));

        Assert.Equal(["POSITION_OCCUPANT_ADD", "POSITION_CREATE"], page.Entries.Select(e => e.Action));
        Assert.All(page.Entries, e => Assert.Equal("Coordinación de Enfermería", e.PositionName));
        Assert.DoesNotContain(page.Entries, e => (e.AffectedName ?? "").Contains("Persona Sin Cuenta"));
    }
}

file sealed class FixedOrganigramaSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
