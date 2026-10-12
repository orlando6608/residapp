using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record BookFamilyAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid VinculoId, AppointmentTeam? Equipo, AppointmentModality? Modalidad, DateTimeOffset? Inicio, Guid OperacionId);

public sealed record RescheduleFamilyAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid VinculoId, Guid CitaId, DateTimeOffset? NuevoInicio);

/// <summary>
/// Citas del Portal Familiar con reserva directa (script 0054; historia 4 del portal): la familia ve los huecos libres de un equipo y una modalidad,
/// reserva, reprograma y cancela sus citas. Solo con el perfil activo FAMILIAR; el acceso a cada residente lo decide la lectura SQL (vínculo y
/// autorización Activa y vigente), como en el resto del portal. La familia nunca elige un profesional.
/// </summary>
public sealed class FamilyAppointmentsApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyAppointments appointments)
{
    public Task<ApplicationResult<FamilyAppointmentsHome>> ReadHomeAsync(FamilyPortalQuery query, Guid linkId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await appointments.ReadHomeAsync(await AccessAsync(query, ct), linkId, Today(), DateTimeOffset.UtcNow, ct) ?? throw new AccessDeniedException());

    public Task<ApplicationResult<IReadOnlyList<AppointmentSlot>>> ListSlotsAsync(
        FamilyPortalQuery query, Guid linkId, AppointmentTeam? team, AppointmentModality? modality, Guid? reschedulingId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var slotRequest = AppointmentSlotRequest.Validate(team, modality);
            return await appointments.ListSlotsAsync(
                await AccessAsync(query, ct), linkId, slotRequest.Team, slotRequest.Modality, reschedulingId, Today(), DateTimeOffset.UtcNow, ct)
                ?? throw new AccessDeniedException();
        });

    public Task<ApplicationResult<FamilyAppointment>> BookAsync(BookFamilyAppointmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var request = AppointmentSlotRequest.ValidateBooking(command.Equipo, command.Modalidad, command.Inicio, command.OperacionId);
            var access = await AccessAsync(new FamilyPortalQuery(command.AmbitoPerfilId, command.CentroId), ct);
            return await appointments.BookAsync(
                access, command.VinculoId, request.Team, request.Modality, request.StartsAt!.Value, command.OperacionId, Today(), DateTimeOffset.UtcNow, ct)
                ?? throw new AccessDeniedException();
        });

    public Task<ApplicationResult<FamilyAppointment>> RescheduleAsync(RescheduleFamilyAppointmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var newStart = command.NuevoInicio ?? throw new DomainValidationException(AppointmentSlotRequest.InvalidCode);
            var access = await AccessAsync(new FamilyPortalQuery(command.AmbitoPerfilId, command.CentroId), ct);
            return await appointments.RescheduleAsync(access, command.VinculoId, command.CitaId, newStart, Today(), DateTimeOffset.UtcNow, ct)
                ?? throw new AccessDeniedException();
        });

    public Task<ApplicationResult<bool>> CancelAsync(FamilyPortalQuery query, Guid linkId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await appointments.CancelAsync(await AccessAsync(query, ct), linkId, appointmentId, Today(), DateTimeOffset.UtcNow, ct)
                ? true
                : throw new AccessDeniedException());

    public Task<ApplicationResult<FamilyAppointment>> ReadAppointmentAsync(
        FamilyPortalQuery query, Guid linkId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await appointments.ReadAppointmentAsync(await AccessAsync(query, ct), linkId, appointmentId, Today(), DateTimeOffset.UtcNow, ct)
            ?? throw new AccessDeniedException());

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private async Task<FamilyPortalAccess> AccessAsync(FamilyPortalQuery query, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct))
            .FirstOrDefault(s => s.ProfileScopeId == query.AmbitoPerfilId && s.CenterId == query.CentroId);
        return scope is null || scope.Profile != SystemProfile.Familiar
            ? throw new AccessDeniedException()
            : new FamilyPortalAccess(scope.ProfileScopeId, scope.AccountId, scope.CenterId);
    }
}
