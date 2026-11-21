using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Modules;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Encender o apagar el interruptor del centro. VersionVista es el número que vio quien cambia (0 si nunca se tocó). Las tres confirmaciones solo cuentan al encender.</summary>
public sealed record SetExternalFamilyCenterCommand(
    Guid AmbitoPerfilId, CenterId CentroId, int VersionVista, bool Encender, bool Informada, bool Voluntad, bool Eipd);

/// <summary>Restringir (o dejar de restringir) a la familia de un residente.</summary>
public sealed record SetExternalFamilyResidentCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, int VersionVista, bool Restringir);

/// <summary>Encender o apagar los avisos por correo de citas externas del centro (PR 6). Informada solo cuenta al encender.</summary>
public sealed record SetExternalMailCommand(Guid AmbitoPerfilId, CenterId CentroId, int VersionVista, bool Encender, bool Informada);

/// <summary>Dar de baja (o volver a dar de alta) a una persona de la familia de los avisos por correo de citas externas (PR 6).</summary>
public sealed record SetExternalFamilyMemberMailCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid FamiliarId, int VersionVista, bool DarDeBaja);

/// <summary>
/// Los interruptores de las citas externas para la familia (PR 5; script 0104). El del centro lo cambia solo Administración, con las tres confirmaciones al encender. El del residente lo cambian
/// Administración y la Enfermería de su unidad (sin el permiso de gestionar citas: es una herramienta de privacidad); Medicina solo lo ve. El ámbito y la unidad se vuelven a comprobar en cada
/// transacción del repositorio; aquí se resuelve quién actúa.
/// </summary>
public sealed class ExternalAppointmentFamilySettingsApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IExternalAppointmentFamilySettings settings, TimeProvider clock)
{
    public Task<ApplicationResult<ExternalFamilyCenterState>> ReadCenterAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.ReadCenterAsync(await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion], ct), Today(), ct));

    /// <summary>Devuelve el número nuevo. Encender sin las tres confirmaciones: entrada no válida. Otra persona cambió antes: conflicto de versión.</summary>
    public Task<ApplicationResult<int>> SetCenterAsync(SetExternalFamilyCenterCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.SetCenterAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion], ct), command.VersionVista, command.Encender,
                new ExternalFamilyConfirmations(command.Informada, command.Voluntad, command.Eipd), ct));

    public Task<ApplicationResult<ExternalFamilyResidentState>> ReadResidentAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.ReadResidentAsync(
                await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion, SystemProfile.Enfermeria, SystemProfile.Medicina], ct), residentId, Today(), ct));

    public Task<ApplicationResult<int>> SetResidentAsync(SetExternalFamilyResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.SetResidentAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion, SystemProfile.Enfermeria], ct), command.ResidenteId, command.VersionVista, command.Restringir, ct));

    // ---- PR 6 (script 0105): los avisos por correo y lo que ofrece la familia ----

    public Task<ApplicationResult<ExternalFamilyMailState>> ReadMailAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await settings.ReadMailAsync(await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion], ct), ct));

    /// <summary>Solo Administración. Encender exige la confirmación; apagar no. Devuelve el número nuevo.</summary>
    public Task<ApplicationResult<int>> SetMailAsync(SetExternalMailCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.SetMailAsync(await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion], ct), command.VersionVista, command.Encender, command.Informada, ct));

    public Task<ApplicationResult<IReadOnlyList<ExternalFamilyMember>>> ListFamilyMembersAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.ListFamilyMembersAsync(await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion], ct), residentId, ct));

    public Task<ApplicationResult<int>> SetFamilyMemberMailAsync(SetExternalFamilyMemberMailCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.SetFamilyMemberMailAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion], ct), command.ResidenteId, command.FamiliarId, command.VersionVista, command.DarDeBaja, ct));

    /// <summary>Lo que la familia ha ofrecido en una cita. Null si el actor no la ve (el mismo «no existe» de siempre).</summary>
    public Task<ApplicationResult<ExternalFamilyOffers>> ReadOffersAsync(Guid profileScopeId, CenterId centerId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.ReadOffersAsync(
                await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion, SystemProfile.Enfermeria, SystemProfile.Medicina], ct), appointmentId, Today(), ct)
            ?? throw new AccessDeniedException());

    public Task<ApplicationResult<IReadOnlySet<Guid>>> ListOfferedAsync(Guid profileScopeId, CenterId centerId, IReadOnlyCollection<Guid> appointmentIds, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await settings.ListOfferedAsync(
                await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion, SystemProfile.Enfermeria, SystemProfile.Medicina], ct), appointmentIds, Today(), ct));

    private DateOnly Today() => CenterTime.TodayAt(clock.GetUtcNow());

    private async Task<ExternalAppointmentActor> ActorAsync(Guid profileScopeId, CenterId centerId, IReadOnlyCollection<SystemProfile> allowed, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }

        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct)).FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        return scope is null || !allowed.Contains(scope.Profile)
            ? throw new AccessDeniedException()
            : new ExternalAppointmentActor(scope.AccountId, scope.ProfileScopeId, scope.Profile, scope.CenterId);
    }
}

