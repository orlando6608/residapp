using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SaveAppointmentServiceCommand(
    Guid AmbitoPerfilId, CenterId CentroId, int Version, AppointmentServiceMode? Modo, int? AntelacionHoras, int? HorizonteDias,
    IReadOnlyCollection<AppointmentModalitySetting> Modalidades);

public sealed record AddAppointmentWindowCommand(
    Guid AmbitoPerfilId, CenterId CentroId, AppointmentTeam? Equipo, int? DiaSemana, TimeOnly? Inicio, TimeOnly? Fin, int? DuracionMinutos);

public sealed record AddAppointmentBlockCommand(
    Guid AmbitoPerfilId, CenterId CentroId, AppointmentTeam? Equipo, AppointmentModality? Modalidad, DateTimeOffset? Inicio, DateTimeOffset? Fin, string? Motivo);

public sealed record AppointmentTargetCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid Id);

public sealed record CancelAppointmentCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, string? Motivo);

public sealed record RescheduleAppointmentCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, DateTimeOffset? NuevoInicio, string? Motivo);

/// <summary>
/// Citas con reserva directa para Administración del centro (script 0054; historia 7 de Administración, CIT-C01 a CIT-C03): configura el servicio
/// (modo, modalidades por equipo, antelación mínima, horizonte máximo), las franjas y los bloqueos, ve la agenda y reprograma, cancela (con motivo) y
/// finaliza las citas. Solo Administración del centro; la comprobación se repite en cada transacción.
/// </summary>
public sealed class AppointmentServiceApplicationService(AdministrationAccessResolver administrationAccess, IAppointmentServiceRepository repository)
{
    public Task<ApplicationResult<AppointmentServiceView>> ReadAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, (access) => repository.ReadAsync(access, ct), ct);

    /// <summary>Devuelve la versión nueva (la misma si no cambia nada).</summary>
    public Task<ApplicationResult<int>> SaveSettingsAsync(SaveAppointmentServiceCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, access =>
        {
            var settings = AppointmentServiceSettings.Validate(command.Modo, command.AntelacionHoras, command.HorizonteDias);
            return repository.SaveSettingsAsync(access, command.Version, settings, command.Modalidades.Distinct().ToList(), ct);
        }, ct);

    public Task<ApplicationResult<bool>> AddWindowAsync(AddAppointmentWindowCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            var window = AppointmentWindow.Validate(Guid.NewGuid(), command.Equipo, command.DiaSemana, command.Inicio, command.Fin, command.DuracionMinutos);
            await repository.AddWindowAsync(access, window, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<bool>> RetireWindowAsync(AppointmentTargetCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            await repository.RetireWindowAsync(access, command.Id, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<bool>> AddBlockAsync(AddAppointmentBlockCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            var block = AppointmentBlock.Validate(Guid.NewGuid(), command.Equipo, command.Modalidad, command.Inicio, command.Fin, command.Motivo);
            await repository.AddBlockAsync(access, block, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<bool>> RetireBlockAsync(AppointmentTargetCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            await repository.RetireBlockAsync(access, command.Id, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<IReadOnlyList<AdministrationAppointment>>> ListAgendaAsync(
        Guid profileScopeId, CenterId centerId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, access => repository.ListAgendaAsync(access, from, to, ct), ct);

    public Task<ApplicationResult<IReadOnlyList<AdministrationAppointment>>> ListPastUnclosedAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, access => repository.ListPastUnclosedAsync(access, DateTimeOffset.UtcNow, ct), ct);

    public Task<ApplicationResult<IReadOnlyList<AppointmentSlot>>> ListRescheduleSlotsAsync(
        Guid profileScopeId, CenterId centerId, Guid appointmentId, CancellationToken ct = default) =>
        Run(profileScopeId, centerId, access => repository.ListRescheduleSlotsAsync(access, appointmentId, DateTimeOffset.UtcNow, ct), ct);

    public Task<ApplicationResult<bool>> CancelAsync(CancelAppointmentCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            var reason = AppointmentReason.Normalize(command.Motivo);
            await repository.CancelAsync(access, command.CitaId, reason, DateTimeOffset.UtcNow, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<bool>> RescheduleAsync(RescheduleAppointmentCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            var reason = AppointmentReason.Normalize(command.Motivo);
            if (command.NuevoInicio is not { } newStart)
            {
                throw new DomainValidationException(AppointmentSlotRequest.InvalidCode);
            }

            await repository.RescheduleAsync(access, command.CitaId, newStart, reason, DateTimeOffset.UtcNow, ct);
            return true;
        }, ct);

    public Task<ApplicationResult<bool>> FinishAsync(AppointmentTargetCommand command, CancellationToken ct = default) =>
        Run(command.AmbitoPerfilId, command.CentroId, async access =>
        {
            await repository.FinishAsync(access, command.Id, DateTimeOffset.UtcNow, ct);
            return true;
        }, ct);

    private Task<ApplicationResult<T>> Run<T>(Guid profileScopeId, CenterId centerId, Func<AccountAdministrationAccess, Task<T>> run, CancellationToken ct) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await run(access);
        });
}
