using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record FamilyPortalQuery(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>
/// Portal Familiar (bloque 1, script 0052): los residentes a los que la cuenta del familiar tiene acceso y sus comunicados publicados. Solo
/// con el perfil activo FAMILIAR; el acceso a cada residente lo decide la lectura SQL (vínculo y autorización Activa), no el ámbito, que no
/// lleva unidades.
/// </summary>
public sealed class FamilyPortalApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyPortalReader reader)
{
    public Task<ApplicationResult<IReadOnlyList<FamilyPortalResident>>> ListResidentsAsync(FamilyPortalQuery query, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await reader.ListResidentsAsync(await AccessAsync(query, ct), DateOnly.FromDateTime(DateTime.Today), ct));

    public Task<ApplicationResult<FamilyPortalResidentHome>> ReadResidentAsync(FamilyPortalQuery query, Guid linkId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await reader.ReadResidentAsync(await AccessAsync(query, ct), linkId, DateOnly.FromDateTime(DateTime.Today), DateTimeOffset.UtcNow, ct)
            ?? throw new AccessDeniedException());

    public Task<ApplicationResult<FamilyPortalCommunicationDetail>> ReadCommunicationAsync(
        FamilyPortalQuery query, Guid linkId, Guid communicationId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await reader.ReadCommunicationAsync(
                await AccessAsync(query, ct), linkId, communicationId, DateOnly.FromDateTime(DateTime.Today), DateTimeOffset.UtcNow, ct)
            ?? throw new AccessDeniedException());

    private async Task<FamilyPortalAccess> AccessAsync(FamilyPortalQuery query, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct))
            .FirstOrDefault(s => s.ProfileScopeId == query.AmbitoPerfilId && s.CenterId == query.CentroId);
        return scope is null || scope.Profile != SystemProfile.Familiar
            ? throw new AccessDeniedException()
            : new FamilyPortalAccess(scope.ProfileScopeId, scope.AccountId, scope.CenterId);
    }
}
