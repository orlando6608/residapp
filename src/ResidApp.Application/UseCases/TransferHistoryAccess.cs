using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record DeclareTransferHistoryCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, string? Motivo, SystemProfile Perfil);

public sealed record CountTransferHistoryCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, SystemProfile Perfil);

/// <summary>
/// Historial de un residente trasladado (CJ, tercera tanda 5.2 C; script 0077): pedir con un motivo los episodios cerrados de la unidad anterior y saber si
/// hay alguno que ofrecer. Común a Enfermería y Medicina: cada controlador pasa su perfil y el ámbito activo tiene que ser de ese perfil. La visibilidad de
/// Medicina (solo escalados y propios) la aplica la consulta, no esto.
/// </summary>
public sealed class TransferHistoryAccess(
    IProfileScopeDirectoryProvider scopes, ISessionIdentityProvider session, ITransferHistoryAccess access, IChangeInboxDirectory directory,
    ClinicalAccessSettings settings)
{
    public Task<ApplicationResult<Guid>> DeclareAsync(DeclareTransferHistoryCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var identity = await EnsureScopeAsync(command.AmbitoPerfilId, command.CentroId, command.Perfil, ct);
            var reason = TransferHistoryRequest.NormalizeReason(command.Motivo);
            return await access.DeclareAsync(
                       identity.ExternalSubject, command.AmbitoPerfilId, command.CentroId, command.ResidenteId, reason, settings.DeclarationMinutes, ct)
                ?? throw new DomainValidationException(TransferHistoryRequest.NotApplicableCode);
        });

    public Task<ApplicationResult<int>> CountAsync(CountTransferHistoryCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            await EnsureScopeAsync(command.AmbitoPerfilId, command.CentroId, command.Perfil, ct);
            return await directory.CountPreviousUnitClosedEventsAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct);
        });

    private async Task<VerifiedIdentity> EnsureScopeAsync(Guid profileScopeId, CenterId centerId, SystemProfile profile, CancellationToken ct)
    {
        var identity = await session.GetVerifiedIdentityAsync(ct) ?? throw new AccessDeniedException();
        var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
        var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId == centerId);
        if (scope is null || scope.Profile != profile || profile is not (SystemProfile.Enfermeria or SystemProfile.Medicina))
        {
            throw new AccessDeniedException();
        }

        return identity;
    }
}
