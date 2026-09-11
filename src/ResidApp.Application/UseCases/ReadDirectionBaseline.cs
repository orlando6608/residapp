using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traduce los campos de entrada de resident-baseline-service.ts::readDirectionBaseline. Purpose
/// viaja como texto libre (igual que en TS): la política es quien decide si vale "SUPERVISION_CLINICA",
/// no se pre-valida ni se fija aquí, para que ACCESS_PURPOSE_REQUIRED siga siendo una ruta alcanzable.</summary>
public sealed record ReadDirectionBaselineCommand(
    Guid ProfileScopeId, CenterId CenterId, ResidentId ResidentId, string ResourceType, string? Purpose);

/// <summary>Traduce readDirectionBaseline de lib/application/resident-baseline-service.ts.</summary>
public sealed class ReadDirectionBaseline(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<IReadOnlyList<AuditedBaselineHeader>>> ExecuteAsync(
        ReadDirectionBaselineCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (!EnumCode.TryParseCode<ClinicalResourceType>(command.ResourceType, out var resourceType))
            {
                throw new AccessDeniedException();
            }
            var purpose = command.Purpose is not null && EnumCode.TryParseCode<ClinicalDetailAccessPurpose>(command.Purpose, out var parsedPurpose)
                ? parsedPurpose
                : (ClinicalDetailAccessPurpose?)null;

            var selection = new AuthorizationSelection(command.ProfileScopeId, command.CenterId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Read(command.ResidentId), resourceType, purpose, ct);
            return await RequestAuthorizationContextResolver.ExecuteDirectionBaselineReadAsync(context, repository, ct);
        });
}
