using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;

namespace ResidApp.Application.UseCases;

/// <summary>MED-05/MED-13 "activar protocolo urgente", una salida de la conducta médica: con la valoración
/// médica guardada, con indicaciones emitidas o al resolver un seguimiento médico. Mismos datos que el de
/// Enfermería (módulo común, DER-01).</summary>
public sealed class ActivateMedicalUrgentProtocol(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(ActivateUrgentProtocolCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var activation = new UrgentProtocolActivation(command.Nota);

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.ActivateUrgentProtocolAsync(new ActivateUrgentProtocolInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, activation), ct);
        });
}

/// <summary>MED-13: registrar una actuación, la evolución o un contacto con un servicio en el protocolo
/// urgente activo de Medicina.</summary>
public sealed class RecordMedicalUrgentProtocolEntry(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory,
    ISessionIdentityProvider session, IMedicalAssessmentRepository repository)
{
    public Task<ApplicationResult<int>> ExecuteAsync(RecordUrgentProtocolEntryCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var entry = command.ToEntry();

            var scope = await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, command.EventoId, ct);
            return await repository.RecordUrgentProtocolEntryAsync(new RecordUrgentProtocolEntryInput(
                scope.AccountId, command.CentroId, command.EventoId, command.Revision, entry), ct);
        });
}

/// <summary>MED-13: bandeja compartida de protocolos urgentes activos de Medicina en el ámbito.</summary>
public sealed class ListMedicalUrgentProtocols(
    IProfileScopeDirectoryProvider scopes, IChangeInboxDirectory directory, ISessionIdentityProvider session)
{
    public Task<ApplicationResult<IReadOnlyList<UrgentProtocolSummary>>> ExecuteAsync(
        ListUrgentProtocolsCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<UrgentProtocolSummary>>(async () =>
        {
            await MedicinaScope.RequireAsync(scopes, session, directory, command.AmbitoPerfilId, command.CentroId, null, ct);
            return (await directory.ListUrgentProtocolsAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .Where(p => p.Status == ClinicalEventStatus.ProtocoloUrgenteMedico)
                .ToList();
        });
}
