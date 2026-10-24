using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Pdf;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using static ResidApp.IntegrationTests.EnfermeriaApplicationServiceTests;

namespace ResidApp.IntegrationTests;

/// <summary>DER-05 (0023): la firma del PDF de derivación lleva el nombre visible de la cuenta que firma; sin nombre, solo
/// el perfil y el identificador, como antes.</summary>
public class FirmaDerivacionNombreTests
{
    [Fact]
    public async Task Firma_LlevaElNombreDeLaCuenta_ySinNombreSoloElIdentificador()
    {
        var (named, _, namedEvent) = await SeedOwnEventAsync();
        var (unnamed, _, unnamedEvent) = await SeedOwnEventAsync();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET nombre_visible = N'Ana Ruiz Peña (ficticia)' WHERE id = @Id",
                new { Id = named.AccountId.Value });
        }

        var namedSignature = await SignAsync(named, namedEvent);
        var unnamedSignature = await SignAsync(unnamed, unnamedEvent);

        Assert.Equal(("Ana Ruiz Peña (ficticia)", named.ExternalSubject), (namedSignature.SignerName, namedSignature.SignerSubject));
        Assert.Equal(((string?)null, unnamed.ExternalSubject), (unnamedSignature.SignerName, unnamedSignature.SignerSubject));
    }

    /// <summary>Script 0068: el PDF se genera con el nombre del centro de quien firma y su logo, si lo tiene; con nombre pero sin logo, solo el nombre.</summary>
    [Fact]
    public async Task Firma_LlevaLaMarcaDelCentro_NombreYLogo()
    {
        var (withLogo, _, withLogoEvent) = await SeedOwnEventAsync();
        var (withoutLogo, _, withoutLogoEvent) = await SeedOwnEventAsync();
        var admin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, withLogo.CenterId, withLogo.UnitId);
        var centers = new AdministracionCentroApplicationService(
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedSignerSessionIdentityProvider(admin.ExternalSubject)),
            new SqlCenterBrandingDirectory(TestDatabase.ConnectionFactory), new SqlCenterBrandingRepository(TestDatabase.ConnectionFactory));
        Assert.True((await centers.RenameAsync(new RenameCenterCommand(admin.ProfileScopeId, admin.CenterId, "Residencia Los Almendros"))).Ok);
        Assert.True((await centers.SetLogoAsync(new SetCenterLogoCommand(admin.ProfileScopeId, admin.CenterId, ReferralReportPdfRendererTests.TinyPng))).Ok);

        var branded = (await SignWithRendererAsync(withLogo, withLogoEvent)).Branding;
        var plain = (await SignWithRendererAsync(withoutLogo, withoutLogoEvent)).Branding;

        Assert.Equal("Residencia Los Almendros", branded!.CenterName);
        Assert.Equal(ReferralReportPdfRendererTests.TinyPng, branded.Logo!.Content);
        Assert.NotEqual("Residencia Los Almendros", plain!.CenterName);
        Assert.Null(plain.Logo);
    }


    private static async Task<ReferralReportSignature> SignAsync(SeededProfile seed, Guid eventId) => (await SignWithRendererAsync(seed, eventId)).Signature!;

    private static async Task<CapturingRenderer> SignWithRendererAsync(SeededProfile seed, Guid eventId)
    {
        var service = BuildService(seed.ExternalSubject);
        var revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(seed, eventId, await StartAndSaveAsync(seed, eventId)))).Value;
        var renderer = new CapturingRenderer();
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var sign = new SignReferralReport(
            scopes, new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory), new FixedSignerSessionIdentityProvider(seed.ExternalSubject),
            new SqlNursingAssessmentRepository(TestDatabase.ConnectionFactory), renderer, new SqlCenterBrandingDirectory(TestDatabase.ConnectionFactory));

        var signed = await sign.ExecuteAsync(SignCommand(seed, eventId, revision, Guid.NewGuid()));

        Assert.True(signed.Ok, signed.Error?.Message);
        Assert.NotEmpty((await ReadReportAsync(eventId)).Pdf);
        return renderer;
    }

    /// <summary>Guarda la firma y genera el PDF de verdad, para que también se compruebe que el nombre se puede pintar.</summary>
    private sealed class CapturingRenderer : IReferralReportPdfRenderer
    {
        public ReferralReportSignature? Signature { get; private set; }

        public ReferralReportBranding? Branding { get; private set; }

        public byte[] Render(
            string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature, ReferralReportBranding? branding = null)
        {
            Signature = signature;
            Branding = branding;
            return new ReferralReportPdfRenderer().Render(residentDisplayName, content, signature, branding);
        }
    }
}

file sealed class FixedSignerSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
