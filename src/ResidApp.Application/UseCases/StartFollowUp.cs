using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record StartFollowUpCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, int Revision,
    DateOnly? FechaPrevista, string? Criterio, string? IndicacionesContinuidad);

/// <summary>
/// ENF-06/ENF-07B "iniciar seguimiento", una de las cuatro salidas de la decisión asistencial: exige fecha
/// prevista o criterio. El equipo responsable es la Enfermería de la unidad del evento. Mismo criterio de
/// ámbito que cerrar el evento.
/// </summary>
public sealed class StartFollowUp(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, INursingAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(StartFollowUpCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var plan = new FollowUpPlan(command.FechaPrevista, command.Criterio);
            var notes = string.IsNullOrWhiteSpace(command.IndicacionesContinuidad) ? null : command.IndicacionesContinuidad.Trim();
            if (notes is { Length: > FollowUpAction.MaxTextLength })
            {
                throw new DomainValidationException("FOLLOW_UP_ACTION_INVALID");
            }

            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }
            if (await directory.FindAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct) is null)
            {
                throw new AccessDeniedException();
            }

            return await repository.StartFollowUpAsync(new StartFollowUpInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, plan, notes), ct);
        });
}
