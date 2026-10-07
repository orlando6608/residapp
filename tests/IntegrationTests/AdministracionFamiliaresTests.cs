using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Families;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, bloque 2 (historia 3, ADM-08 a ADM-11, FAM-01, script 0022): familiares, autorizaciones de
/// acceso y contacto urgente. Cada prueba crea su propio centro.</summary>
public class AdministracionFamiliaresTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static AddFamilyMemberCommand Add(
        SeededProfile seed, ResidentId residentId, Guid? operationId = null, string name = "Lucía Pérez", string phone = "600 123 456") =>
        new(seed.ProfileScopeId, seed.CenterId, residentId, operationId ?? Guid.NewGuid(), name, "Hija", phone, null);

    private static ChangeFamilyAuthorizationCommand Change(
        SeededProfile seed, ResidentId residentId, Guid linkId, FamilyAuthorizationChange change, int expected,
        DateOnly? validUntil = null, string? reason = null) =>
        new(seed.ProfileScopeId, seed.CenterId, residentId, linkId, change, validUntil, reason, expected);

    private static DesignateEmergencyContactCommand Designate(SeededProfile seed, ResidentId residentId, int expected, params Guid[] linkIds) =>
        new(seed.ProfileScopeId, seed.CenterId, residentId, linkIds, expected);

    private static async Task<AdministrativeResidentDetail> DetailAsync(SeededProfile seed, ResidentId residentId) =>
        (await Build(seed.ExternalSubject).FindResidentAsync(new FindAdministrativeResidentQuery(seed.ProfileScopeId, seed.CenterId, residentId))).Value!;

    private static async Task<int> CountAuditAsync(ResidentId residentId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND accion_codigo = @Action",
            new { Id = residentId.Value, Action = action });
    }

    [Fact]
    public async Task Familiar_SeCreaSinAutorizacion_YUnReenvioNoLoDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Familia");
        var service = Build(admin.ExternalSubject);
        var operationId = Guid.NewGuid();

        var first = await service.AddFamilyMemberAsync(Add(admin, residentId, operationId));
        var resent = await service.AddFamilyMemberAsync(Add(admin, residentId, operationId));
        var invalidPhone = await service.AddFamilyMemberAsync(Add(admin, residentId, phone: "llamar a recepción"));
        var detail = await DetailAsync(admin, residentId);

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(first.Value, resent.Value);
        Assert.Equal(ApplicationFailureCode.InvalidInput, invalidPhone.Error!.Code);
        var member = Assert.Single(detail.Family);
        Assert.Equal(("Lucía Pérez", "Hija", "600 123 456", (string?)null), (member.DisplayName, member.Relationship, member.Phone, member.Email));
        Assert.Null(member.CurrentAuthorization);
        Assert.Empty(detail.CurrentEmergencyContacts);
        Assert.Equal(1, await CountAuditAsync(residentId, "FAMILY_MEMBER_CREATE"));
    }

    [Fact]
    public async Task Autorizacion_RecorreLosEstados_YRechazaLoQueNoToca()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Autorización");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;

        var activateBeforeOpen = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 0));
        var open = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0));
        var openAgain = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0));
        var activate = await service.ChangeFamilyAuthorizationAsync(
            Change(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 1, validUntil: Today.AddDays(30)));
        var suspendWithoutReason = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Suspender, 2));
        var suspend = await service.ChangeFamilyAuthorizationAsync(
            Change(admin, residentId, linkId, FamilyAuthorizationChange.Suspender, 2, reason: "Petición del propio familiar."));
        var reactivate = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 3));
        var revoke = await service.ChangeFamilyAuthorizationAsync(
            Change(admin, residentId, linkId, FamilyAuthorizationChange.Revocar, 4, reason: "Deja de ser familiar autorizado."));
        var afterRevoke = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 5));
        var stale = await service.ChangeFamilyAuthorizationAsync(
            Change(admin, residentId, linkId, FamilyAuthorizationChange.Revocar, 3, reason: "Otra vez."));
        var member = Assert.Single((await DetailAsync(admin, residentId)).Family);

        Assert.Equal(ApplicationFailureCode.InvalidInput, activateBeforeOpen.Error!.Code);
        Assert.Equal([1, 2, 3, 4, 5], new[] { open, activate, suspend, reactivate, revoke }.Select(r => r.Value));
        Assert.Equal(ApplicationFailureCode.Conflict, openAgain.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, suspendWithoutReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, afterRevoke.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(
            [FamilyAuthorizationStatus.Pendiente, FamilyAuthorizationStatus.Activa, FamilyAuthorizationStatus.Suspendida,
             FamilyAuthorizationStatus.Activa, FamilyAuthorizationStatus.Revocada],
            member.AuthorizationChanges.Select(c => c.Status));
        Assert.Equal(Today.AddDays(30), member.AuthorizationChanges[1].ValidUntil);
        Assert.Equal("Petición del propio familiar.", member.AuthorizationChanges[2].Reason);
        Assert.Equal(5, await CountAuditAsync(residentId, "FAMILY_AUTHORIZATION_CHANGE"));
    }

    [Fact]
    public async Task Autorizacion_Caducada_SeRenueva_PeroNoSeSuspende()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Caducidad");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;
        Assert.True((await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0))).Ok);
        var pastDate = await service.ChangeFamilyAuthorizationAsync(
            Change(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 1, validUntil: Today.AddDays(-1)));
        // Una activación que ya ha vencido solo puede venir del paso del tiempo: se inserta directamente.
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.familiares_autorizaciones_cambios
                    (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
                VALUES (NEWID(), @CenterId, @LinkId, 2, 'ACTIVA', @Yesterday, NULL, @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
                """, new { CenterId = admin.CenterId.Value, LinkId = linkId, Yesterday = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), AccountId = admin.AccountId.Value });
        }

        var expired = Assert.Single((await DetailAsync(admin, residentId)).Family).CurrentAuthorization!;
        var suspend = await service.ChangeFamilyAuthorizationAsync(
            Change(admin, residentId, linkId, FamilyAuthorizationChange.Suspender, 2, reason: "No procede."));
        var renew = await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Activar, 2, validUntil: Today));

        Assert.Equal(ApplicationFailureCode.InvalidInput, pastDate.Error!.Code);
        Assert.Equal(FamilyAuthorizationStatus.Caducada, FamilyAuthorizationRules.Effective(expired.Status, expired.ValidUntil, Today));
        Assert.Equal(ApplicationFailureCode.InvalidInput, suspend.Error!.Code);
        Assert.Equal(3, renew.Value);
    }

    [Fact]
    public async Task ContactoUrgente_PuedeHaberVarios_SeCambianYNuncaSeQuedaSinNinguno()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Contacto");
        var otherResident = await CreateResidentAsync(admin, "Residente Vecino");
        var service = Build(admin.ExternalSubject);
        var daughter = (await service.AddFamilyMemberAsync(Add(admin, residentId, name: "Hija Contacto"))).Value;
        var son = (await service.AddFamilyMemberAsync(Add(admin, residentId, name: "Hijo Contacto"))).Value;
        var neighbour = (await service.AddFamilyMemberAsync(Add(admin, otherResident, name: "Familiar Ajeno"))).Value;

        var first = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 0, daughter));
        var same = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 1, daughter));
        var foreign = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 1, neighbour));
        var changed = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 1, son));
        var stale = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 1, daughter));
        var both = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 3, daughter, son));
        var none = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 4));
        var detail = await DetailAsync(admin, residentId);

        Assert.Equal([1, 3, 4], new[] { first, changed, both }.Select(r => r.Value));
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, none.Error!.Code);
        Assert.Equal(["AGREGAR", "QUITAR", "AGREGAR", "AGREGAR"], detail.EmergencyContacts.Select(c => c.Action));
        Assert.Equal(["Hija Contacto", "Hija Contacto", "Hijo Contacto", "Hija Contacto"], detail.EmergencyContacts.Select(c => c.DisplayName));
        Assert.Equal([son, daughter], detail.CurrentEmergencyContacts);
        Assert.Equal(4, await CountAuditAsync(residentId, "EMERGENCY_CONTACT_DESIGNATE"));
    }

    [Fact]
    public async Task Editar_CambiaLosDatos_YRechazaSinCambiosOFamiliarAjeno()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Edición");
        var otherResident = await CreateResidentAsync(admin, "Residente Otro");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;
        UpdateFamilyMemberCommand Update(ResidentId resident, string phone, string? email, string version) =>
            new(admin.ProfileScopeId, admin.CenterId, resident, linkId, "Lucía Pérez", "Hija", phone, email, version);

        var initialVersion = await FamilyVersionAsync(admin, residentId);
        var updated = await service.UpdateFamilyMemberAsync(Update(residentId, "+34 611 222 333", "lucia@example.org", initialVersion));
        var currentVersion = await FamilyVersionAsync(admin, residentId);
        var unchanged = await service.UpdateFamilyMemberAsync(Update(residentId, "+34 611 222 333", "lucia@example.org", currentVersion));
        var badEmail = await service.UpdateFamilyMemberAsync(Update(residentId, "+34 611 222 333", "lucia.example.org", currentVersion));
        var foreign = await service.UpdateFamilyMemberAsync(Update(otherResident, "699 999 999", null, initialVersion));
        var member = Assert.Single((await DetailAsync(admin, residentId)).Family);

        Assert.True(updated.Ok, updated.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unchanged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, badEmail.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        Assert.Equal(("+34 611 222 333", "lucia@example.org"), (member.Phone, member.Email));
        Assert.Equal(1, await CountAuditAsync(residentId, "FAMILY_MEMBER_UPDATE"));
    }

    [Fact]
    public async Task Editar_ConUnaVersionObsoleta_EsConflicto_YNoPisaElCambioDeOtraPersona()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Concurrencia");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;
        UpdateFamilyMemberCommand Update(string phone, string version) =>
            new(admin.ProfileScopeId, admin.CenterId, residentId, linkId, "Lucía Pérez", "Hija", phone, null, version);
        var openedByBoth = await FamilyVersionAsync(admin, residentId);

        var first = await service.UpdateFamilyMemberAsync(Update("+34 611 000 111", openedByBoth));
        var second = await service.UpdateFamilyMemberAsync(Update("+34 622 000 222", openedByBoth));
        var blank = await service.UpdateFamilyMemberAsync(Update("+34 633 000 333", ""));
        var afterConflicts = Assert.Single((await DetailAsync(admin, residentId)).Family);
        var retried = await service.UpdateFamilyMemberAsync(Update("+34 622 000 222", await FamilyVersionAsync(admin, residentId)));

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, second.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, blank.Error!.Code);
        Assert.Equal("+34 611 000 111", afterConflicts.Phone);
        Assert.True(retried.Ok, retried.Error?.Message);
        Assert.Equal("+34 622 000 222", Assert.Single((await DetailAsync(admin, residentId)).Family).Phone);
        Assert.Equal(2, await CountAuditAsync(residentId, "FAMILY_MEMBER_UPDATE"));
    }

    [Fact]
    public async Task Vincular_UnFamiliarDeOtroResidenteDelAmbito_ConSuPropiaRelacion_SinAbrirAutorizacion_YElReenvioNoLoDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var first = await CreateResidentAsync(admin, "Residente Primero");
        var second = await CreateResidentAsync(admin, "Residente Segundo");
        var service = Build(admin.ExternalSubject);
        var familyId = Guid.NewGuid();
        await service.AddFamilyMemberAsync(Add(admin, first, familyId, "Marta Gil", "611 000 111"));
        FindAdministrativeResidentQuery Query(ResidentId resident) => new(admin.ProfileScopeId, admin.CenterId, resident);
        LinkFamilyMemberCommand Link(Guid operationId, string? relation = "Sobrina") =>
            new(admin.ProfileScopeId, admin.CenterId, second, operationId, familyId, relation);

        var beforeFirst = (await service.ListLinkableFamilyAsync(Query(first))).Value!;
        var beforeSecond = (await service.ListLinkableFamilyAsync(Query(second))).Value!;
        var operation = Guid.NewGuid();
        var linked = await service.LinkFamilyMemberAsync(Link(operation));
        var resent = await service.LinkFamilyMemberAsync(Link(operation));
        var again = await service.LinkFamilyMemberAsync(Link(Guid.NewGuid()));
        var reused = await service.LinkFamilyMemberAsync(Link(operation, "Prima"));
        var blank = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(admin.ProfileScopeId, admin.CenterId, second, Guid.NewGuid(), familyId, "  "));
        var afterSecond = (await service.ListLinkableFamilyAsync(Query(second))).Value!;
        var firstDetail = await DetailAsync(admin, first);
        var secondDetail = await DetailAsync(admin, second);

        Assert.Empty(beforeFirst);
        Assert.Equal([new LinkableFamilyMember(familyId, "Marta Gil", "611 000 111")], beforeSecond);
        Assert.True(linked.Ok, linked.Error?.Message);
        Assert.Equal(operation, linked.Value);
        Assert.Equal(operation, resent.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, blank.Error!.Code);
        Assert.Empty(afterSecond);
        var inFirst = Assert.Single(firstDetail.Family);
        var inSecond = Assert.Single(secondDetail.Family);
        Assert.Equal(("Hija", 1), (inFirst.Relationship, inFirst.OtherResidentLinks));
        Assert.Equal(("Sobrina", 1, "Marta Gil", operation), (inSecond.Relationship, inSecond.OtherResidentLinks, inSecond.DisplayName, inSecond.LinkId));
        Assert.Null(inSecond.CurrentAuthorization);
        Assert.Equal(1, await CountAuditAsync(second, "FAMILY_MEMBER_LINK"));
        Assert.Equal(0, await CountAuditAsync(first, "FAMILY_MEMBER_LINK"));
    }

    [Fact]
    public async Task Vincular_NoOfreceNiAdmiteFamiliaresLigadosSoloAResidentesAjenosAlAmbito_NiDeOtroCentro_NiAOtroPerfil()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var other = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var foreignCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var mine = await CreateResidentAsync(admin, "Residente Propio");
        var theirs = await CreateResidentAsync(other, "Residente Ajeno", otherUnit);
        var foreign = await CreateResidentAsync(foreignCenter, "Residente De Otro Centro");
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var theirFamily = Guid.NewGuid();
        var foreignFamily = Guid.NewGuid();
        await Build(other.ExternalSubject).AddFamilyMemberAsync(Add(other, theirs, theirFamily));
        await Build(foreignCenter.ExternalSubject).AddFamilyMemberAsync(Add(foreignCenter, foreign, foreignFamily));
        var service = Build(admin.ExternalSubject);
        LinkFamilyMemberCommand Link(Guid familyId) => new(admin.ProfileScopeId, admin.CenterId, mine, Guid.NewGuid(), familyId, "Amigo");

        var listed = (await service.ListLinkableFamilyAsync(new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, mine))).Value!;
        var ofTheirs = await service.LinkFamilyMemberAsync(Link(theirFamily));
        var ofForeignCenter = await service.LinkFamilyMemberAsync(Link(foreignFamily));
        var unknown = await service.LinkFamilyMemberAsync(Link(Guid.NewGuid()));
        var forTheirResident = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, theirs, Guid.NewGuid(), theirFamily, "Amigo"));
        var byNurse = await Build(nurse.ExternalSubject).LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            nurse.ProfileScopeId, nurse.CenterId, mine, Guid.NewGuid(), theirFamily, "Amigo"));
        var nurseList = await Build(nurse.ExternalSubject).ListLinkableFamilyAsync(new FindAdministrativeResidentQuery(nurse.ProfileScopeId, nurse.CenterId, mine));

        Assert.Empty(listed);
        Assert.All(new[] { ofTheirs, ofForeignCenter, unknown, forTheirResident, byNurse }, r => Assert.Equal(ApplicationFailureCode.AccessDenied, r.Error!.Code));
        Assert.Equal(ApplicationFailureCode.AccessDenied, nurseList.Error!.Code);
        Assert.Empty((await DetailAsync(admin, mine)).Family);
    }

    private static async Task<string> FamilyVersionAsync(SeededProfile admin, ResidentId residentId)
    {
        var member = Assert.Single((await DetailAsync(admin, residentId)).Family);
        return new FamilyMemberData(member.DisplayName, member.Relationship, member.Phone, member.Email).Version;
    }

    [Fact]
    public async Task Familiares_SoloLosGestionaAdministracion_YNoFueraDeSuAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Permisos");
        var linkId = (await Build(admin.ExternalSubject).AddFamilyMemberAsync(Add(admin, residentId))).Value;
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var denied = new List<ApplicationFailureCode?>();
        foreach (var profile in new[] { SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Auxiliar, SystemProfile.DireccionClinica })
        {
            var other = await SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId);
            var service = Build(other.ExternalSubject);
            denied.Add((await service.AddFamilyMemberAsync(Add(other, residentId))).Error?.Code);
            denied.Add((await service.ChangeFamilyAuthorizationAsync(Change(other, residentId, linkId, FamilyAuthorizationChange.Abrir, 0))).Error?.Code);
            denied.Add((await service.DesignateEmergencyContactAsync(Designate(other, residentId, 0, linkId))).Error?.Code);
        }

        denied.Add((await Build(otherAdmin.ExternalSubject).AddFamilyMemberAsync(Add(otherAdmin, residentId))).Error?.Code);
        denied.Add((await Build(otherAdmin.ExternalSubject).ChangeFamilyAuthorizationAsync(
            Change(otherAdmin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0))).Error?.Code);

        Assert.All(denied, code => Assert.Equal(ApplicationFailureCode.AccessDenied, code));
        Assert.Null(Assert.Single((await DetailAsync(admin, residentId)).Family).CurrentAuthorization);
        Assert.Empty((await DetailAsync(admin, residentId)).CurrentEmergencyContacts);
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaTocarElHistorial_NiSaltarseTransiciones()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Historial");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;
        Assert.True((await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0))).Ok);
        Assert.True((await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 0, linkId))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var parameters = new { LinkId = linkId, CenterId = admin.CenterId.Value, AccountId = admin.AccountId.Value, ResidentId = residentId.Value };

        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.familiares_autorizaciones_cambios SET estado_codigo = 'ACTIVA' WHERE vinculo_id = @LinkId", parameters));
        var suspendPending = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.familiares_autorizaciones_cambios
                (id, centro_id, vinculo_id, numero, estado_codigo, valida_hasta, motivo, registrado_por_cuenta_id, registrado_por_perfil, registrado_en)
            VALUES (NEWID(), @CenterId, @LinkId, 2, 'SUSPENDIDA', NULL, N'x', @AccountId, 'ADMINISTRACION', SYSUTCDATETIME())
            """, parameters));
        var deleteContact = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.residentes_contacto_urgente WHERE residente_id = @ResidentId", parameters));
        var unlink = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.residentes_familiares WHERE id = @LinkId", parameters));

        Assert.Contains("FAMILY_AUTHORIZATION_CHANGE_IMMUTABLE", update.Message);
        Assert.Contains("FAMILY_AUTHORIZATION_TRANSITION_INVALID", suspendPending.Message);
        Assert.Contains("EMERGENCY_CONTACT_DESIGNATION_IMMUTABLE", deleteContact.Message);
        Assert.Contains("RESIDENT_FAMILY_LINK_DELETE_FORBIDDEN", unlink.Message);
    }

    [Fact]
    public void TiposDeFamiliares_NoTienenNingunDatoClinico()
    {
        var properties = new[] { typeof(ResidentFamilyMember), typeof(FamilyAuthorizationChangeEntry), typeof(EmergencyContactDesignation) }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name + ":" + p.PropertyType.Name).ToList();

        Assert.DoesNotContain(properties, p => p.Contains("Baseline", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Basal", StringComparison.OrdinalIgnoreCase) || p.Contains("Barthel", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Event", StringComparison.OrdinalIgnoreCase) || p.Contains("Clinical", StringComparison.OrdinalIgnoreCase));
    }
}
