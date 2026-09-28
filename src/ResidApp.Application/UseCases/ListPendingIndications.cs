using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListPendingIndicationsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>ENF-10: indicaciones de Medicina pendientes para la Enfermería del ámbito (compartidas por
/// unidad): las que faltan por leer y las leídas que faltan por realizar. Nunca caducan: siguen aquí hasta
/// registrarse como realizadas o no realizadas.</summary>
public sealed class ListPendingIndications(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<MedicalIndicationListItem>>> ExecuteAsync(
        ListPendingIndicationsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<MedicalIndicationListItem>>(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }

            return (await directory.ListIndicationsAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .Where(i => i.Indication.Status is MedicalIndicationStatus.PendienteLectura or MedicalIndicationStatus.Leida)
                .ToList();
        });
}
