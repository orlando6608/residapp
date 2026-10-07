using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>DIR-12: Dirección Clínica descarga el PDF firmado de un informe de derivación de un residente de su ámbito. Necesita la declaración de
/// acceso vigente de ese residente (finalidad y justificación, CJ 2026-10-06): sin ella, acceso denegado y la pantalla pide declarar de nuevo.
/// Cada descarga queda auditada en la misma transacción que la lectura.</summary>
public sealed record DownloadDirectionReferralReportCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid EventoId, Guid DeclaracionId);

public sealed class DownloadDirectionReferralReport(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository declarations,
    IReferralReportRepository reports)
{
    public Task<ApplicationResult<ReferralReportPdf>> ExecuteAsync(
        DownloadDirectionReferralReportCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var declaration = await declarations.FindActiveAccessDeclarationAsync(
                    identity.ExternalSubject, command.DeclaracionId, command.AmbitoPerfilId, command.ResidenteId, ct)
                ?? throw new AccessDeniedException();

            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId),
                new AuthorizationTarget.Read(command.ResidenteId), ClinicalResourceType.ReferralReports, declaration.Purpose, ct);
            return await RequestAuthorizationContextResolver.ExecuteDirectionReferralDownloadAsync(
                       context, reports, command.EventoId, declaration.Id, ct)
                   ?? throw new AccessDeniedException();
        });
}
