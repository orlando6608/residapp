using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Modules;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record RegisterExternalAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, ExternalAppointment Cita, Guid? OperacionId = null, Guid? CitaOrigenId = null);

public sealed record UpdateExternalAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointment Cita, ExternalAppointmentReasonCode? Motivo, string? LineaMotivo);

public sealed record ChangeExternalAppointmentStatusCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointmentStatus? Estado);

public sealed record CancelExternalAppointmentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointmentReasonCode? Motivo, string? LineaMotivo);

/// <summary>Anotar la nota «al terminar» (PR 4). VersionVista es el número de la versión vigente que vio quien escribe (0 si no había).</summary>
public sealed record SaveExternalAppointmentNoteCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalAppointmentNote Nota, Guid? OperacionId = null);

public sealed record RetractExternalAppointmentNoteCommand(
    Guid AmbitoPerfilId, CenterId CentroId, Guid CitaId, int VersionVista, ExternalNoteRetractionReason? Motivo, Guid? OperacionId = null);

/// <summary>Las citas externas que ve quien mira y si puede registrarlas y cambiarlas.</summary>
public sealed record ExternalAppointmentList(IReadOnlyList<ExternalAppointmentRecord> Appointments, bool CanManage);

/// <summary>Las citas de hoy y mañana del Auxiliar y qué día es hoy en el centro.</summary>
public sealed record AuxiliarExternalAppointments(DateOnly Today, IReadOnlyList<ExternalAppointmentForAuxiliar> Appointments);

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
            return await repository.CreateAsync(actor, command.ResidenteId, command.Cita.RequireNotInPast(Today()), command.OperacionId, command.CitaOrigenId, ct);
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

    /// <summary>Anota una versión nueva de la nota de una cita realizada y devuelve su número. La escribe cualquier Enfermería con acceso al residente (no hace falta el permiso de gestionar citas);
    /// Administración, Medicina y el resto, no (decisión de Orlando, 2026-11-18; DPO, apartado 7).</summary>
    public Task<ApplicationResult<int>> SaveNoteAsync(SaveExternalAppointmentNoteCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (actor, _) = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            RequireNurse(actor);
            return await repository.SaveNoteAsync(actor, command.CitaId, command.VersionVista, command.Nota, command.OperacionId, ct);
        });

    /// <summary>Retira la nota vigente (versión nueva marcada como retirada, con motivo de lista cerrada) y devuelve su número. Mismas reglas de quién escribe.</summary>
    public Task<ApplicationResult<int>> RetractNoteAsync(RetractExternalAppointmentNoteCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Motivo is not { } reason || !Enum.IsDefined(reason))
            {
                throw new DomainValidationException(ExternalAppointment.InvalidCode);
            }

            var (actor, _) = await ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            RequireNurse(actor);
            return await repository.RetractNoteAsync(actor, command.CitaId, command.VersionVista, reason, command.OperacionId, ct);
        });

    /// <summary>La última versión de la nota, solo para Enfermería y Medicina (Administración y el resto, acceso denegado); null si no hay ninguna. Si está retirada (Retracted), la nota ya no es la vigente pero su número es la versión vista para anotar otra.</summary>
    public Task<ApplicationResult<ExternalNoteVersion?>> FindNoteAsync(Guid profileScopeId, CenterId centerId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync<ExternalNoteVersion?>(async () =>
        {
            var (actor, _) = await ResolveAsync(profileScopeId, centerId, ct);
            RequireClinicalReader(actor);
            return await repository.FindCurrentNoteAsync(actor, appointmentId, ct);
        });

    /// <summary>Todas las versiones de la nota con su autoría, solo para Enfermería y Medicina; cada apertura deja un evento de auditoría sin contenido.</summary>
    public Task<ApplicationResult<IReadOnlyList<ExternalNoteVersion>>> ListNoteVersionsAsync(
        Guid profileScopeId, CenterId centerId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (actor, _) = await ResolveAsync(profileScopeId, centerId, ct);
            RequireClinicalReader(actor);
            return await repository.ListNoteVersionsAsync(actor, appointmentId, ct);
        });

    /// <summary>La siguiente cita prevista (solo ese dato de la nota) y la siguiente cita ya registrada de una cita visible para Administración, Enfermería o Medicina.</summary>
    public Task<ApplicationResult<ExternalAppointmentFollowUp>> FindFollowUpAsync(Guid profileScopeId, CenterId centerId, Guid appointmentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (actor, _) = await ResolveAsync(profileScopeId, centerId, ct);
            return await repository.FindFollowUpAsync(actor, appointmentId, ct) ?? throw new DomainValidationException(ExternalAppointment.NotFoundCode);
        });

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

    /// <summary>Lo que ve el Auxiliar (PR 3): hoy y mañana (día del centro), solo de sus residentes asignados, y solo los campos de <see cref="ExternalAppointmentForAuxiliar"/>. Para otro perfil, acceso denegado.</summary>
    public Task<ApplicationResult<AuxiliarExternalAppointments>> ListForAuxiliarAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var scope = await ActiveScopeAsync(profileScopeId, centerId, [SystemProfile.Auxiliar], ct);
            var today = Today();
            var actor = new ExternalAppointmentActor(scope.AccountId, scope.ProfileScopeId, scope.Profile, scope.CenterId);
            return new AuxiliarExternalAppointments(today, await repository.ListForAuxiliarAsync(actor, today, today.AddDays(1), ct));
        });

    private DateOnly Today() => CenterModuleRules.TodayInMadrid(clock.GetUtcNow());

    private static void RequireManage(bool canManage)
    {
        if (!canManage)
        {
            throw new AccessDeniedException();
        }
    }

    private static void RequireNurse(ExternalAppointmentActor actor)
    {
        if (actor.Profile != SystemProfile.Enfermeria)
        {
            throw new AccessDeniedException();
        }
    }

    private static void RequireClinicalReader(ExternalAppointmentActor actor)
    {
        if (actor.Profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            throw new AccessDeniedException();
        }
    }

    /// <summary>El ámbito activo de la cuenta de la sesión, si es de uno de los perfiles permitidos; si no, acceso denegado.</summary>
    private async Task<ActiveProfileScope> ActiveScopeAsync(Guid profileScopeId, CenterId centerId, IReadOnlyCollection<SystemProfile> allowed, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }

        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct)).FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        return scope is null || !allowed.Contains(scope.Profile) ? throw new AccessDeniedException() : scope;
    }

    /// <summary>El ámbito activo de la cuenta de la sesión: Administración, Enfermería o Medicina (si no, acceso denegado) y si puede registrar y cambiar citas.</summary>
    private async Task<(ExternalAppointmentActor Actor, bool CanManage)> ResolveAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct)
    {
        var scope = await ActiveScopeAsync(profileScopeId, centerId, Profiles, ct);
        var identity = await session.GetVerifiedIdentityAsync(ct);
        var canManage = scope.Profile == SystemProfile.Administracion
            || (scope.Profile == SystemProfile.Enfermeria
                && (await scopes.ListPermissionsAsync(identity!.ExternalSubject, profileScopeId, centerId, ct)).Contains(ProfilePermissions.ExternalAppointmentManage));
        return (new ExternalAppointmentActor(scope.AccountId, scope.ProfileScopeId, scope.Profile, scope.CenterId), canManage);
    }
}
