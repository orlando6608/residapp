using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListMilestoneWarningsCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>
/// DIR-11 (CJ, 2026-10-07; continuidad-supervision-comunicacion, 4.1 C): el equipo responsable ve un aviso cuando un hito está a punto de
/// pasar de plazo, y también los que ya han pasado y siguen sin hacer. Los ve Enfermería o Medicina, solo los hitos de los eventos de su
/// ámbito que le corresponden a su perfil, los más urgentes primero. Mira los hitos de los últimos 30 días: uno más antiguo y aún sin hacer
/// ya no es un aviso sino un olvido que Dirección ve en la revisión de calidad.
/// </summary>
public sealed class ListMilestoneWarnings(
    IProfileScopeDirectoryProvider scopes, IMilestoneFactDirectory directory, ISessionIdentityProvider session)
{
    public const int WindowDays = 30;

    public Task<ApplicationResult<IReadOnlyList<MilestoneEntry>>> ExecuteAsync(
        ListMilestoneWarningsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
            {
                throw new AccessDeniedException();
            }

            var now = DateTime.UtcNow;
            var facts = await directory.ListMilestoneFactsAsync(command.AmbitoPerfilId, command.CentroId, now.AddDays(-WindowDays), now.AddMinutes(5), ct);
            var report = ProcessQualityRules.Build(
                facts.Where(f => f.Responsible == scope.Profile).ToList(), ProcessMilestoneRules.Defaults, [], DateOnly.FromDateTime(now), DateOnly.FromDateTime(now), now);
            return (IReadOnlyList<MilestoneEntry>)report.Exceptions
                .Where(e => e.End is null)
                .OrderByDescending(e => e.Status == MilestoneStatus.FueraDePlazo)
                .ThenBy(e => e.Start)
                .ToList();
        });
}
