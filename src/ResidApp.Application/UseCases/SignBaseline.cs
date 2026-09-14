using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traduce los campos de entrada de resident-baseline-service.ts::signBaseline.</summary>
public sealed record SignBaselineCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, BaselineDraftId BorradorId,
    int RevisionBorradorEsperada, Guid OperacionId);

/// <summary>Traduce signBaseline de lib/application/resident-baseline-service.ts.</summary>
public sealed class SignBaseline(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<SignBaselineDraftResult>> ExecuteAsync(SignBaselineCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection,
                new AuthorizationTarget.Sign(command.ResidenteId, command.BorradorId), ct: ct);
            var payload = new BaselineSignPayload(command.RevisionBorradorEsperada, command.OperacionId);
            return await RequestAuthorizationContextResolver.ExecuteBaselineSignAsync(context, repository, payload, ct);
        });
}
