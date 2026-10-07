using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record DischargeResidentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid OperacionId, ResidentDischargeReason Motivo, string? MotivoTexto);

public sealed record ReactivateResidentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid OperacionId, UnitId UnidadId, Guid? HabitacionId, Guid? PlazaId);

public sealed record SuspendResidentCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Guid OperacionId, string? Nota);

/// <summary>
/// Baja, reactivación y suspensión del residente (CJ, 2026-10-07). Las hace Administración sobre un residente de su ámbito. La baja y la
/// suspensión parten de un residente activo (se autorizan como la corrección de identidad); la reactivación parte de uno dado de baja,
/// que ya no es «activo» para la autorización, así que comprueba el ámbito de Administración, que la baja sea visible en él y que la
/// unidad de destino sea de su ámbito, como al dar de alta.
/// </summary>
public sealed class ResidentStatusApplicationService(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, AdministrationAccessResolver administrationAccess,
    IResidentStatusRepository statuses, IResidentStatusDirectory directory)
{
    private const int MaxNoteLength = 500;

    public Task<ApplicationResult<IReadOnlyList<DischargedResident>>> ListDischargedAsync(
        Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await directory.ListDischargedAsync(profileScopeId, centerId, ct);
        });

    public Task<ApplicationResult<DischargedResident>> FindDischargedAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await directory.FindDischargedAsync(profileScopeId, centerId, residentId, ct) ?? throw new AccessDeniedException();
        });

    /// <summary>La suspensión abierta del residente, o null si no la tiene.</summary>
    public Task<ApplicationResult<ResidentSuspension?>> FindSuspensionAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await administrationAccess.ResolveAsync(profileScopeId, centerId, ct);
            return await directory.FindSuspensionAsync(centerId, residentId, ct);
        });

    public Task<ApplicationResult<bool>> DischargeAsync(DischargeResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await ResolveActiveResidentAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            var text = ResidentDischarge.ValidateReasonText(command.Motivo, command.MotivoTexto);
            await statuses.DischargeAsync(new DischargeResidentInput(target, command.OperacionId, command.Motivo, text), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ReactivateAsync(ReactivateResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var (access, _) = await administrationAccess.ResolveAsync(command.AmbitoPerfilId, command.CentroId, ct);
            _ = await directory.FindDischargedAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct)
                ?? throw new AccessDeniedException();
            // La unidad de destino tiene que ser de su ámbito, como al dar de alta.
            await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId),
                new AuthorizationTarget.Create(command.UnidadId), ct: ct);
            await statuses.ReactivateAsync(new ReactivateResidentInput(
                access.AccountId, command.CentroId, command.ResidenteId, command.OperacionId, command.UnidadId,
                command.HabitacionId, command.PlazaId), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> SuspendAsync(SuspendResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var target = await ResolveActiveResidentAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
            var note = string.IsNullOrWhiteSpace(command.Nota) ? null : command.Nota.Trim();
            if (note?.Length > MaxNoteLength)
            {
                throw new DomainValidationException("RESIDENT_SUSPENSION_INVALID");
            }

            await statuses.SuspendAsync(new SuspendResidentInput(target, command.OperacionId, note), ct);
            return true;
        });

    public Task<ApplicationResult<bool>> ResumeAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await statuses.ResumeAsync(await ResolveActiveResidentAsync(profileScopeId, centerId, residentId, ct), ct);
            return true;
        });

    private async Task<AdministrativeResidentTarget> ResolveActiveResidentAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct) =>
        RequestAuthorizationContextResolver.RequireResidentAdministration(
            await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, new AuthorizationSelection(profileScopeId, centerId),
                new AuthorizationTarget.IdentityUpdate(residentId), ct: ct));
}
