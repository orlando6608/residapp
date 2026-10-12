using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record CreateFamilyAppointmentRequestCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid VinculoId, AppointmentTeam? Equipo, AppointmentModality? Modalidad, AppointmentRequestReason? Motivo,
    string? Comentario, IReadOnlyCollection<int>? Dias, AppointmentDayPart? ParteDelDia, Guid OperacionId);

/// <summary>
/// Solicitudes de cita del Portal Familiar (script 0057; historia 5 del portal, CIT-S01 a CIT-S04): la familia envía una solicitud, y cuando el centro
/// propone un horario lo acepta, pide otro o cancela. Solo con el perfil activo FAMILIAR; el acceso a cada residente lo decide la lectura SQL (vínculo
/// y autorización Activa y vigente), como en el resto del portal. No hay chat y la familia nunca ve ni elige un profesional.
/// </summary>
public sealed class FamilyAppointmentRequestsApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyAppointmentRequests requests)
{
    public Task<ApplicationResult<IReadOnlyList<FamilyAppointmentRequest>>> ListAsync(FamilyPortalQuery query, Guid linkId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await requests.ListAsync(await AccessAsync(query, ct), linkId, Today(), DateTimeOffset.UtcNow, ct) ?? throw new AccessDeniedException());

    public Task<ApplicationResult<FamilyAppointmentRequest>> ReadAsync(FamilyPortalQuery query, Guid linkId, Guid requestId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await requests.ReadAsync(await AccessAsync(query, ct), linkId, requestId, Today(), DateTimeOffset.UtcNow, ct) ?? throw new AccessDeniedException());

    public Task<ApplicationResult<FamilyAppointmentRequest>> CreateAsync(CreateFamilyAppointmentRequestCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var input = AppointmentRequestInput.Validate(
                command.Equipo, command.Modalidad, command.Motivo, command.Comentario, command.Dias, command.ParteDelDia);
            if (command.OperacionId == Guid.Empty)
            {
                throw new DomainValidationException(AppointmentRequestInput.InvalidCode);
            }

            var access = await AccessAsync(new FamilyPortalQuery(command.AmbitoPerfilId, command.CentroId), ct);
            return await requests.CreateAsync(access, command.VinculoId, input, command.OperacionId, Today(), DateTimeOffset.UtcNow, ct)
                ?? throw new AccessDeniedException();
        });

    public Task<ApplicationResult<FamilyAppointmentRequest>> AcceptAsync(FamilyPortalQuery query, Guid linkId, Guid requestId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await requests.AcceptAsync(await AccessAsync(query, ct), linkId, requestId, Today(), DateTimeOffset.UtcNow, ct) ?? throw new AccessDeniedException());

    public Task<ApplicationResult<FamilyAppointmentRequest>> RequestAnotherTimeAsync(
        FamilyPortalQuery query, Guid linkId, Guid requestId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await requests.RequestAnotherTimeAsync(await AccessAsync(query, ct), linkId, requestId, Today(), DateTimeOffset.UtcNow, ct)
            ?? throw new AccessDeniedException());

    public Task<ApplicationResult<bool>> CancelAsync(FamilyPortalQuery query, Guid linkId, Guid requestId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await requests.CancelAsync(await AccessAsync(query, ct), linkId, requestId, Today(), DateTimeOffset.UtcNow, ct)
                ? true
                : throw new AccessDeniedException());

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
