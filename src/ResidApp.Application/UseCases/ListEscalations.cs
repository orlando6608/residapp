using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListEscalationsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>MED-02: bandeja de escalados que Enfermería envía a Medicina, dentro del ámbito de Medicina
/// (unidades concedidas y residentes visibles). Sin resumen diagnóstico automático.</summary>
public sealed class ListEscalations(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<EscalationSummary>>> ExecuteAsync(
        ListEscalationsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Medicina)
            {
                throw new AccessDeniedException();
            }

            return await directory.ListEscalationsAsync(command.AmbitoPerfilId, command.CentroId, ct);
        });
}
