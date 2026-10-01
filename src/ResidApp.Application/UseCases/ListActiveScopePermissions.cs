using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Resuelve la identidad de sesión y lista los permisos vigentes de su ámbito activo, para que el Inicio solo
/// enseñe las fichas que la cuenta puede usar. Solo orienta: cada caso de uso vuelve a autorizar en el servidor.</summary>
public sealed class ListActiveScopePermissions(IProfileScopeDirectoryProvider directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<string>>> ExecuteAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            return await directory.ListPermissionsAsync(identity.ExternalSubject, profileScopeId, centerId, ct);
        });
}
