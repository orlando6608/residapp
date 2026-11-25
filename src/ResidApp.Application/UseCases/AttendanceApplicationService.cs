using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Attendance;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Encender o apagar la asistencia del centro. VersionVista es el número que vio quien cambia (0 si nunca se tocó). Las dos confirmaciones solo cuentan al encender.</summary>
public sealed record SetAttendanceCenterCommand(Guid AmbitoPerfilId, CenterId CentroId, int VersionVista, bool Encender, bool Informada, bool Eipd);

/// <summary>Anotar (o corregir) si una persona vino un día. La causa solo con «no vino»; la nota solo con «otro motivo».</summary>
public sealed record RecordAttendanceCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, DateOnly Fecha, AttendanceState Estado, AttendanceCause? Causa, string? Nota);

/// <summary>Fijar la pauta de una persona: la máscara de los días de la semana en que se la espera (lunes = 1 … domingo = 64).</summary>
public sealed record SetAttendancePatternCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, int VersionVista, int DiasEsperados, DateOnly? ValeDesde = null, bool SustituirProgramado = false);

public sealed record RegisterAbsenceCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, DateOnly Desde, DateOnly Hasta, AbsenceReason Motivo);

public sealed record AbsenceChangeCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid AusenciaId);

public sealed record ShortenAbsenceCommand(Guid AmbitoPerfilId, CenterId CentroId, Guid AusenciaId, DateOnly NuevoHasta);

