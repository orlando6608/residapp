using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListPendingChangesCommand(Guid AmbitoPerfilId, CenterId CentroId, DailyChangeClassification Clasificacion);

/// <summary>Traduce ENF-02 (cambios ordinarios) y ENF-03 (bandeja prioritaria): mismo caso de uso
/// parametrizado por Clasificacion, en vez de dos casi idénticos — mismo criterio que RegisterDailyClosure
/// (Auxiliar) usó para Sin cambios/No valorable.</summary>
public sealed class ListPendingChanges(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<PendingChangeSummary>>> ExecuteAsync(
        ListPendingChangesCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
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

            return await directory.ListAsync(command.AmbitoPerfilId, command.CentroId, command.Clasificacion, ct);
        });
}
