using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadProcessDeadlinesCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>Un hito con su plazo normal y el de un evento prioritario, en minutos. Null en uno: ese plazo no se mide.</summary>
public sealed record ProcessDeadlineCommandItem(ProcessMilestone Hito, int? PlazoMinutos, int? PlazoPrioritarioMinutos);

public sealed record SaveProcessDeadlinesCommand(
    Guid AmbitoPerfilId, CenterId CentroId, int Version, IReadOnlyList<ProcessDeadlineCommandItem> Plazos);

/// <summary>
/// Pantalla de plazos de los hitos del proceso del centro (CJ, 2026-10-07). Solo Dirección / Coordinación Clínica con el permiso
/// PROCESS_DEADLINES_MANAGE, que comprueba el repositorio en la misma transacción. Los plazos solo sirven para medir y avisar (DIR-11): no
/// cambian ninguna categoría ni umbral clínico.
/// </summary>
public sealed class ProcessDeadlinesApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IProcessDeadlineRepository repository)
{
    public Task<ApplicationResult<ProcessDeadlinesView>> ReadAsync(ReadProcessDeadlinesCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await repository.ReadAsync(await ResolveAccessAsync(command.AmbitoPerfilId, command.CentroId, ct), ct));

    public Task<ApplicationResult<int>> SaveAsync(SaveProcessDeadlinesCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Plazos.Select(p => p.Hito).Distinct().Count() != command.Plazos.Count)
            {
                throw new DomainValidationException("PROCESS_DEADLINES_INVALID");
            }

            var desired = command.Plazos.ToDictionary(p => p.Hito, p => ProcessMilestoneRules.ValidateMinutes(p.PlazoMinutos, p.PlazoPrioritarioMinutos));
            var access = await ResolveAccessAsync(command.AmbitoPerfilId, command.CentroId, ct);
            return await repository.SaveAsync(new SaveProcessDeadlinesInput(access, command.Version, desired), ct);
        });

    private async Task<ProcessDeadlinesAccess> ResolveAccessAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct))
            .FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile is not SystemProfile.DireccionClinica)
        {
            throw new AccessDeniedException();
        }

        return new ProcessDeadlinesAccess(scope.ProfileScopeId, scope.AccountId, scope.Profile, centerId);
    }
}
