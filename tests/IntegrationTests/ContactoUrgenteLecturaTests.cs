using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>ADM-08/DER-06 (0022): el contacto urgente que designa Administración, en solo lectura en la ficha de
/// Enfermería y de Medicina y en la derivación (decisión del usuario, 2026-10-01; la matriz da lectura a esos dos perfiles y
/// la niega a Auxiliar y Dirección).</summary>
public class ContactoUrgenteLecturaTests
{
    private static FindEmergencyContact BuildReader(string externalSubject)
    {
        var directory = new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory);
        var listScopeResidents = new ListScopeResidents(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), directory, new FixedContactSessionIdentityProvider(externalSubject));
        return new FindEmergencyContact(new FindScopeResident(listScopeResidents), directory);
    }

    private static Task<ApplicationResult<IReadOnlyList<EmergencyContactSummary>>> ReadAsync(SeededProfile seed, ResidentId residentId, SystemProfile profile) =>
        BuildReader(seed.ExternalSubject).ExecuteAsync(new FindScopeResidentCommand(seed.ProfileScopeId, seed.CenterId, residentId, profile));

    /// <summary>Añade un familiar al residente como Administración y lo deja como único contacto urgente (si había otro, lo sustituye).</summary>
    private static async Task<Guid> DesignateAsync(SeededProfile admin, ResidentId residentId, string name, string phone, int expected)
    {
        var service = AdministracionResidentesTests.Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), name, "Hija", phone, "no-se-muestra@example.org"))).Value;
        Assert.True((await service.DesignateEmergencyContactAsync(
            new DesignateEmergencyContactCommand(admin.ProfileScopeId, admin.CenterId, residentId, [linkId], expected))).Ok);
        return linkId;
    }

    [Fact]
    public async Task Ficha_EnfermeriaYMedicinaLoLeen_AuxiliarDireccionYFueraDeAmbitoNo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Contacto Ficha");
        var enfermera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var medica = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var direccion = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        var before = await ReadAsync(enfermera, residentId, SystemProfile.Enfermeria);
        await DesignateAsync(admin, residentId, "Lucía Contacto", "600 111 222", 0);
        var byNurse = await ReadAsync(enfermera, residentId, SystemProfile.Enfermeria);
        var byDoctor = await ReadAsync(medica, residentId, SystemProfile.Medicina);
        var denied = new[]
        {
            await ReadAsync(auxiliar, residentId, SystemProfile.Auxiliar),
            await ReadAsync(direccion, residentId, SystemProfile.DireccionClinica),
            await ReadAsync(outsider, residentId, SystemProfile.Enfermeria),
            // Un ámbito de Enfermería que pide como si fuera de Medicina.
            await ReadAsync(enfermera, residentId, SystemProfile.Medicina),
        };

        Assert.True(before.Ok);
        Assert.Empty(before.Value!);
        Assert.Equal(new EmergencyContactSummary("Lucía Contacto", "Hija", "600 111 222"), Assert.Single(byNurse.Value!));
        Assert.Equal(byNurse.Value, byDoctor.Value);
        Assert.All(denied, r => Assert.Equal(ApplicationFailureCode.AccessDenied, r.Error!.Code));
    }

    [Fact]
    public async Task Ficha_MuestraLosVigentes_VariosJuntos_YSoloLosQueQuedanTrasUnCambio()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Contacto Cambios");
        var enfermera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        await DesignateAsync(admin, residentId, "Primera Contacto", "600 111 222", 0);
        // Sustituir a la primera por la segunda son dos cambios (quitar y añadir): la designación pasa a tener 3.
        var second = await DesignateAsync(admin, residentId, "Segundo Contacto", "611 333 444", 1);
        var service = AdministracionResidentesTests.Build(admin.ExternalSubject);
        var third = (await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Tercero Contacto", "Hijo", "622 555 666", null))).Value;

        var changed = (await ReadAsync(enfermera, residentId, SystemProfile.Enfermeria)).Value!;
        Assert.True((await service.DesignateEmergencyContactAsync(
            new DesignateEmergencyContactCommand(admin.ProfileScopeId, admin.CenterId, residentId, [second, third], 3))).Ok);
        var both = (await ReadAsync(enfermera, residentId, SystemProfile.Enfermeria)).Value!;

        Assert.Equal("Segundo Contacto", Assert.Single(changed).DisplayName);
        Assert.Equal(["Segundo Contacto", "Tercero Contacto"], both.Select(c => c.DisplayName));
    }

    [Fact]
    public async Task Derivacion_LlevaElContactoUrgenteVigente()
    {
        var (enfermera, _, eventId) = await SeedOwnEventAsync();
        var service = BuildService(enfermera.ExternalSubject);
        var revision = (await service.ActivateUrgentProtocolAsync(
            ActivateCommand(enfermera, eventId, await StartAndSaveAsync(enfermera, eventId)))).Value;
        Assert.True((await service.SignReferralReportAsync(SignCommand(enfermera, eventId, revision, Guid.NewGuid()))).Ok);
        var residentId = (await DetailAsync(enfermera, eventId)).ResidentId;
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, enfermera.CenterId, enfermera.UnitId);

        var withoutContact = (await DetailAsync(enfermera, eventId)).Referral!;
        await DesignateAsync(admin, residentId, "Lucía Derivación", "+34 600 111 222", 0);
        var withContact = (await DetailAsync(enfermera, eventId)).Referral!;

        Assert.Empty(withoutContact.EmergencyContacts);
        Assert.Equal(new EmergencyContactSummary("Lucía Derivación", "Hija", "+34 600 111 222"), Assert.Single(withContact.EmergencyContacts));
        // El contacto no entra en el contenido firmado del informe (DER-04): la huella sigue siendo la del contenido de prueba.
        Assert.Equal(ReferralHash(), withContact.ContentHash);
    }
}

file sealed class FixedContactSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
