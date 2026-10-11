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
    public async Task ContactoUrgente_PuedeHaberVarios_SeCambian_YSeQuedaSinNingunoSoloConUnMotivo()
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

        // CJ, 2026-10-10: el residente puede quedarse sin contacto urgente, con su motivo (una sola designación), y no se repite.
        DesignateEmergencyContactCommand NoContact(int expected, string? reason) =>
            new(admin.ProfileScopeId, admin.CenterId, residentId, [], expected, reason);
        var blank = await service.DesignateEmergencyContactAsync(NoContact(4, "   "));
        var tooLong = await service.DesignateEmergencyContactAsync(NoContact(4, new string('x', 501)));
        var unwanted = await service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, [daughter, son], 4, "Se ignora con contactos"));
        var withReason = await service.DesignateEmergencyContactAsync(NoContact(4, "  Sin familia conocida  "));
        var emptied = await DetailAsync(admin, residentId);
        var again = await service.DesignateEmergencyContactAsync(NoContact(5, "Otra vez"));
        var designatedAgain = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 5, son));
        var restored = await DetailAsync(admin, residentId);

        Assert.Equal(ApplicationFailureCode.InvalidInput, blank.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unwanted.Error!.Code);
        Assert.Equal(5, withReason.Value);
        Assert.Empty(emptied.CurrentEmergencyContacts);
        Assert.Equal("Sin familia conocida", emptied.NoContactReason);
        Assert.Equal(["AGREGAR", "QUITAR", "AGREGAR", "AGREGAR", "SIN_CONTACTO"], emptied.EmergencyContacts.Select(c => c.Action));
        Assert.Equal(ApplicationFailureCode.InvalidInput, again.Error!.Code);
        Assert.Equal(6, designatedAgain.Value);
        Assert.Equal([son], restored.CurrentEmergencyContacts);
        Assert.Null(restored.NoContactReason);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // La base de datos exige el motivo en «sin contacto» y lo prohíbe en las demás acciones.
        foreach (var sql in new[]
        {
            "INSERT INTO dbo.residentes_contacto_urgente (id, centro_id, residente_id, numero, vinculo_id, accion_codigo, motivo, designado_por_cuenta_id, designado_por_perfil, designado_en) SELECT NEWID(), centro_id, residente_id, 99, NULL, 'SIN_CONTACTO', NULL, designado_por_cuenta_id, designado_por_perfil, designado_en FROM dbo.residentes_contacto_urgente WHERE residente_id = @ResidentId AND numero = 1",
            "INSERT INTO dbo.residentes_contacto_urgente (id, centro_id, residente_id, numero, vinculo_id, accion_codigo, motivo, designado_por_cuenta_id, designado_por_perfil, designado_en) SELECT NEWID(), centro_id, residente_id, 99, vinculo_id, 'AGREGAR', 'no debería llevar motivo', designado_por_cuenta_id, designado_por_perfil, designado_en FROM dbo.residentes_contacto_urgente WHERE residente_id = @ResidentId AND numero = 1",
            "UPDATE dbo.residentes_contacto_urgente SET motivo = 'otro motivo' WHERE residente_id = @ResidentId AND accion_codigo = 'SIN_CONTACTO'",
        })
        {
            await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { ResidentId = residentId.Value }));
        }
    }

    [Fact]
    public async Task Desvincular_ElUnicoContactoUrgente_DejaAlResidenteSinContacto_ConElMotivoDelDesvinculo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Unico Contacto");
        var service = Build(admin.ExternalSubject);
        var daughter = (await service.AddFamilyMemberAsync(Add(admin, residentId, name: "Hija Unica"))).Value;
        Assert.True((await service.DesignateEmergencyContactAsync(Designate(admin, residentId, 0, daughter))).Ok);

        var unlinked = await service.UnlinkFamilyMemberAsync(new UnlinkFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, daughter, "Se ha trasladado a otro país."));
        var detail = await DetailAsync(admin, residentId);

        Assert.True(unlinked.Ok, unlinked.Error?.Message);
        Assert.Empty(detail.CurrentEmergencyContacts);
        Assert.Equal("Único contacto urgente desvinculado: Se ha trasladado a otro país.", detail.NoContactReason);
        Assert.Equal(["AGREGAR", "SIN_CONTACTO"], detail.EmergencyContacts.Select(c => c.Action));
        Assert.Empty(detail.Family);
    }

    [Fact]
    public async Task Desvincular_ConservaElVinculoPasado_RevocaLaAutorizacion_QuitaElContacto_YElFamiliarSigueConLosDemas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var first = await CreateResidentAsync(admin, "Residente Desvincula Uno");
        var second = await CreateResidentAsync(admin, "Residente Desvincula Dos");
        var service = Build(admin.ExternalSubject);
        var daughter = (await service.AddFamilyMemberAsync(Add(admin, first, name: "Hija Desvinculada"))).Value;
        var son = (await service.AddFamilyMemberAsync(Add(admin, first, name: "Hijo Que Se Queda"))).Value;
        var familyId = Guid.NewGuid();
        await service.AddFamilyMemberAsync(Add(admin, first, familyId, "Prima Compartida", "611 000 222"));
        var shared = (await DetailAsync(admin, first)).Family.Single(f => f.DisplayName == "Prima Compartida").LinkId;
        Assert.True((await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, second, Guid.NewGuid(), familyId, "Sobrina"))).Ok);
        Assert.True((await service.ChangeFamilyAuthorizationAsync(Change(admin, first, daughter, FamilyAuthorizationChange.Abrir, 0))).Ok);
        Assert.True((await service.ChangeFamilyAuthorizationAsync(Change(admin, first, daughter, FamilyAuthorizationChange.Activar, 1))).Ok);
        Assert.True((await service.DesignateEmergencyContactAsync(Designate(admin, first, 0, daughter, son))).Ok);
        UnlinkFamilyMemberCommand Unlink(Guid link, string? reason = "Ya no mantiene relación con el residente.", ResidentId? resident = null) =>
            new(admin.ProfileScopeId, admin.CenterId, resident ?? first, link, reason);

        var noReason = await service.UnlinkFamilyMemberAsync(Unlink(daughter, "  "));
        var unlinked = await service.UnlinkFamilyMemberAsync(Unlink(daughter));
        var again = await service.UnlinkFamilyMemberAsync(Unlink(daughter));
        var wrongResident = await service.UnlinkFamilyMemberAsync(Unlink(son, resident: second));
        var sharedUnlinked = await service.UnlinkFamilyMemberAsync(Unlink(shared, "Error al vincularla."));
        var firstDetail = await DetailAsync(admin, first);
        var secondDetail = await DetailAsync(admin, second);

        Assert.Equal(ApplicationFailureCode.InvalidInput, noReason.Error!.Code);
        Assert.True(unlinked.Ok, unlinked.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, wrongResident.Error!.Code);
        Assert.True(sharedUnlinked.Ok, sharedUnlinked.Error?.Message);
        // El vínculo pasado se ve en el historial; el contacto urgente que queda es el hijo.
        Assert.Equal(["Hijo Que Se Queda"], firstDetail.Family.Select(f => f.DisplayName));
        Assert.Equal([son], firstDetail.CurrentEmergencyContacts);
        Assert.Equal(["Prima Compartida", "Hija Desvinculada"], firstDetail.Former.Select(f => f.DisplayName));
        Assert.Equal("Ya no mantiene relación con el residente.", firstDetail.Former[1].Reason);
        // La prima sigue vinculada al otro residente, ya sin vínculos compartidos.
        var inSecond = Assert.Single(secondDetail.Family);
        Assert.Equal(("Prima Compartida", 0), (inSecond.DisplayName, inSecond.OtherResidentLinks));
        Assert.Equal(2, await CountAuditAsync(first, "FAMILY_MEMBER_UNLINK"));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(["ACTIVA", "REVOCADA"], (await connection.QueryAsync<string>(
            "SELECT estado_codigo FROM dbo.familiares_autorizaciones_cambios WHERE vinculo_id = @daughter AND estado_codigo <> 'PENDIENTE' ORDER BY numero",
            new { daughter })).ToArray());
        // La base de datos no deja tocar un vínculo desvinculado, y vincular de nuevo al mismo familiar crea un vínculo nuevo.
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.residentes_familiares SET relacion = 'Otra' WHERE id = @daughter", new { daughter }));
        var relinked = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, first, Guid.NewGuid(), familyId, "Prima"));
        Assert.True(relinked.Ok, relinked.Error?.Message);
        Assert.NotEqual(shared, relinked.Value);
    }

    [Fact]
    public async Task Desvincular_SoloLoHaceAdministracionDelAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Desvincula Permisos");
        var linkId = (await Build(admin.ExternalSubject).AddFamilyMemberAsync(Add(admin, residentId))).Value;
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var denied = new List<ApplicationFailureCode?>();
        foreach (var profile in new[] { SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Auxiliar, SystemProfile.DireccionClinica })
        {
            var other = await SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId);
            denied.Add((await Build(other.ExternalSubject).UnlinkFamilyMemberAsync(
                new UnlinkFamilyMemberCommand(other.ProfileScopeId, other.CenterId, residentId, linkId, "Motivo"))).Error?.Code);
        }

        denied.Add((await Build(otherAdmin.ExternalSubject).UnlinkFamilyMemberAsync(
            new UnlinkFamilyMemberCommand(otherAdmin.ProfileScopeId, otherAdmin.CenterId, residentId, linkId, "Motivo"))).Error?.Code);

        Assert.All(denied, code => Assert.Equal(ApplicationFailureCode.AccessDenied, code));
        Assert.Single((await DetailAsync(admin, residentId)).Family);
    }

    [Fact]
    public async Task FamiliarEnUnidadesDistintas_LoEditaCualquieraQueGestioneAAlgunoDeSusResidentes_SinVerLosDemas()
    {
        // CJ (2026-10-07, 2.2): los datos de contacto son compartidos y los cambia cualquiera que gestione a alguno de sus residentes.
        var adminOne = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var unitTwo = await AdministracionResidentesTests.AddUnitAsync(adminOne.CenterId);
        var adminTwo = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, adminOne.CenterId, unitTwo);
        var residentOne = await CreateResidentAsync(adminOne, "Residente Unidad Uno");
        var residentTwo = await CreateResidentAsync(adminOne, "Residente Unidad Dos", unitTwo);
        var familyId = Guid.NewGuid();
        var linkOne = (await Build(adminOne.ExternalSubject).AddFamilyMemberAsync(Add(adminOne, residentOne, familyId, "Hija Compartida"))).Value;
        var linkTwo = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.residentes_familiares (id, centro_id, residente_id, familiar_id, relacion, vinculado_por_cuenta_id, vinculado_en)
                VALUES (@linkTwo, @centerId, @residentId, @familyId, 'Hija', @accountId, SYSUTCDATETIME())
                """, new { linkTwo, centerId = adminOne.CenterId.Value, residentId = residentTwo.Value, familyId, accountId = adminOne.AccountId.Value });
        }

        var version = new FamilyMemberData("Hija Compartida", "Hija", "600 123 456", null).Version;
        var edited = await Build(adminTwo.ExternalSubject).UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
            adminTwo.ProfileScopeId, adminTwo.CenterId, residentTwo, linkTwo, "Hija Compartida", "Hija", "699 000 111", null, version));
        var throughOtherLink = await Build(adminTwo.ExternalSubject).UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
            adminTwo.ProfileScopeId, adminTwo.CenterId, residentOne, linkOne, "Hija Compartida", "Hija", "699 000 222", null, version));
        var otherResident = await Build(adminTwo.ExternalSubject).FindResidentAsync(
            new FindAdministrativeResidentQuery(adminTwo.ProfileScopeId, adminTwo.CenterId, residentOne));

        Assert.True(edited.Ok, edited.Error?.Message);
        Assert.Equal("699 000 111", Assert.Single((await DetailAsync(adminOne, residentOne)).Family).Phone);
        Assert.Equal(ApplicationFailureCode.AccessDenied, throughOtherLink.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, otherResident.Error!.Code);
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
    public async Task Vincular_UnFamiliarDeOtroResidenteDelCentro_ConSuPropiaRelacion_SinAbrirAutorizacion_YElReenvioNoLoDuplica()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var first = await CreateResidentAsync(admin, "Residente Primero");
        var second = await CreateResidentAsync(admin, "Residente Segundo");
        var service = Build(admin.ExternalSubject);
        var familyId = Guid.NewGuid();
        await service.AddFamilyMemberAsync(Add(admin, first, familyId, "Marta Gil", "611 000 111"));
        LinkableFamilyQuery Query(ResidentId resident, string? search) => new(admin.ProfileScopeId, admin.CenterId, resident, search);
        LinkFamilyMemberCommand Link(Guid operationId, string? relation = "Sobrina") =>
            new(admin.ProfileScopeId, admin.CenterId, second, operationId, familyId, relation);

        var beforeFirst = (await service.ListLinkableFamilyAsync(Query(first, "Marta"))).Value!;
        var beforeSecond = (await service.ListLinkableFamilyAsync(Query(second, "Marta"))).Value!;
        var operation = Guid.NewGuid();
        var linked = await service.LinkFamilyMemberAsync(Link(operation));
        var resent = await service.LinkFamilyMemberAsync(Link(operation));
        var again = await service.LinkFamilyMemberAsync(Link(Guid.NewGuid()));
        var reused = await service.LinkFamilyMemberAsync(Link(operation, "Prima"));
        var blank = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(admin.ProfileScopeId, admin.CenterId, second, Guid.NewGuid(), familyId, "  "));
        var afterSecond = (await service.ListLinkableFamilyAsync(Query(second, "Marta"))).Value!;
        var firstDetail = await DetailAsync(admin, first);
        var secondDetail = await DetailAsync(admin, second);

        Assert.Empty(beforeFirst);
        Assert.Equal([new LinkableFamilyMember(familyId, "Marta Gil", "611 000 111", 1)], beforeSecond);
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

    /// <summary>CJ, 1.1: la hija con dos padres en unidades distintas. Quien gestiona solo una unidad busca a la hija, que ya figura como
    /// familiar de un residente de otra unidad del mismo centro, y la vincula sin darla de alta otra vez; no ve a qué residente está ligada.</summary>
    [Fact]
    public async Task Vincular_BuscaEnTodoElCentro_AunqueElFamiliarSoloEsteLigadoAUnResidenteDeOtraUnidad()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var other = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, otherUnit);
        var mine = await CreateResidentAsync(admin, "Lucía Residente Unidad Dos");
        var theirs = await CreateResidentAsync(other, "Pedro Residente Unidad Uno", otherUnit);
        var carmen = Guid.NewGuid();
        var withoutLinks = Guid.NewGuid();
        await Build(other.ExternalSubject).AddFamilyMemberAsync(Add(other, theirs, carmen, "Carmen Ñúñez Peña", "+34 611-222.333"));
        var detached = (await Build(other.ExternalSubject).AddFamilyMemberAsync(Add(other, theirs, withoutLinks, "Carmelo Sin Vinculos", "699 000 000"))).Value;
        Assert.True((await Build(other.ExternalSubject).UnlinkFamilyMemberAsync(new UnlinkFamilyMemberCommand(
            other.ProfileScopeId, other.CenterId, theirs, detached, "Se ha ido a vivir fuera"))).Ok);
        var service = Build(admin.ExternalSubject);
        async Task<IReadOnlyList<LinkableFamilyMember>> Search(string? text) =>
            (await service.ListLinkableFamilyAsync(new LinkableFamilyQuery(admin.ProfileScopeId, admin.CenterId, mine, text))).Value!;

        var byName = await Search("carmen");
        var byAccents = await Search("NUNEZ pena");
        var byPhone = await Search("611 222");
        var bySymbols = await Search("(34) 611-222");
        var shared = await Search("Carm");
        var tooShort = await Search("Ca");
        var blank = await Search("   ");
        var none = await Search(null);
        var noMatch = await Search("Zacarías");
        var wildcards = await Search("%%%");
        var linked = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(admin.ProfileScopeId, admin.CenterId, mine, Guid.NewGuid(), carmen, "Hija"));

        Assert.Equal([new LinkableFamilyMember(carmen, "Carmen Ñúñez Peña", "+34 611-222.333", 1)], byName);
        Assert.Equal(byName, byAccents);
        Assert.Equal(byName, byPhone);
        Assert.Equal(byName, bySymbols);
        // Quien ya no tiene ningún vínculo vigente también se ofrece, con 0 vínculos, y el orden es por nombre.
        Assert.Equal(["Carmelo Sin Vinculos", "Carmen Ñúñez Peña"], shared.Select(f => f.DisplayName));
        Assert.Equal(0, shared[0].ActiveLinks);
        Assert.All(new[] { tooShort, blank, none, noMatch, wildcards }, Assert.Empty);
        Assert.True(linked.Ok, linked.Error?.Message);
        Assert.Equal("Hija", Assert.Single((await DetailAsync(admin, mine)).Family).Relationship);
        Assert.Equal(["Carmelo Sin Vinculos"], (await Search("Carm")).Select(f => f.DisplayName));
    }

    [Fact]
    public async Task Vincular_NoOfreceNiAdmiteFamiliaresDeOtroCentro_NiAOtroPerfil_YLaBusquedaTieneTope()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var foreignCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var mine = await CreateResidentAsync(admin, "Residente Propio");
        var foreign = await CreateResidentAsync(foreignCenter, "Residente De Otro Centro");
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var foreignFamily = Guid.NewGuid();
        await Build(foreignCenter.ExternalSubject).AddFamilyMemberAsync(Add(foreignCenter, foreign, foreignFamily, "Zulema Ajena Del Centro", "655 555 555"));
        var service = Build(admin.ExternalSubject);
        for (var i = 0; i < LinkableFamilySearch.MaxResults + 5; i++)
        {
            Assert.True((await service.AddFamilyMemberAsync(Add(admin, mine, Guid.NewGuid(), $"Zulema Propia {i:00}", $"600 000 {i:000}"))).Ok);
        }
        var other = await CreateResidentAsync(admin, "Residente Sin Familiares");
        LinkFamilyMemberCommand Link(Guid familyId) => new(admin.ProfileScopeId, admin.CenterId, mine, Guid.NewGuid(), familyId, "Amigo");

        var listedForOther = (await service.ListLinkableFamilyAsync(new LinkableFamilyQuery(admin.ProfileScopeId, admin.CenterId, other, "Zulema"))).Value!;
        var foreignByPhone = (await service.ListLinkableFamilyAsync(new LinkableFamilyQuery(admin.ProfileScopeId, admin.CenterId, other, "655 555"))).Value!;
        var ofForeignCenter = await service.LinkFamilyMemberAsync(Link(foreignFamily));
        var unknown = await service.LinkFamilyMemberAsync(Link(Guid.NewGuid()));
        var byNurse = await Build(nurse.ExternalSubject).LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            nurse.ProfileScopeId, nurse.CenterId, mine, Guid.NewGuid(), Guid.NewGuid(), "Amigo"));
        var nurseList = await Build(nurse.ExternalSubject).ListLinkableFamilyAsync(new LinkableFamilyQuery(nurse.ProfileScopeId, nurse.CenterId, mine, "Zulema"));

        Assert.Equal(LinkableFamilySearch.MaxResults, listedForOther.Count);
        Assert.DoesNotContain(listedForOther, f => f.DisplayName.Contains("Ajena"));
        Assert.Empty(foreignByPhone);
        Assert.All(new[] { ofForeignCenter, unknown, byNurse }, r => Assert.Equal(ApplicationFailureCode.AccessDenied, r.Error!.Code));
        Assert.Equal(ApplicationFailureCode.AccessDenied, nurseList.Error!.Code);
        Assert.Equal(LinkableFamilySearch.MaxResults + 5, (await DetailAsync(admin, mine)).Family.Count);
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
