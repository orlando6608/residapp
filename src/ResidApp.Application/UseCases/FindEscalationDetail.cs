using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record FindEscalationDetailCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId);

/// <summary>MED-03: detalle de un escalado recibido, con sus fuentes de solo lectura (observación original,
/// valoración de Enfermería, constantes, actuaciones, seguimiento y motivo). Con un ámbito de Medicina el
/// directorio solo devuelve eventos escalados; null (sin error) si no existe o no es visible, igual que
/// FindPendingChangeDetail.</summary>
public sealed class FindEscalationDetail(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<PendingChangeDetail?>> ExecuteAsync(
        FindEscalationDetailCommand command, CancellationToken ct = default) =>
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

            return await directory.FindAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
        });
}
