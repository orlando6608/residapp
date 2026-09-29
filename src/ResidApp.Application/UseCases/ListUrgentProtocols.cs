using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Común a Enfermería (ListUrgentProtocols) y Medicina (ListMedicalUrgentProtocols).</summary>
public sealed record ListUrgentProtocolsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>ENF-11: bandeja compartida de protocolos urgentes activos de Enfermería en el ámbito.</summary>
public sealed class ListUrgentProtocols(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<UrgentProtocolSummary>>> ExecuteAsync(
        ListUrgentProtocolsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<UrgentProtocolSummary>>(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }

            return (await directory.ListUrgentProtocolsAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .Where(p => p.Status == ClinicalEventStatus.ProtocoloUrgente)
                .ToList();
        });
}
