using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>Resuelve la identidad de sesión y lista sus ambitos_perfil activos, para la pantalla de
/// selección de ámbito activo (centro + perfil).</summary>
public sealed class ListActiveProfileScopes(IProfileScopeDirectoryProvider directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<ActiveProfileScope>>> ExecuteAsync(CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }
            return await directory.ListActiveAsync(identity.ExternalSubject, ct);
        });
}
