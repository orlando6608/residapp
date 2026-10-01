using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>El ámbito activo con el que se opera la plataforma (el de Plataforma, en el centro reservado).</summary>
public sealed record PlatformQuery(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>Alta de un centro. OperacionId nace con el formulario y es el id del centro, así que un reenvío no lo duplica.</summary>
public sealed record CreateCenterCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid OperacionId, string? CodigoCentro, string? NombreCentro, string? CodigoUnidad,
    string? NombreUnidad, string? IdentificadorAdministrador, string? NombreAdministrador);

/// <summary>
/// Fachada del perfil de plataforma (script 0026): lista de centros y alta de un centro con su primera unidad y su primer
/// administrador. Exige un ámbito activo de la cuenta de la sesión con perfil Plataforma en el centro reservado; cualquier otro
/// perfil o centro es acceso denegado. No entrega ningún dato de residentes ni clínico.
/// </summary>
public sealed class PlatformApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IPlatformCenterDirectory directory,
    IPlatformCenterRepository centers)
{
    public Task<ApplicationResult<IReadOnlyList<PlatformCenterSummary>>> ListCentersAsync(PlatformQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await directory.ListAsync(await AccessAsync(query.AmbitoPerfilId, query.CentroId, ct), ct));

    public Task<ApplicationResult<CenterId>> CreateCenterAsync(CreateCenterCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await AccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            var data = NewCenter.Validate(
                command.CodigoCentro, command.NombreCentro, command.CodigoUnidad, command.NombreUnidad,
                command.IdentificadorAdministrador, command.NombreAdministrador);
            return await centers.CreateCenterAsync(access, command.OperacionId, data, ct);
        });

    private async Task<PlatformAccess> AccessAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != SystemProfile.Plataforma || centerId.Value != PlatformCenter.Id)
        {
            throw new AccessDeniedException();
        }

        return new PlatformAccess(scope.ProfileScopeId, scope.AccountId);
    }
}