/// <summary>
/// Las citas externas en el Portal Familiar, solo ver (PR 5). Solo con el perfil activo FAMILIAR; el acceso a cada residente y la regla de visibilidad (centro encendido, residente no restringido, cita
/// no oculta) los decide la lectura SQL en cada petición. Cualquier fallo es el mismo AccessDenied, sin distinguir un vínculo ajeno, un interruptor apagado o una cita inexistente.
/// </summary>
public sealed class FamilyExternalAppointmentsApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IFamilyExternalAppointments appointments, TimeProvider clock)
{
    /// <summary>Si el enlace «Citas fuera del centro» debe salir en la pantalla del residente. Sin vínculo accesible, false.</summary>
    public Task<ApplicationResult<bool>> IsAvailableAsync(FamilyPortalQuery query, Guid linkId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await appointments.IsAvailableAsync(await AccessAsync(query, ct), linkId, Today(), ct));

    public Task<ApplicationResult<FamilyExternalAppointments>> ListAsync(FamilyPortalQuery query, Guid linkId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await appointments.ListAsync(await AccessAsync(query, ct), linkId, Today(), ct) ?? throw new AccessDeniedException());

    public Task<ApplicationResult<(FamilyPortalResident Resident, ExternalAppointmentForFamily Appointment)>> FindAsync(
        FamilyPortalQuery query, Guid linkId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await appointments.FindAsync(await AccessAsync(query, ct), linkId, appointmentId, Today(), ct) ?? throw new AccessDeniedException());

    /// <summary>«Me encargo yo», «Ya no puedo», «No puedo» y deshacer «No puedo» (PR 6). Lo que la familia no puede ver es el mismo AccessDenied; «ya lo tiene otra persona» y «ya no hace falta» se devuelven como resultado.</summary>
    public Task<ApplicationResult<FamilyArrangementOutcome>> ArrangeAsync(
        FamilyPortalQuery query, Guid linkId, Guid appointmentId, ExternalArrangementScope scope, FamilyArrangementAction action, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var outcome = await appointments.ArrangeAsync(await AccessAsync(query, ct), linkId, appointmentId, scope, action, Today(), ct);
            return outcome == FamilyArrangementOutcome.NotFound ? throw new AccessDeniedException() : outcome;
        });

    private DateOnly Today() => CenterTime.TodayAt(clock.GetUtcNow());

    private async Task<FamilyPortalAccess> AccessAsync(FamilyPortalQuery query, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct)).FirstOrDefault(s => s.ProfileScopeId == query.AmbitoPerfilId && s.CenterId == query.CentroId);
        return scope is null || scope.Profile != SystemProfile.Familiar
            ? throw new AccessDeniedException()
            : new FamilyPortalAccess(scope.ProfileScopeId, scope.AccountId, scope.CenterId);
    }
}
