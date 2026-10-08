using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Un comunicado con la hora en que se publica solo y si ya está publicado.</summary>
public sealed record FamilyCommunicationStatusView(AdministrationFamilyCommunication Communication, DateTimeOffset ScheduledAt, bool Published);

public sealed record PublishFamilyCommunicationCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid ComunicadoId);

/// <summary>
/// Comunicados a la familia en Administración (CJ, 2026-10-07; script 0048): los de las unidades de su ámbito de los últimos
/// <see cref="WindowDays"/> días con su hora de publicación, y la publicación anticipada, que solo hace Administración. Todavía no hay
/// Portal Familiar: «publicado» significa que ya podrá verlo la familia, no que lo haya visto.
/// </summary>
public sealed class AdministrationFamilyCommunicationApplicationService(
    AdministrationAccessResolver administrationAccess, IFamilyCommunicationDirectory directory, IFamilyCommunicationPublisher publisher)
{
    public const int WindowDays = 30;

    public Task<ApplicationResult<IReadOnlyList<FamilyCommunicationStatusView>>> ListAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<FamilyCommunicationStatusView>>(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            var now = DateTimeOffset.UtcNow;
            var communications = await directory.ListAsync(access, now.AddDays(-WindowDays), ct);
            return communications.Select(communication => new FamilyCommunicationStatusView(
                communication,
                FamilyCommunicationSchedule.ScheduledAt(communication.PreparedAt, TimeZoneInfo.Local),
                FamilyCommunicationSchedule.IsPublished(communication.PreparedAt, communication.PublishedEarlyAt, now, TimeZoneInfo.Local))).ToList();
        });

    public Task<ApplicationResult<bool>> PublishNowAsync(PublishFamilyCommunicationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await publisher.PublishNowAsync(access, command.ComunicadoId, ct);
            return true;
        });
}
