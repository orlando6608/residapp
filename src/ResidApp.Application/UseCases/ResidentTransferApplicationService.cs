using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traslado de un residente a otra unidad del centro (o a otra habitación o plaza). Lo hace Administración sobre un residente de
/// su ámbito y hacia una unidad de su ámbito; el permiso para Enfermería queda para cuando un centro lo pida (CJ, 2026-10-07).</summary>
public sealed record TransferResidentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, UnitId UnidadOrigenEsperadaId, UnitId UnidadDestinoId,
    Guid? HabitacionId, Guid? PlazaId, Guid OperacionId);

public sealed class ResidentTransferApplicationService(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IResidentTransferRepository transfers)
{
    public Task<ApplicationResult<TransferResidentResult>> TransferAsync(TransferResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var resident = RequestAuthorizationContextResolver.RequireResidentAdministration(
                await RequestAuthorizationContextResolver.ResolveAsync(
                    evidenceProvider, session, selection, new AuthorizationTarget.IdentityUpdate(command.ResidenteId), ct: ct));
            // La unidad de destino tiene que ser de su ámbito, como al dar de alta.
            await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Create(command.UnidadDestinoId), ct: ct);
            return await transfers.TransferAsync(new TransferResidentInput(
                resident, command.OperacionId, command.UnidadOrigenEsperadaId, command.UnidadDestinoId,
                command.HabitacionId, command.PlazaId), ct);
        });
}
