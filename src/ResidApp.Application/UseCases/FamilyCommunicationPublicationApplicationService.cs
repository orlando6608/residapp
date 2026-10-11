using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Un comunicado con la hora en que se publica solo y si ya está publicado.</summary>
public sealed record FamilyCommunicationStatusView(AdministrationFamilyCommunication Communication, DateTimeOffset ScheduledAt, bool Published);

public sealed record PublishFamilyCommunicationCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid ComunicadoId);

/// <summary>
/// Comunicados a la familia en Administración y en Dirección Clínica (CJ, 2026-10-07 y 2026-10-10; script 0048): los de las unidades de su ámbito
/// de los últimos <see cref="WindowDays"/> días con su hora de publicación, y la publicación anticipada, que hacen Administración y Dirección
/// Clínica. Todavía no hay Portal Familiar: «publicado» significa que ya podrá verlo la familia, no que lo haya visto.
/// </summary>
public sealed class FamilyCommunicationPublicationApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyCommunicationDirectory directory,
    IFamilyCommunicationPublisher publisher)
{
    public const int WindowDays = 30;

    public Task<ApplicationResult<IReadOnlyList<FamilyCommunicationStatusView>>> ListAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<FamilyCommunicationStatusView>>(async () =>
        {
            var access = await ResolveAsync(profileScopeId, centerId, ct);
            var now = DateTimeOffset.UtcNow;
            var communications = await directory.ListAsync(access, now.AddDays(-WindowDays), ct);
            return communications.Select(communication => new FamilyCommunicationStatusView(
                communication, communication.ScheduledAt,
                FamilyCommunicationSchedule.IsPublished(communication.ScheduledAt, communication.PublishedEarlyAt, now))).ToList();
        });

    public Task<ApplicationResult<bool>> PublishNowAsync(PublishFamilyCommunicationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await publisher.PublishNowAsync(access, command.ComunicadoId, ct);
            return true;
        });

    /// <summary>El ámbito activo de la cuenta de la sesión, si es de Administración o de Dirección Clínica; si no, acceso denegado.</summary>
    private async Task<FamilyCommunicationAccess> ResolveAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct))
            .FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile is not (SystemProfile.Administracion or SystemProfile.DireccionClinica))
        {
            throw new AccessDeniedException();
        }

        return new FamilyCommunicationAccess(scope.ProfileScopeId, scope.AccountId, scope.CenterId, scope.Profile);
    }
}
