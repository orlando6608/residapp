using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Resuelve la identidad de sesión y lista las habitaciones y plazas libres de las unidades de su ámbito activo, para el selector de
/// ubicación del alta de residente (hermano de ListActiveScopeUnits). Solo orienta el formulario: el alta vuelve a validar la ubicación elegida.</summary>
public sealed class ListActiveScopeLocations(ILocationOptionsDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<LocationOption>>> ExecuteAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            return await directory.ListAsync(identity.ExternalSubject, profileScopeId, centerId, ct);
        });
}