/// <summary>
/// La asistencia diaria del Centro de Día (ADR 0019). Aquí se resuelve quién actúa (la sesión verificada y su ámbito vigente) y qué perfil puede hacer cada cosa; el almacén vuelve a comprobar el ámbito,
/// el tipo de centro, el interruptor y el alcance sobre el residente dentro de cada transacción. Administración lee la asistencia pero no la anota ni la corrige; la fecha es la del centro.
/// </summary>
public sealed class AttendanceApplicationService(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, IAttendanceStore store, TimeProvider clock)
{
    public Task<ApplicationResult<AttendanceCenterState>> ReadCenterAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await store.ReadCenterAsync(await ActorAsync(profileScopeId, centerId, ReadProfiles, ct), ct));

    /// <summary>Solo Administración. Devuelve el número nuevo. Encender sin las dos confirmaciones: entrada no válida; otra persona cambió antes: conflicto de versión.</summary>
    public Task<ApplicationResult<int>> SetCenterAsync(SetAttendanceCenterCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            if (command.Encender && !(command.Informada && command.Eipd))
            {
                throw new DomainValidationException(AttendanceRules.InvalidCode);
            }

            return await store.SetCenterAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion], ct), command.VersionVista, command.Encender, command.Informada, command.Eipd, ct);
        });

    /// <summary>La lista de un día (nulo = hoy en el centro). Quien anota y Administración; Dirección no.</summary>
    public Task<ApplicationResult<IReadOnlyList<AttendanceDayEntry>>> ListDayAsync(Guid profileScopeId, CenterId centerId, DateOnly? date, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var actor = await ActorAsync(profileScopeId, centerId, ReadProfiles, ct);
            var day = date ?? Today();
            if (day > Today())
            {
                throw new DomainValidationException(AttendanceRules.DateNotAllowedCode);
            }

            return await store.ListDayAsync(actor, day, ct);
        });

    /// <summary>Auxiliar, Enfermería y Medicina. Auxiliar, hasta 7 días atrás; Enfermería y Medicina, sin ese tope. Nunca una fecha futura. Devuelve el número de la versión vigente.</summary>
    public Task<ApplicationResult<int>> RecordAsync(RecordAttendanceCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var actor = await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Auxiliar, SystemProfile.Enfermeria, SystemProfile.Medicina], ct);
            var today = Today();
            AttendanceRules.ValidateDate(actor.Profile, command.Fecha, today);
            var note = AttendanceRules.Validate(command.Estado, command.Causa, command.Nota);
            return await store.RecordAsync(actor, command.ResidenteId, command.Fecha, today, command.Estado, command.Causa, note, ct);
        });

    public Task<ApplicationResult<AttendancePatternState>> ReadPatternAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await store.ReadPatternAsync(await ActorAsync(profileScopeId, centerId, ReadProfiles, ct), residentId, Today(), ct));

    /// <summary>Administración y Enfermería. Devuelve el número vigente.</summary>
    public Task<ApplicationResult<int>> SetPatternAsync(SetAttendancePatternCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var mask = AttendanceRules.ValidatePattern(command.DiasEsperados);
            return await store.SetPatternAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion, SystemProfile.Enfermeria], ct), command.ResidenteId, command.VersionVista, mask, command.ValeDesde, Today(), command.SustituirProgramado, ct);
        });

    /// <summary>Quien lee la asistencia ve las ausencias; el motivo, solo Enfermería y Medicina.</summary>
    public Task<ApplicationResult<IReadOnlyList<ScheduledAbsence>>> ListAbsencesAsync(Guid profileScopeId, CenterId centerId, ResidentId? residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await store.ListAbsencesAsync(await ActorAsync(profileScopeId, centerId, ReadProfiles, ct), residentId, Today(), ct));

    /// <summary>Las ya terminadas, solo para quien las corrige: Administración y Enfermería; el motivo, solo Enfermería.</summary>
    public Task<ApplicationResult<IReadOnlyList<ScheduledAbsence>>> ListEndedAbsencesAsync(Guid profileScopeId, CenterId centerId, ResidentId? residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await store.ListEndedAbsencesAsync(
            await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion, SystemProfile.Enfermeria], ct), residentId, Today(), ct));

    /// <summary>Administración y Enfermería. Administración elige el motivo al registrar y no lo vuelve a ver. Devuelve el identificador de la ausencia.</summary>
    public Task<ApplicationResult<Guid>> RegisterAbsenceAsync(RegisterAbsenceCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            AttendanceRules.ValidateAbsence(command.Desde, command.Hasta, command.Motivo);
            return await store.RegisterAbsenceAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion, SystemProfile.Enfermeria], ct), command.ResidenteId, command.Desde, command.Hasta, command.Motivo, ct);
        });

    public Task<ApplicationResult<bool>> AnnulAbsenceAsync(AbsenceChangeCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await store.AnnulAbsenceAsync(await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion, SystemProfile.Enfermeria], ct), command.AusenciaId, ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ShortenAbsenceAsync(ShortenAbsenceCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await store.ShortenAbsenceAsync(
                await ActorAsync(command.AmbitoPerfilId, command.CentroId, [SystemProfile.Administracion, SystemProfile.Enfermeria], ct), command.AusenciaId, command.NuevoHasta, ct);
            return true;
        });

    /// <summary>Solo Administración: quién lleva <see cref="AttendanceRules.FollowUpDays"/> o más días esperados seguidos sin venir, y quién tiene días sin anotar. Es una consulta: no guarda nada ni avisa a nadie.</summary>
    public Task<ApplicationResult<AttendanceFollowUp>> ListFollowUpAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () => await store.ListFollowUpAsync(await ActorAsync(profileScopeId, centerId, [SystemProfile.Administracion], ct), Today(), ct));

    private static readonly SystemProfile[] ReadProfiles = [SystemProfile.Administracion, SystemProfile.Auxiliar, SystemProfile.Enfermeria, SystemProfile.Medicina];

    private DateOnly Today() => CenterTime.TodayAt(clock.GetUtcNow());

    private async Task<AttendanceActor> ActorAsync(Guid profileScopeId, CenterId centerId, IReadOnlyCollection<SystemProfile> allowed, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.ExternalSubject))
        {
            throw new AccessDeniedException();
        }

        var scope = (await scopes.ListActiveAsync(identity.ExternalSubject, ct)).FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        return scope is null || !allowed.Contains(scope.Profile)
            ? throw new AccessDeniedException()
            : new AttendanceActor(scope.AccountId, scope.ProfileScopeId, scope.Profile, scope.CenterId);
    }
}
