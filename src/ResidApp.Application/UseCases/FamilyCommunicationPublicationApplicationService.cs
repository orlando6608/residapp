using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Families;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Un comunicado con la hora en que se publica solo y si ya está publicado. CanAmend: ya está publicado y no está retirado, así que se
/// puede retirar (script 0053). CanCorrect: además, la familia todavía lo ve, así que se puede publicar una correctora (una correctora deja de
/// verse con su original, CJ portal 4.2: sobre uno que ya no se ve, la familia no la vería nunca).</summary>
public sealed record FamilyCommunicationStatusView(
    AdministrationFamilyCommunication Communication, DateTimeOffset ScheduledAt, bool Published, bool CanAmend, bool CanCorrect);


public sealed record PublishFamilyCommunicationCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid ComunicadoId);

public sealed record PublishFamilyCommunicationCorrectiveCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid ComunicadoId, FamilyCommunicationType? Tipo, string? Texto);

public sealed record WithdrawFamilyCommunicationCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid ComunicadoId, string? Motivo);

/// <summary>
/// Comunicados a la familia en Administración y en Dirección Clínica (CJ, 2026-10-07 y 2026-10-10; script 0048): los de las unidades de su ámbito
/// preparados en los últimos <see cref="WindowDays"/> días y los anteriores que la familia todavía ve (un relevante, hasta 6 meses), para poder
/// corregirlos; con su hora de publicación, y la publicación anticipada, que hacen Administración y Dirección Clínica. Todavía no hay Portal Familiar: «publicado» significa que ya podrá verlo la familia, no que lo haya visto.
/// </summary>
public sealed class FamilyCommunicationPublicationApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyCommunicationDirectory directory,
    IFamilyCommunicationPublisher publisher, IFamilyCommunicationAmender amender)
{
    public const int WindowDays = 30;

    public Task<ApplicationResult<IReadOnlyList<FamilyCommunicationStatusView>>> ListAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<FamilyCommunicationStatusView>>(async () =>
        {
            var access = await ResolveAsync(profileScopeId, centerId, ct);
            var now = DateTimeOffset.UtcNow;
            // Los candidatos son los preparados en la ventana más larga de la familia (6 meses) y unos días más, como en el portal.
            var communications = await directory.ListAsync(access, now.AddMonths(-FamilyPortalVisibility.RelevantMonths).AddDays(-3), ct);
            return communications.Select(communication =>
            {
                var published = FamilyCommunicationSchedule.IsPublished(communication.ScheduledAt, communication.PublishedEarlyAt, now);
                var canAmend = published && communication.Withdrawal is null;
                var visibleToFamily = canAmend
                    && FamilyPortalVisibility.IsVisible(communication.Type, communication.ScheduledAt, communication.PublishedEarlyAt, now);
                return new FamilyCommunicationStatusView(communication, communication.ScheduledAt, published, canAmend, visibleToFamily);
            }).Where(view => view.Communication.PreparedAt >= now.AddDays(-WindowDays) || view.CanCorrect).ToList();
        });

    public Task<ApplicationResult<bool>> PublishNowAsync(PublishFamilyCommunicationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var access = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await publisher.PublishNowAsync(access, command.ComunicadoId, ct);
            return true;
        });

    /// <summary>Una publicación correctora vinculada al comunicado ya publicado (historia 3): sale en el acto, con el tipo y el texto que elija quien
    /// la publica (las mismas reglas que al preparar un comunicado) y sin cambiar el original.</summary>
    public Task<ApplicationResult<bool>> PublishCorrectiveAsync(PublishFamilyCommunicationCorrectiveCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var content = new FamilyCommunicationChoice(FamilyCommunicationDecision.Preparar, command.Tipo, command.Texto);
            var access = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await amender.PublishCorrectiveAsync(access, command.ComunicadoId, content.Type!.Value, content.Text!, ct);
            return true;
        });

    /// <summary>La retirada excepcional de un comunicado publicado, con un motivo interno: la familia solo ve que ya no está disponible.</summary>
    public Task<ApplicationResult<bool>> WithdrawAsync(WithdrawFamilyCommunicationCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var reason = FamilyCommunicationWithdrawal.NormalizeReason(command.Motivo);
            var access = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            await amender.WithdrawAsync(access, command.ComunicadoId, reason, ct);
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
