using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ProposeAppointmentCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid SolicitudId, DateTimeOffset? Inicio, string? Profesional);

public sealed record CloseAppointmentRequestCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid SolicitudId, string? Motivo);

/// <summary>
/// Solicitudes de cita para Administración del centro (script 0057; historia 8 de Administración, CIT-S02 y CIT-S04): ve la bandeja, propone un hueco
/// libre (y apunta, si quiere, un profesional interno que la familia no ve) y cierra con un motivo interno las solicitudes que no puede atender. Solo
/// Administración del centro; la comprobación se repite en cada transacción. La cita nace cuando la familia acepta, no aquí.
/// </summary>
public sealed class AppointmentRequestsApplicationService(AdministrationAccessResolver administrationAccess, IAppointmentRequestRepository repository)
{
    public Task<ApplicationResult<IReadOnlyList<AdministrationAppointmentRequest>>> ListAsync(
        Guid profileScopeId, CenterId centerId, bool onlyOpen, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, access => repository.ListAsync(access, onlyOpen, DateTimeOffset.UtcNow, ct), ct);

    public Task<ApplicationResult<AdministrationAppointmentRequest>> ReadAsync(
        Guid profileScopeId, CenterId centerId, Guid requestId, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, access => repository.ReadAsync(access, requestId, DateTimeOffset.UtcNow, ct), ct);

    public Task<ApplicationResult<IReadOnlyList<AppointmentSlot>>> ListProposalSlotsAsync(
        Guid profileScopeId, CenterId centerId, Guid requestId, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, access => repository.ListProposalSlotsAsync(access, requestId, DateTimeOffset.UtcNow, ct), ct);

    public Task<ApplicationResult<bool>> ProposeAsync(ProposeAppointmentCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            var professional = AppointmentRequestRules.NormalizeProfessional(command.Profesional);
            if (command.Inicio is not { } startsAt)
            {
                throw new DomainValidationException(AppointmentSlotRequest.InvalidCode);
            }

            await repository.ProposeAsync(access, command.SolicitudId, startsAt, professional, DateTimeOffset.UtcNow, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<bool>> CloseAsync(CloseAppointmentRequestCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            var reason = AppointmentReason.Normalize(command.Motivo);
            await repository.CloseAsync(access, command.SolicitudId, reason, DateTimeOffset.UtcNow, ct);
            return true;
        }, ct);

    private Task<ApplicationResult<T>> Run<T>(Guid profileScopeId, CenterId centerId, Func<AccountAdministrationAccess, Task<T>> run, CancellationToken ct) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await run(access);
        });
}
