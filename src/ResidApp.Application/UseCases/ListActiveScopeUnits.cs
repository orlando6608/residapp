using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Resuelve la identidad de sesión y lista las unidades de su ámbito activo, para el selector de unidad del alta
/// de residente. Solo orienta el formulario: el alta vuelve a autorizar la unidad elegida (AuthorizationTarget.Create).</summary>
public sealed class ListActiveScopeUnits(IProfileScopeDirectoryProvider directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<ScopeUnit>>> ExecuteAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            return await directory.ListUnitsAsync(identity.ExternalSubject, profileScopeId, centerId, ct);
        });
}
