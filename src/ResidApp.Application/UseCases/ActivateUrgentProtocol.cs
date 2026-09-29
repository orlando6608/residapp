using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Común a Enfermería (ActivateUrgentProtocol) y Medicina (ActivateMedicalUrgentProtocol).</summary>
public sealed record ActivateUrgentProtocolCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision, string? Nota);

/// <summary>
/// ENF-06/ENF-11 "activar protocolo urgente", la cuarta salida de la decisión asistencial: desde la
/// valoración guardada o al resolver un seguimiento. Un solo paso con nota opcional, para no retrasar la
/// atención. Mismo criterio de ámbito que escalar.
/// </summary>
public sealed class ActivateUrgentProtocol(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(ActivateUrgentProtocolCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var activation = new UrgentProtocolActivation(command.Nota);

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

            return await repository.ActivateUrgentProtocolAsync(new ActivateUrgentProtocolInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, activation), ct);
        });
}
