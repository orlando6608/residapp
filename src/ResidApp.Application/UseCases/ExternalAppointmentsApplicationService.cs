using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Modules;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RegisterExternalAppointmentCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, ExternalAppointment Cita);

public sealed record UpdateExternalAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointment Cita, ExternalAppointmentReasonCode? Motivo, string? LineaMotivo);

public sealed record ChangeExternalAppointmentStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointmentStatus? Estado);

public sealed record CancelExternalAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointmentReasonCode? Motivo, string? LineaMotivo);

/// <summary>Las citas externas que ve quien mira y si puede registrarlas y cambiarlas.</summary>
public sealed record ExternalAppointmentList(IReadOnlyList<ExternalAppointmentRecord> Appointments, bool CanManage);

/// <summary>
/// Citas externas de los residentes, PR 1 (docs/producto/citas-externas-diseno.md; script 0101). Administración del centro registra y cambia siempre; Enfermería solo con el permiso
/// EXTERNAL_APPOINTMENT_MANAGE que Administración da a su perfil; Medicina las ve y no las cambia. Auxiliar, Dirección Clínica, Plataforma y Familia no tienen acceso en esta fase. Enfermería
/// y Medicina ven las citas de los residentes de sus unidades (lo aplica el repositorio); Administración, las de todo el centro. El permiso y el ámbito se vuelven a comprobar en cada
/// transacción del repositorio.
/// </summary>
public sealed class ExternalAppointmentsApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IExternalAppointmentRepository repository, TimeProvider clock)
{
    private static readonly SystemProfile[] Profiles = [SystemProfile.Administracion, SystemProfile.Enfermeria, SystemProfile.Medicina];

