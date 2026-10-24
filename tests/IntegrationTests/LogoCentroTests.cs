using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Audit;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Nombre y logo propios del centro (script 0068): los cambia la Administración del propio centro, con su auditoría y sin la imagen;
/// ningún otro perfil, ni la Administración de otro centro, ni Plataforma sobre un centro ya creado; el logo solo se lee desde un ámbito de
/// la cuenta de la sesión, nunca desde otro centro ni desde el de Plataforma. Plataforma puede subir el logo al dar de alta el centro.</summary>
public class LogoCentroTests
{
    private static readonly byte[] Png = ReferralReportPdfRendererTests.TinyPng;

    private static AdministracionCentroApplicationService Service(string subject) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedBrandingSessionIdentityProvider(subject)),
        new SqlCenterBrandingDirectory(TestDatabase.ConnectionFactory), new SqlCenterBrandingRepository(TestDatabase.ConnectionFactory));

    private static SqlCenterBrandingDirectory Directory => new(TestDatabase.ConnectionFactory);

    private static async Task<(string Subject, Guid ProfileScopeId)> CreatePlatformOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var subject = $"test-plat-{Guid.NewGuid():N}"[..28];
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de pruebas', 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', @now, @accountId);
            """, new { accountId, subject, now, profileScopeId, centerId = PlatformCenter.Id });
        return (subject, profileScopeId);
    }

    private static async Task<List<string>> AuditAsync(CenterId centerId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<string>("""
            SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE centro_id = @CenterId AND accion_codigo IN ('CENTER_RENAME', 'CENTER_LOGO_SET', 'CENTER_LOGO_REMOVE') ORDER BY ocurrido_en
            """, new { CenterId = centerId.Value })).ToList();
    }

    [Fact]
    public async Task Administracion_RenombraElCentro_PoneCambiaYQuitaElLogo_ConAuditoriaSinLaImagen()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Service(admin.ExternalSubject);
        var query = new AdministracionQuery(admin.ProfileScopeId, admin.CenterId);

        var before = await Directory.FindForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId);
        Assert.Null(before!.LogoUpdatedAt);
        Assert.Null(await Directory.ReadLogoForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId));

        Assert.True((await service.RenameAsync(new RenameCenterCommand(admin.ProfileScopeId, admin.CenterId, "  Residencia Los Almendros "))).Ok);
        Assert.True((await service.SetLogoAsync(new SetCenterLogoCommand(admin.ProfileScopeId, admin.CenterId, Png))).Ok);
        Assert.True((await service.SetLogoAsync(new SetCenterLogoCommand(admin.ProfileScopeId, admin.CenterId, Png))).Ok);

        var after = await Directory.FindForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId);
        Assert.Equal("Residencia Los Almendros", after!.Name);
        Assert.NotNull(after.LogoUpdatedAt);
        var logo = await Directory.ReadLogoForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId);
        Assert.Equal(CenterLogo.Png, logo!.Type);
        Assert.Equal(Png, logo.Content);
        Assert.Equal("Residencia Los Almendros", (await service.GetAsync(query)).Value!.Name);
        Assert.Equal(logo.Content, (await Directory.ReadLogoAsync(admin.CenterId))!.Content);

        var removed = await service.RemoveLogoAsync(query);
        var removedAgain = await service.RemoveLogoAsync(query);
        Assert.True(removed.Value);
        Assert.False(removedAgain.Value);
        Assert.Null((await Directory.FindForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId))!.LogoUpdatedAt);

        Assert.Equal(["CENTER_RENAME", "CENTER_LOGO_SET", "CENTER_LOGO_SET", "CENTER_LOGO_REMOVE"], await AuditAsync(admin.CenterId));
        // Las tres acciones salen en la pantalla de auditoría de Administración.
        foreach (var action in new[] { "CENTER_RENAME", "CENTER_LOGO_SET", "CENTER_LOGO_REMOVE" })
        {
            Assert.Contains((action, AuditCategory.Estructura), AdministrativeAudit.Actions);
        }
    }

    [Fact]
    public async Task Renombrar_AlMismoNombre_OConUnoVacio_EsEntradaInvalida_YElLogoInvalidoNoSeGuarda()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Service(admin.ExternalSubject);
        var current = (await service.GetAsync(new AdministracionQuery(admin.ProfileScopeId, admin.CenterId))).Value!.Name;

        var same = await service.RenameAsync(new RenameCenterCommand(admin.ProfileScopeId, admin.CenterId, current));
        var empty = await service.RenameAsync(new RenameCenterCommand(admin.ProfileScopeId, admin.CenterId, "   "));
        var svg = await service.SetLogoAsync(new SetCenterLogoCommand(admin.ProfileScopeId, admin.CenterId, "<svg/>"u8.ToArray()));

        Assert.Equal(ApplicationFailureCode.InvalidInput, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, empty.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, svg.Error!.Code);
        Assert.Empty(await AuditAsync(admin.CenterId));
        Assert.Null(await Directory.ReadLogoAsync(admin.CenterId));
    }

    [Fact]
    public async Task OtrosPerfiles_YLaAdministracionDeOtroCentro_NoCambianElCentro_NiLeenSuLogo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await Service(admin.ExternalSubject).SetLogoAsync(new SetCenterLogoCommand(admin.ProfileScopeId, admin.CenterId, Png))).Ok);

        var byNurse = await Service(nurse.ExternalSubject).RenameAsync(new RenameCenterCommand(nurse.ProfileScopeId, nurse.CenterId, "Intruso"));
        var byNurseLogo = await Service(nurse.ExternalSubject).RemoveLogoAsync(new AdministracionQuery(nurse.ProfileScopeId, nurse.CenterId));
        var byOtherAdmin = await Service(otherAdmin.ExternalSubject).RenameAsync(new RenameCenterCommand(admin.ProfileScopeId, admin.CenterId, "Intruso"));
        var byOtherAdminWithOwnScope = await Service(otherAdmin.ExternalSubject).RemoveLogoAsync(new AdministracionQuery(admin.ProfileScopeId, otherAdmin.CenterId));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurseLogo.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOtherAdmin.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOtherAdminWithOwnScope.Error!.Code);
        Assert.NotNull(await Directory.ReadLogoAsync(admin.CenterId));
        Assert.Equal(["CENTER_LOGO_SET"], await AuditAsync(admin.CenterId));

        // Cualquier perfil del centro ve el logo, pero solo con un ámbito de su propia cuenta.
        Assert.NotNull(await Directory.ReadLogoForScopeAsync(nurse.ExternalSubject, nurse.ProfileScopeId));
        Assert.NotNull((await Directory.FindForScopeAsync(nurse.ExternalSubject, nurse.ProfileScopeId))!.LogoUpdatedAt);
        Assert.Null(await Directory.ReadLogoForScopeAsync(otherAdmin.ExternalSubject, admin.ProfileScopeId));
        Assert.Null(await Directory.FindForScopeAsync(otherAdmin.ExternalSubject, admin.ProfileScopeId));
        Assert.Null(await Directory.ReadLogoForScopeAsync(otherAdmin.ExternalSubject, otherAdmin.ProfileScopeId));
        Assert.Null((await Directory.FindForScopeAsync(otherAdmin.ExternalSubject, otherAdmin.ProfileScopeId))!.LogoUpdatedAt);
    }

    [Fact]
    public async Task ElAmbitoDePlataforma_NoTieneMarcaPropia_YUnaCuentaSuspendidaTampoco()
    {
        var (subject, scopeId) = await CreatePlatformOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        Assert.True((await Service(admin.ExternalSubject).SetLogoAsync(new SetCenterLogoCommand(admin.ProfileScopeId, admin.CenterId, Png))).Ok);

        Assert.Null(await Directory.FindForScopeAsync(subject, scopeId));
        Assert.Null(await Directory.ReadLogoForScopeAsync(subject, scopeId));

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE sujeto_externo = @Subject", new { Subject = admin.ExternalSubject });
        }
        Assert.Null(await Directory.FindForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId));
        Assert.Null(await Directory.ReadLogoForScopeAsync(admin.ExternalSubject, admin.ProfileScopeId));
    }

    [Fact]
    public async Task Plataforma_AlDarDeAltaUnCentro_PuedeSubirSuLogo_YUnLogoInvalidoNoDejaElCentroCreado()
    {
        var (subject, scopeId) = await CreatePlatformOperatorAsync();
        var platform = new PlatformApplicationService(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedBrandingSessionIdentityProvider(subject),
            new SqlPlatformCenterDirectory(TestDatabase.ConnectionFactory), new SqlPlatformCenterRepository(TestDatabase.ConnectionFactory),
            new SqlAccessInvitationRepository(TestDatabase.ConnectionFactory), TimeProvider.System);
        string Code() => $"cen-{Guid.NewGuid():N}"[..20];
        string Admin() => $"test-adm-{Guid.NewGuid():N}"[..28];
        var withLogoId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();

        var created = await platform.CreateCenterAsync(new CreateCenterCommand(
            scopeId, CenterId.From(PlatformCenter.Id), withLogoId, Code(), "Residencia Con Logo (ficticia)", Code(), "Primera unidad", Admin(), "Admin Nuevo", Png));
        var invalid = await platform.CreateCenterAsync(new CreateCenterCommand(
            scopeId, CenterId.From(PlatformCenter.Id), invalidId, Code(), "Residencia Logo Roto (ficticia)", Code(), "Primera unidad", Admin(), "Admin Nuevo",
            "<svg/>"u8.ToArray()));

        Assert.True(created.Ok, created.Error?.Message);
        var logo = await Directory.ReadLogoAsync(CenterId.From(withLogoId));
        Assert.Equal(Png, logo!.Content);
        Assert.Equal("Residencia Con Logo (ficticia)", (await Directory.FindAsync(CenterId.From(withLogoId)))!.Name);
        Assert.NotNull((await Directory.FindAsync(CenterId.From(withLogoId)))!.LogoUpdatedAt);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var audit = (await connection.QueryAsync<(string Action, string Profile)>("""
            SELECT accion_codigo, perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE centro_id = @CenterId AND accion_codigo IN ('CENTER_CREATE', 'CENTER_LOGO_SET')
            """, new { CenterId = withLogoId })).ToList();
        Assert.Contains(("CENTER_LOGO_SET", "PLATAFORMA"), audit);
        Assert.Contains(("CENTER_CREATE", "PLATAFORMA"), audit);

        Assert.Equal(ApplicationFailureCode.InvalidInput, invalid.Error!.Code);
        Assert.Null(await Directory.FindAsync(CenterId.From(invalidId)));
    }

}
file sealed class FixedBrandingSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
