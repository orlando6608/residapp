using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>TransferDeclarationId: la petición vigente del historial tras un traslado (0077), para abrir un cerrado de la unidad anterior (solo lectura).</summary>
public sealed record FindPendingChangeDetailCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, Guid? TransferDeclarationId = null);

/// <summary>ENF-04: detalle de un elemento de bandeja. Null (sin error) si el evento no existe o no está
/// en el ámbito, igual que FindScopeResident — el llamador no distingue "no existe" de "no autorizado".</summary>
public sealed class FindPendingChangeDetail(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<PendingChangeDetail?>> ExecuteAsync(
        FindPendingChangeDetailCommand command, CancellationToken ct = default) =>
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

            return await directory.FindAsync(
                command.AmbitoPerfilId, command.CentroId, command.EventoId, ct, command.TransferDeclarationId);
        });
}