    public Task<ApplicationResult<Guid>> RegisterAsync(RegisterExternalAppointmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (actor, canManage) = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            RequireManage(canManage);
            return await repository.CreateAsync(actor, command.ResidenteId, command.Cita.RequireNotInPast(Today()), ct);
        });

    /// <summary>Devuelve la versión nueva. Cambiar la fecha o la hora es reprogramar y exige un motivo; el día nuevo no puede ser anterior a hoy. Cambiar otra cosa no lleva motivo.</summary>
    public Task<ApplicationResult<int>> UpdateAsync(UpdateExternalAppointmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (actor, canManage) = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            RequireManage(canManage);
            var current = await repository.FindAsync(actor, command.CitaId, ct) ?? throw new DomainValidationException(ExternalAppointment.NotFoundCode);
            var reschedules = ExternalAppointmentRules.IsReschedule(current.Appointment, command.Cita);
            if (reschedules)
            {
                command.Cita.RequireNotInPast(Today());
            }
            ExternalAppointmentReasonCode? code = null;
            string? line = null;
            if (reschedules)
            {
                var reason = ExternalAppointmentRules.Reason(command.Motivo, command.LineaMotivo);
                code = reason.Code;
                line = reason.Line;
            }
            return await repository.UpdateAsync(actor, command.CitaId, command.VersionVista, command.Cita, code, line, ct);
        });

    /// <summary>Devuelve la versión nueva. Cancelar tiene su propia operación (exige motivo); el resto de estados no.</summary>
    public Task<ApplicationResult<int>> ChangeStatusAsync(ChangeExternalAppointmentStatusCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Estado is not { } status || !Enum.IsDefined(status) || status == ExternalAppointmentStatus.Cancelada)
            {
                throw new DomainValidationException(ExternalAppointment.InvalidCode);
            }

            var (actor, canManage) = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            RequireManage(canManage);
            return await repository.ChangeStatusAsync(actor, command.CitaId, command.VersionVista, status, ct);
        });

    public Task<ApplicationResult<int>> CancelAsync(CancelExternalAppointmentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (code, line) = ExternalAppointmentRules.Reason(command.Motivo, command.LineaMotivo);
            var (actor, canManage) = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            RequireManage(canManage);
            return await repository.CancelAsync(actor, command.CitaId, command.VersionVista, code, line, ct);
        });

    public Task<ApplicationResult<ExternalAppointmentRecord>> FindAsync(Guid profileScopeId, CenterId centerId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (actor, _) = await ResolveAsync(profileScopeId, centerId, ct);
            return await repository.FindAsync(actor, appointmentId, ct) ?? throw new DomainValidationException(ExternalAppointment.NotFoundCode);
        });

    public Task<ApplicationResult<ExternalAppointmentList>> ListAsync(
        Guid profileScopeId, CenterId centerId, DateOnly from, DateOnly to, ResidentId? residentId = null, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (to < from)
            {
                throw new DomainValidationException(ExternalAppointment.InvalidCode);
            }

            var (actor, canManage) = await ResolveAsync(profileScopeId, centerId, ct);
            return new ExternalAppointmentList(await repository.ListAsync(actor, from, to, residentId, ct), canManage);
        });

    /// <summary>Si el ámbito activo puede registrar y cambiar citas (Administración, o Enfermería con el permiso). Medicina y el resto de perfiles permitidos, no.</summary>
    public Task<ApplicationResult<bool>> CanManageAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => (await ResolveAsync(profileScopeId, centerId, ct)).CanManage);

    /// <summary>Días de antelación del aviso de «falta transporte o acompañante» (diseño, valor 16) y desde cuántos se destaca más.</summary>
    public const int WarningDays = 7;
    public const int UrgentWarningDays = 2;

    /// <summary>Hasta cuántos días atrás se buscan las citas «por cerrar».</summary>
    public const int OverdueLookbackDays = 365;

    /// <summary>Las citas «por cerrar» (decisión de Orlando, 2026-11-18): abiertas (programadas o confirmadas por el hospital) cuyo día ya pasó sin marcarse realizadas ni «no se acudió», de los
    /// últimos <see cref="OverdueLookbackDays"/> días, por fecha. Cada perfil con acceso ve las de su ámbito; con residentId, solo las de ese residente. Se calcula al abrir, sin tabla.</summary>
    public Task<ApplicationResult<IReadOnlyList<ExternalAppointmentRecord>>> ListOverdueAsync(
        Guid profileScopeId, CenterId centerId, ResidentId? residentId = null, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<ExternalAppointmentRecord>>(async () =>
        {
            var (actor, _) = await ResolveAsync(profileScopeId, centerId, ct);
            var today = Today();
            var past = await repository.ListAsync(actor, today.AddDays(-OverdueLookbackDays), today.AddDays(-1), residentId, ct);
            return past.Where(a => ExternalAppointmentRules.IsOpen(a.Status)).ToList();
        });

    /// <summary>El aviso del inicio de Administración (diseño, valor 16): citas abiertas que empiezan dentro de <see cref="WarningDays"/> días con el transporte o el acompañante sin
    /// confirmar, por fecha. Se calcula al abrir, sin tabla. Solo Administración; para el resto, acceso denegado.</summary>
    public Task<ApplicationResult<IReadOnlyList<ExternalAppointmentRecord>>> ListUncoveredAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<IReadOnlyList<ExternalAppointmentRecord>>(async () =>
        {
            var (actor, _) = await ResolveAsync(profileScopeId, centerId, ct);
            if (actor.Profile != SystemProfile.Administracion)
            {
                throw new AccessDeniedException();
            }

            var today = Today();
            var all = await repository.ListAsync(actor, today, today.AddDays(WarningDays), null, ct);
            return all.Where(a => ExternalAppointmentRules.IsOpen(a.Status) && !a.Appointment.Covered).ToList();
        });

    /// <summary>Hoy en el centro (hora de Madrid), para que las pantallas ofrezcan el mismo día que el servicio exige.</summary>
    public DateOnly TodayAtCenter() => Today();

    private DateOnly Today() => CenterModuleRules.TodayInMadrid(clock.GetUtcNow());

    private static void RequireManage(bool canManage)
    {
        if (!canManage)
        {
            throw new AccessDeniedException();
        }
    }

    /// <summary>El ámbito activo de la cuenta de la sesión: Administración, Enfermería o Medicina (si no, acceso denegado) y si puede registrar y cambiar citas.</summary>
    private async Task<(ExternalAppointmentActor Actor, bool CanManage)> ResolveAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }

        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct)).FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || !Profiles.Contains(scope.Profile))
        {
            throw new AccessDeniedException();
        }

        var canManage = scope.Profile == SystemProfile.Administracion
            || (scope.Profile == SystemProfile.Enfermeria
                && (await scopes.ListPermissionsAsync(identity.ExternalSubject, profileScopeId, centerId, ct)).Contains(ProfilePermissions.ExternalAppointmentManage));
        return (new ExternalAppointmentActor(scope.AccountId, scope.ProfileScopeId, scope.Profile, scope.CenterId), canManage);
    }
}
