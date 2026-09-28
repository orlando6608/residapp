using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record StartNursingAssessmentCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision);

/// <summary>
/// ENF-03 "empezar valoración" (pantalla ENF-04): registra qué profesional y cuándo empieza, sin crear
/// propiedad permanente sobre el evento. Solo sobre eventos visibles en la bandeja del ámbito (mismo
/// criterio que FindPendingChangeDetail); si el evento cambió desde que se abrió, conflicto y recarga.
/// </summary>
public sealed class StartNursingAssessment(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(StartNursingAssessmentCommand command, CancellationToken ct = default) =>
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
            if (await directory.FindAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct) is null)
            {
                throw new AccessDeniedException();
            }

            return await repository.StartAsync(
                new StartNursingAssessmentInput(scope.AccountId, command.CentroId, command.EventoId, command.Revision), ct);
        });
}
