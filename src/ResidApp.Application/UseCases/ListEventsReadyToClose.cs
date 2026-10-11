using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ListEventsReadyToCloseCommand(Guid AmbitoPerfilId, CenterId CentroId);

/// <summary>Un episodio con indicaciones de Medicina ya cumplidas, a la espera de que alguien lo cierre. WithIncident son las indicaciones
/// que quedaron «no realizadas» (con su incidencia).</summary>
public sealed record ReadyToCloseEvent(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, string? UnitName, int Indications, int WithIncident,
    DateTimeOffset FulfilledAt);

/// <summary>
/// CJ, 4.2: Medicina no cierra un episodio con indicaciones sin cumplir; cumplidas todas (realizadas o no realizadas con incidencia), el
/// episodio lo cierra Enfermería. Esta lista le dice a la Enfermería del ámbito cuáles son, del cumplimiento más antiguo al más reciente.
/// </summary>
public sealed class ListEventsReadyToClose(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<ReadyToCloseEvent>>> ExecuteAsync(
        ListEventsReadyToCloseCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<ReadyToCloseEvent>>(async () =>
        {
            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }

            return (await directory.ListIndicationsAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .Where(i => i.EventStatus == ClinicalEventStatus.ConIndicacionPendiente)
                .GroupBy(i => i.EventId)
                .Where(g => g.All(i => i.Indication.Status is MedicalIndicationStatus.Realizada or MedicalIndicationStatus.NoRealizada))
                .Select(g =>
                {
                    var first = g.First();
                    return new ReadyToCloseEvent(
                        g.Key, first.ResidentId, first.ResidentDisplayName, first.UnitName, g.Count(),
                        g.Count(i => i.Indication.Status == MedicalIndicationStatus.NoRealizada),
                        g.Max(i => i.Indication.ResolvedAt ?? i.Indication.IssuedAt));
                })
                .OrderBy(e => e.FulfilledAt)
                .ToList();
        });
}
