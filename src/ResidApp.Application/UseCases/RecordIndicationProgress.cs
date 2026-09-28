using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Realizada = true/false registra el resultado; null confirma solo la lectura.</summary>
public sealed record RecordIndicationProgressCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid EventoId, Guid IndicacionId, int Revision, bool? Realizada = null,
    string? Incidencia = null);

/// <summary>
/// ENF-10: Enfermería confirma la lectura de una indicación de Medicina o, ya leída, registra si fue realizada
/// o no realizada con incidencia. Son hitos distintos: confirmar la lectura nunca la marca como realizada.
/// Solo sobre indicaciones de eventos visibles en el ámbito de Enfermería (deny-by-default).
/// </summary>
public sealed class RecordIndicationProgress(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalIndicationRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordIndicationProgressCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var outcome = command.Realizada is { } done ? new MedicalIndicationOutcome(done, command.Incidencia) : null;

            var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Enfermeria)
            {
                throw new AccessDeniedException();
            }
            var detail = await directory.FindAsync(command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            if (detail is null || detail.Medical.Indications.All(i => i.Id != command.IndicacionId))
            {
                throw new AccessDeniedException();
            }

            return outcome is null
                ? await repository.AcknowledgeAsync(new AcknowledgeMedicalIndicationInput(
                    scope.AccountId, command.CentroId, command.IndicacionId, command.Revision), ct)
                : await repository.ResolveAsync(new ResolveMedicalIndicationInput(
                    scope.AccountId, command.CentroId, command.IndicacionId, command.Revision, outcome), ct);
        });
}
