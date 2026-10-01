using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Audit;
using ResidApp.Infrastructure.Authorization;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, turnos y equipos (historia 4, ADM-14, script 0027), fase 1: catálogo de turnos, equipos de las unidades
/// del ámbito y sus miembros. Cada prueba crea su propio centro.</summary>
public class AdministracionTurnosTests
{
    private static string NewName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..24];

    private static CreateShiftCommand NewShift(SeededProfile admin, string? name = null, Guid? operationId = null, string start = "07:00", string end = "15:00") =>
        new(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), name ?? NewName("Turno"), TimeOnly.Parse(start), TimeOnly.Parse(end));

    private static CreateTeamCommand NewTeam(SeededProfile admin, UnitId? unit = null, string? name = null, Guid? operationId = null) =>
        new(admin.ProfileScopeId, admin.CenterId, operationId ?? Guid.NewGuid(), unit ?? admin.UnitId, name ?? NewName("Equipo"));

    private static AdministracionQuery Query(SeededProfile admin) => new(admin.ProfileScopeId, admin.CenterId);

    private static ChangeTeamMemberCommand Member(SeededProfile admin, Guid teamId, SeededProfile account, bool add) =>
        new(admin.ProfileScopeId, admin.CenterId, teamId, account.AccountId, add);

    private static async Task<int> CountAuditAsync(Guid resourceId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE recurso_id = @Id AND accion_codigo = @Action AND perfil_activo = 'ADMINISTRACION'",
            new { Id = resourceId, Action = action });
    }

    [Fact]
    public async Task Turno_SeCreaConSusHoras_YElReenvioNoLoDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var operationId = Guid.NewGuid();
        var command = NewShift(admin, "  Noche  ", operationId, "22:00", "06:00");

        var created = await service.CreateShiftAsync(command);
        var again = await service.CreateShiftAsync(command);
        var reused = await service.CreateShiftAsync(command with { Fin = TimeOnly.Parse("07:00") });

        Assert.True(created.Ok, created.Error?.Message);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        var shift = Assert.Single((await service.ListShiftsAsync(Query(admin))).Value!);
        Assert.Equal(operationId, shift.ShiftId);
        Assert.Equal("Noche", shift.Name);
        Assert.Equal(new TimeOnly(22, 0), shift.Start);
        Assert.Equal(new TimeOnly(6, 0), shift.End);
        Assert.True(shift.CrossesMidnight);
        Assert.True(shift.Active);
        Assert.Equal(1, await CountAuditAsync(operationId, "SHIFT_CREATE"));
    }

    [Fact]
    public async Task Turno_RechazaNombreRepetidoODatosInvalidos_SeRenombra_YSeInactiva()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var first = (await service.CreateShiftAsync(NewShift(admin, "Mañana"))).Value;
        var second = (await service.CreateShiftAsync(NewShift(admin, "Tarde", start: "15:00", end: "23:00"))).Value;
        RenameShiftCommand Rename(Guid id, string name) => new(admin.ProfileScopeId, admin.CenterId, id, name);
        ChangeShiftStatusCommand Status(Guid id, bool active) => new(admin.ProfileScopeId, admin.CenterId, id, active);

        var sameName = await service.CreateShiftAsync(NewShift(admin, "MAÑANA"));
        var noName = await service.CreateShiftAsync(NewShift(admin, " "));
        var noHours = await service.CreateShiftAsync(NewShift(admin) with { Fin = null });
        var renamed = await service.RenameShiftAsync(Rename(first, "Mañana larga"));
        var renamedSame = await service.RenameShiftAsync(Rename(first, "Mañana larga"));
        var renamedTaken = await service.RenameShiftAsync(Rename(first, "Tarde"));
        var deactivated = await service.ChangeShiftStatusAsync(Status(second, false));
        var twice = await service.ChangeShiftStatusAsync(Status(second, false));
        var reactivated = await service.ChangeShiftStatusAsync(Status(second, true));

        Assert.Equal(ApplicationFailureCode.Conflict, sameName.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noName.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noHours.Error!.Code);
        Assert.True(renamed.Ok, renamed.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, renamedSame.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, renamedTaken.Error!.Code);
        Assert.True(deactivated.Ok, deactivated.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, twice.Error!.Code);
        Assert.True(reactivated.Ok, reactivated.Error?.Message);
        var listed = (await service.ListShiftsAsync(Query(admin))).Value!;
        Assert.Contains(listed, s => s.ShiftId == first && s.Name == "Mañana larga" && s.Start == new TimeOnly(7, 0));
        Assert.Equal(1, await CountAuditAsync(first, "SHIFT_RENAME"));
        Assert.Equal(1, await CountAuditAsync(second, "SHIFT_DEACTIVATE"));
        Assert.Equal(1, await CountAuditAsync(second, "SHIFT_ACTIVATE"));
    }

    [Fact]
    public async Task Equipo_SeCreaEnUnaUnidadDelAmbito_ConNombreUnicoPorUnidad_YSeRenombraEInactiva()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var foreignUnit = await AddUnitAsync(admin.CenterId);
        var operationId = Guid.NewGuid();
        var command = NewTeam(admin, name: "Equipo A", operationId: operationId);

        var created = await service.CreateTeamAsync(command);
        var again = await service.CreateTeamAsync(command);
        var reused = await service.CreateTeamAsync(command with { Nombre = "Otro" });
        var sameName = await service.CreateTeamAsync(NewTeam(admin, name: "equipo a"));
        var outOfScope = await service.CreateTeamAsync(NewTeam(admin, foreignUnit));
        var blank = await service.CreateTeamAsync(NewTeam(admin, name: " "));
        var renamed = await service.RenameTeamAsync(new RenameTeamCommand(admin.ProfileScopeId, admin.CenterId, operationId, "Equipo B"));
        var deactivated = await service.ChangeTeamStatusAsync(new ChangeTeamStatusCommand(admin.ProfileScopeId, admin.CenterId, operationId, false));
        var twice = await service.ChangeTeamStatusAsync(new ChangeTeamStatusCommand(admin.ProfileScopeId, admin.CenterId, operationId, false));

        Assert.True(created.Ok, created.Error?.Message);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, sameName.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, outOfScope.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, blank.Error!.Code);
        Assert.True(renamed.Ok, renamed.Error?.Message);
        Assert.True(deactivated.Ok, deactivated.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, twice.Error!.Code);
        var team = Assert.Single((await service.ListTeamsAsync(Query(admin))).Value!);
        Assert.Equal("Equipo B", team.Name);
        Assert.False(team.Active);
        Assert.Equal(admin.UnitId, team.UnitId);
        Assert.Equal(1, await CountAuditAsync(operationId, "TEAM_CREATE"));
        Assert.Equal(1, await CountAuditAsync(operationId, "TEAM_RENAME"));
        Assert.Equal(1, await CountAuditAsync(operationId, "TEAM_DEACTIVATE"));
    }

    [Fact]
    public async Task Miembros_SoloLosElegiblesDeLaUnidad_ConAltaBajaYAuditoria_SinCambiarSusPermisos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var elsewhere = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, otherUnit);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId);
        var teamId = (await service.CreateTeamAsync(NewTeam(admin))).Value;
        var read = new AuthorizationTarget.Create(admin.UnitId);
        var before = await new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory)
            .LoadEvidenceAsync(nurse.ExternalSubject, new AuthorizationSelection(nurse.ProfileScopeId, nurse.CenterId), read);

        var eligible = (await service.ListEligibleTeamMembersAsync(Query(admin), teamId)).Value!;
        var addNurse = await service.ChangeTeamMemberAsync(Member(admin, teamId, nurse, true));
        var addAgain = await service.ChangeTeamMemberAsync(Member(admin, teamId, nurse, true));
        var addDoctor = await service.ChangeTeamMemberAsync(Member(admin, teamId, doctor, true));
        var addElsewhere = await service.ChangeTeamMemberAsync(Member(admin, teamId, elsewhere, true));
        var addDirection = await service.ChangeTeamMemberAsync(Member(admin, teamId, direction, true));
        var eligibleAfter = (await service.ListEligibleTeamMembersAsync(Query(admin), teamId)).Value!;
        var team = Assert.Single((await service.ListTeamsAsync(Query(admin))).Value!);
        var removeDoctor = await service.ChangeTeamMemberAsync(Member(admin, teamId, doctor, false));
        var removeTwice = await service.ChangeTeamMemberAsync(Member(admin, teamId, doctor, false));
        var after = await new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory)
            .LoadEvidenceAsync(nurse.ExternalSubject, new AuthorizationSelection(nurse.ProfileScopeId, nurse.CenterId), read);

        Assert.Equal(new[] { nurse.AccountId.Value, doctor.AccountId.Value }.Order(), eligible.Select(e => e.AccountId.Value).Order());
        Assert.DoesNotContain(eligible, e => e.AccountId == elsewhere.AccountId || e.AccountId == direction.AccountId);
        Assert.True(addNurse.Ok, addNurse.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, addAgain.Error!.Code);
        Assert.True(addDoctor.Ok, addDoctor.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, addElsewhere.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, addDirection.Error!.Code);
        Assert.Empty(eligibleAfter);
        Assert.Equal(2, team.Members.Count);
        Assert.All(team.Members, m => Assert.True(m.Eligible));
        Assert.Contains(team.Members, m => m.AccountId == nurse.AccountId && m.Profiles.SequenceEqual([SystemProfile.Enfermeria]));
        Assert.True(removeDoctor.Ok, removeDoctor.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, removeTwice.Error!.Code);
        Assert.Equal(1, await CountAuditAsync(nurse.AccountId.Value, "TEAM_MEMBER_ADD"));
        Assert.Equal(1, await CountAuditAsync(doctor.AccountId.Value, "TEAM_MEMBER_REMOVE"));
        // Pertenecer a un equipo no cambia lo que la cuenta puede hacer.
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equivalent(before, after);
    }

    [Fact]
    public async Task UnMiembroQueYaNoTienePerfilEnLaUnidad_SigueEnElEquipo_PeroSaleComoNoElegible_YUnEquipoInactivoNoAdmiteMiembros()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var other = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var teamId = (await service.CreateTeamAsync(NewTeam(admin))).Value;
        Assert.True((await service.ChangeTeamMemberAsync(Member(admin, teamId, nurse, true))).Ok);

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @actor WHERE id = @id",
                new { actor = admin.AccountId.Value, id = nurse.ProfileScopeId });
        }

        Assert.True((await service.ChangeTeamStatusAsync(new ChangeTeamStatusCommand(admin.ProfileScopeId, admin.CenterId, teamId, false))).Ok);
        var team = Assert.Single((await service.ListTeamsAsync(Query(admin))).Value!);
        var addToInactive = await service.ChangeTeamMemberAsync(Member(admin, teamId, other, true));

        var member = Assert.Single(team.Members);
        Assert.Equal(nurse.AccountId, member.AccountId);
        Assert.False(member.Eligible);
        Assert.Empty(member.Profiles);
        Assert.Equal(ApplicationFailureCode.InvalidInput, addToInactive.Error!.Code);
    }

    [Fact]
    public async Task OtroPerfil_OtroAmbito_OUnaUnidadAjena_DanAccesoDenegado_YNoSeVenSusEquipos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var shiftId = (await Build(admin.ExternalSubject).CreateShiftAsync(NewShift(admin))).Value;
        var teamId = (await Build(admin.ExternalSubject).CreateTeamAsync(NewTeam(admin))).Value;
        var foreign = Build(other.ExternalSubject);
        var nurseService = Build(nurse.ExternalSubject);

        var results = new[]
        {
            (await nurseService.ListShiftsAsync(Query(nurse))).Error,
            (await nurseService.ListTeamsAsync(Query(nurse))).Error,
            (await nurseService.CreateShiftAsync(NewShift(nurse))).Error,
            (await nurseService.CreateTeamAsync(NewTeam(nurse))).Error,
            (await foreign.ListShiftsAsync(new AdministracionQuery(other.ProfileScopeId, admin.CenterId))).Error,
            (await foreign.RenameShiftAsync(new RenameShiftCommand(other.ProfileScopeId, other.CenterId, shiftId, "Robado"))).Error,
            (await foreign.RenameTeamAsync(new RenameTeamCommand(other.ProfileScopeId, other.CenterId, teamId, "Robado"))).Error,
            (await foreign.ChangeTeamStatusAsync(new ChangeTeamStatusCommand(other.ProfileScopeId, other.CenterId, teamId, false))).Error,
            (await foreign.ChangeTeamMemberAsync(new ChangeTeamMemberCommand(other.ProfileScopeId, other.CenterId, teamId, nurse.AccountId, true))).Error,
        };

        Assert.All(results, error => Assert.Equal(ApplicationFailureCode.AccessDenied, error!.Code));
        Assert.Empty((await foreign.ListTeamsAsync(Query(other))).Value!);
        Assert.Empty((await foreign.ListShiftsAsync(Query(other))).Value!);
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaCambiarLasHorasDeUnTurno_NiBorrar_NiReescribirMiembros()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var shiftId = (await service.CreateShiftAsync(NewShift(admin))).Value;
        var teamId = (await service.CreateTeamAsync(NewTeam(admin))).Value;
        Assert.True((await service.ChangeTeamMemberAsync(Member(admin, teamId, nurse, true))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var hours = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.turnos_catalogo SET hora_fin = '09:00' WHERE id = @shiftId", new { shiftId }));
        var shiftDelete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.turnos_catalogo WHERE id = @shiftId", new { shiftId }));
        var teamUnit = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.equipos SET unidad_id = unidad_id, centro_id = NEWID() WHERE id = @teamId", new { teamId }));
        var teamDelete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.equipos WHERE id = @teamId", new { teamId }));
        var memberDelete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.equipos_miembros WHERE equipo_id = @teamId", new { teamId }));
        var memberRewrite = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.equipos_miembros SET cuenta_id = @other WHERE equipo_id = @teamId", new { teamId, other = admin.AccountId.Value }));

        Assert.Contains("SHIFT_IMMUTABLE_FIELD", hours.Message);
        Assert.Contains("SHIFT_DELETE_FORBIDDEN", shiftDelete.Message);
        Assert.True(teamUnit.Message.Contains("TEAM_IMMUTABLE_FIELD") || teamUnit.Message.Contains("FK_"), teamUnit.Message);
        Assert.Contains("TEAM_DELETE_FORBIDDEN", teamDelete.Message);
        Assert.Contains("TEAM_MEMBER_DELETE_FORBIDDEN", memberDelete.Message);
        Assert.Contains("TEAM_MEMBER_REVOKE_ONLY", memberRewrite.Message);
    }

    [Fact]
    public async Task LaAuditoria_MuestraLasAccionesDeTurnosYEquipos_ConLaCuentaAfectada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        await service.CreateShiftAsync(NewShift(admin));
        var teamId = (await service.CreateTeamAsync(NewTeam(admin))).Value;
        await service.ChangeTeamMemberAsync(Member(admin, teamId, nurse, true));

        var page = (await service.ListAuditAsync(new ListAdministrativeAuditQuery(
            admin.ProfileScopeId, admin.CenterId, DateOnly.FromDateTime(DateTime.Today).AddDays(-1), DateOnly.FromDateTime(DateTime.Today).AddDays(1)))).Value!;

        Assert.Contains(page.Entries, e => e.Action == "SHIFT_CREATE");
        Assert.Contains(page.Entries, e => e.Action == "TEAM_CREATE" && e.UnitName is not null);
        Assert.Contains(page.Entries, e => e.Action == "TEAM_MEMBER_ADD" && e.AffectedName == nurse.ExternalSubject);
        Assert.All(AdministrativeAudit.Actions.Where(a => a.Category == AuditCategory.TurnosYEquipos), a => Assert.True(AdministrativeAudit.IsAdministrative(a.Code)));
    }
}
