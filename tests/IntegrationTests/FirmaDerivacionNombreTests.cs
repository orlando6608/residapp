using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
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

    private static async Task<ReferralReportSignature> SignAsync(SeededProfile seed, Guid eventId)
    {
        var service = BuildService(seed.ExternalSubject);
        var revision = (await service.ActivateUrgentProtocolAsync(ActivateCommand(seed, eventId, await StartAndSaveAsync(seed, eventId)))).Value;
        var renderer = new CapturingRenderer();
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var sign = new SignReferralReport(
            scopes, new SqlChangeInboxDirectory(TestDatabase.ConnectionFactory), new FixedSignerSessionIdentityProvider(seed.ExternalSubject),
            new SqlNursingAssessmentRepository(TestDatabase.ConnectionFactory), renderer);

        var signed = await sign.ExecuteAsync(SignCommand(seed, eventId, revision, Guid.NewGuid()));

        Assert.True(signed.Ok, signed.Error?.Message);
        Assert.NotEmpty((await ReadReportAsync(eventId)).Pdf);
        return renderer.Signature!;
    }

    /// <summary>Guarda la firma y genera el PDF de verdad, para que también se compruebe que el nombre se puede pintar.</summary>
    private sealed class CapturingRenderer : IReferralReportPdfRenderer
    {
        public ReferralReportSignature? Signature { get; private set; }

        public byte[] Render(string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature)
        {
            Signature = signature;
            return new ReferralReportPdfRenderer().Render(residentDisplayName, content, signature);
        }
    }
}

file sealed class FixedSignerSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
