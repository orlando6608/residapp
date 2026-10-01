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

    private static DesignateEmergencyContactCommand Designate(SeededProfile seed, ResidentId residentId, Guid? linkId, int expected) =>
        new(seed.ProfileScopeId, seed.CenterId, residentId, linkId, expected);

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
        Assert.Null(detail.CurrentEmergencyContact);
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
    public async Task ContactoUrgente_SeDesignaEntreSusFamiliares_SeCambiaYSeQuita()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Contacto");
        var otherResident = await CreateResidentAsync(admin, "Residente Vecino");
        var service = Build(admin.ExternalSubject);
        var daughter = (await service.AddFamilyMemberAsync(Add(admin, residentId, name: "Hija Contacto"))).Value;
        var son = (await service.AddFamilyMemberAsync(Add(admin, residentId, name: "Hijo Contacto"))).Value;
        var neighbour = (await service.AddFamilyMemberAsync(Add(admin, otherResident, name: "Familiar Ajeno"))).Value;

        var first = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, daughter, 0));
        var same = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, daughter, 1));
        var foreign = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, neighbour, 1));
        var changed = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, son, 1));
        var stale = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, daughter, 1));
        var removed = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, null, 2));
        var removedAgain = await service.DesignateEmergencyContactAsync(Designate(admin, residentId, null, 3));
        var detail = await DetailAsync(admin, residentId);

        Assert.Equal([1, 2, 3], new[] { first, changed, removed }.Select(r => r.Value));
        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, removedAgain.Error!.Code);
        Assert.Equal(new string?[] { "Hija Contacto", "Hijo Contacto", null }, detail.EmergencyContacts.Select(c => c.DisplayName));
        Assert.Null(detail.CurrentEmergencyContact);
        Assert.Equal(3, await CountAuditAsync(residentId, "EMERGENCY_CONTACT_DESIGNATE"));
    }

    [Fact]
    public async Task Editar_CambiaLosDatos_YRechazaSinCambiosOFamiliarAjeno()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Edición");
        var otherResident = await CreateResidentAsync(admin, "Residente Otro");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;
        UpdateFamilyMemberCommand Update(ResidentId resident, string phone, string? email) =>
            new(admin.ProfileScopeId, admin.CenterId, resident, linkId, "Lucía Pérez", "Hija", phone, email);

        var updated = await service.UpdateFamilyMemberAsync(Update(residentId, "+34 611 222 333", "lucia@example.org"));
        var unchanged = await service.UpdateFamilyMemberAsync(Update(residentId, "+34 611 222 333", "lucia@example.org"));
        var badEmail = await service.UpdateFamilyMemberAsync(Update(residentId, "+34 611 222 333", "lucia.example.org"));
        var foreign = await service.UpdateFamilyMemberAsync(Update(otherResident, "699 999 999", null));
        var member = Assert.Single((await DetailAsync(admin, residentId)).Family);

        Assert.True(updated.Ok, updated.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unchanged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, badEmail.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        Assert.Equal(("+34 611 222 333", "lucia@example.org"), (member.Phone, member.Email));
        Assert.Equal(1, await CountAuditAsync(residentId, "FAMILY_MEMBER_UPDATE"));
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
            denied.Add((await service.DesignateEmergencyContactAsync(Designate(other, residentId, linkId, 0))).Error?.Code);
        }

        denied.Add((await Build(otherAdmin.ExternalSubject).AddFamilyMemberAsync(Add(otherAdmin, residentId))).Error?.Code);
        denied.Add((await Build(otherAdmin.ExternalSubject).ChangeFamilyAuthorizationAsync(
            Change(otherAdmin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0))).Error?.Code);

        Assert.All(denied, code => Assert.Equal(ApplicationFailureCode.AccessDenied, code));
        Assert.Null(Assert.Single((await DetailAsync(admin, residentId)).Family).CurrentAuthorization);
        Assert.Null((await DetailAsync(admin, residentId)).CurrentEmergencyContact);
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaTocarElHistorial_NiSaltarseTransiciones()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Historial");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(Add(admin, residentId))).Value;
        Assert.True((await service.ChangeFamilyAuthorizationAsync(Change(admin, residentId, linkId, FamilyAuthorizationChange.Abrir, 0))).Ok);
        Assert.True((await service.DesignateEmergencyContactAsync(Designate(admin, residentId, linkId, 0))).Ok);
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
