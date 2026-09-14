using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadCurrentBaselineCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>Traduce AUX-03 "Consulta del estado basal": lectura resumida (nueve áreas + Barthel) del
/// basal vigente, nunca versiones históricas, borradores, respuestas detalladas de Barthel, aportaciones,
/// firma ni corrección. Devuelve null cuando el residente todavía no tiene basal vigente (estado "sin
/// basal", no un error).</summary>
public sealed class ReadCurrentBaseline(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<CurrentBaselineSummary?>> ExecuteAsync(
        ReadCurrentBaselineCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Read(command.ResidenteId),
                ClinicalResourceType.BaselineCurrent, ct: ct);
            return await RequestAuthorizationContextResolver.ExecuteBaselineCurrentReadAsync(context, repository, ct);
        });
}
