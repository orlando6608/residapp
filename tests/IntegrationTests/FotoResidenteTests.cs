using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Foto de identificación del residente (estudio de mercado R2, script 0059): la ponen, cambian y quitan Administración y Enfermería;
/// la ven además Medicina y la Auxiliar que tiene asignado al residente, nadie fuera del ámbito ni de otro centro, ni Dirección Clínica; cada
/// cambio queda en la auditoría y la foto se guarda sin EXIF. Cada prueba crea su propio centro.</summary>
public class FotoResidenteTests
{
    private static byte[] Segment(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    private static readonly byte[] Image =
        [.. Segment(0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]), .. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x12, 0x34, 0xFF, 0xD9];

    /// <summary>Un JPEG con un bloque EXIF que dice dónde se hizo la foto.</summary>
    private static readonly byte[] PhotoWithExif = [0xFF, 0xD8, .. Segment(0xE1, [.. "Exif\0\0"u8, .. "GPS 40.4168 N"u8]), .. Image];

    private static readonly byte[] PhotoWithoutExif = [0xFF, 0xD8, .. Image];

    private static ResidentPhotoApplicationService Service(SeededProfile profile) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedPhotoSessionIdentityProvider(profile.ExternalSubject),
        new SqlResidentPhotoRepository(TestDatabase.ConnectionFactory));

    private static Task<ApplicationResult<bool>> SetAsync(SeededProfile profile, ResidentId residentId, byte[] photo, bool consent = true) =>
        Service(profile).SetAsync(new SetResidentPhotoCommand(profile.ProfileScopeId, profile.CenterId, residentId, photo, consent));

    private static Task<ApplicationResult<byte[]?>> ReadAsync(SeededProfile profile, ResidentId residentId) =>
        Service(profile).ReadAsync(profile.ProfileScopeId, profile.CenterId, residentId);

    [Fact]
    public async Task Administracion_PoneLaFoto_SinExif_YLaVenEnfermeriaMedicinaYLaAuxiliarAsignada_YSaleEnLasListas()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Con Foto");
        await AssignAsync(auxiliar, residentId, admin.AccountId);

        var set = await SetAsync(admin, residentId, PhotoWithExif);

        Assert.True(set.Ok);
        foreach (var viewer in new[] { admin, nurse, doctor, auxiliar })
        {
            var read = await ReadAsync(viewer, residentId);
            Assert.True(read.Ok);
            Assert.Equal(PhotoWithoutExif, read.Value);
        }
        var adminList = await new SqlAdministracionResidentDirectory(TestDatabase.ConnectionFactory).ListAsync(admin.ProfileScopeId, admin.CenterId);
        var nurseList = await new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory).ListAsync(nurse.ProfileScopeId, nurse.CenterId);
        var auxiliarList = await new SqlAssignedResidentDirectory(TestDatabase.ConnectionFactory).ListAsync(auxiliar.ProfileScopeId, auxiliar.CenterId);
        Assert.NotNull(Assert.Single(adminList, r => r.ResidentId == residentId).PhotoUpdatedAt);
        Assert.NotNull(Assert.Single(nurseList, r => r.ResidentId == residentId).PhotoUpdatedAt);
        Assert.NotNull(Assert.Single(auxiliarList, r => r.ResidentId == residentId).PhotoUpdatedAt);
    }

    [Fact]
    public async Task Enfermeria_CambiaYQuitaLaFoto_YCadaCambioQuedaEnLaAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Foto Cambiada");
        byte[] other = [0xFF, 0xD8, .. Segment(0xDA, [1, 1, 0, 0, 63, 0]), 0x77, 0xFF, 0xD9];

        Assert.True((await SetAsync(admin, residentId, PhotoWithoutExif)).Ok);
        Assert.True((await SetAsync(nurse, residentId, other)).Ok);
        Assert.Equal(other, (await ReadAsync(admin, residentId)).Value);

        var removed = await Service(nurse).RemoveAsync(nurse.ProfileScopeId, nurse.CenterId, residentId);
        var removedAgain = await Service(nurse).RemoveAsync(nurse.ProfileScopeId, nurse.CenterId, residentId);

        Assert.True(removed.Value);
        Assert.False(removedAgain.Value);
        Assert.Null((await ReadAsync(admin, residentId)).Value);
        var list = await new SqlAdministracionResidentDirectory(TestDatabase.ConnectionFactory).ListAsync(admin.ProfileScopeId, admin.CenterId);
        Assert.Null(Assert.Single(list, r => r.ResidentId == residentId).PhotoUpdatedAt);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var audit = (await connection.QueryAsync<(string Action, string Profile, Guid? UnitId)>("""
            SELECT accion_codigo, perfil_activo, unidad_id FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE recurso_id = @Id AND accion_codigo LIKE 'RESIDENT_PHOTO_%' ORDER BY ocurrido_en, accion_codigo DESC
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal(
            [("RESIDENT_PHOTO_SET", "ADMINISTRACION", admin.UnitId.Value), ("RESIDENT_PHOTO_SET", "ENFERMERIA", admin.UnitId.Value),
             ("RESIDENT_PHOTO_REMOVE", "ENFERMERIA", admin.UnitId.Value)],
            audit.Select(a => (a.Action, a.Profile, a.UnitId!.Value)));
    }

    [Fact]
    public async Task AuxiliarYMedicina_NoPuedenPonerla_YSinConsentimientoOSinJpegNoSeGuarda()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var doctor = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Medicina, admin.CenterId, admin.UnitId);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Sin Foto");
        await AssignAsync(auxiliar, residentId, admin.AccountId);

        var byDoctor = await SetAsync(doctor, residentId, PhotoWithoutExif);
        var byAuxiliar = await SetAsync(auxiliar, residentId, PhotoWithoutExif);
        var removeByDoctor = await Service(doctor).RemoveAsync(doctor.ProfileScopeId, doctor.CenterId, residentId);
        var withoutConsent = await SetAsync(admin, residentId, PhotoWithoutExif, consent: false);
        var png = await SetAsync(admin, residentId, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Assert.Equal(ApplicationFailureCode.AccessDenied, byDoctor.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, removeByDoctor.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutConsent.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, png.Error?.Code);
        Assert.Null((await ReadAsync(admin, residentId)).Value);
    }

    [Fact]
    public async Task NadieFueraDelAmbito_NiDeOtroCentro_NiDireccionClinica_LaVe()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Foto Protegida");
        var otherResident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Otro Residente");
        Assert.True((await SetAsync(admin, residentId, PhotoWithoutExif)).Ok);
        var unassignedAuxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var restrictedNurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        await AssignAsync(restrictedNurse, otherResident, admin.AccountId);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId);
        var otherCenterAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        foreach (var outsider in new[] { unassignedAuxiliar, restrictedNurse, direction, otherCenterAdmin })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(outsider, residentId)).Error?.Code);
        }
        // Con el centro del residente y el ámbito de otro centro tampoco.
        var crossCenter = await Service(otherCenterAdmin).ReadAsync(otherCenterAdmin.ProfileScopeId, admin.CenterId, residentId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, crossCenter.Error?.Code);
    }

    private static async Task AssignAsync(SeededProfile profile, ResidentId residentId, AccountId grantedBy)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
            VALUES (@Id, @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId)
            """, new
        {
            Id = Guid.NewGuid(), profile.ProfileScopeId, CenterId = profile.CenterId.Value, ResidentId = residentId.Value,
            AccountId = grantedBy.Value,
        });
    }
}

file sealed class FixedPhotoSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
