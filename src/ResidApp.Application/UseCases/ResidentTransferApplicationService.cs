using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traslado de un residente a otra unidad del centro (o a otra habitación o plaza). Lo hace Administración, o Enfermería con el permiso
/// RESIDENT_TRANSFER que concede Administración (CJ, 2026-10-07: «inicialmente Administración»), sobre un residente de su ámbito y hacia una unidad de
/// su ámbito.</summary>
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
            var (resident, profile) = RequestAuthorizationContextResolver.RequireResidentTransfer(
                await RequestAuthorizationContextResolver.ResolveAsync(
                    evidenceProvider, session, selection, new AuthorizationTarget.Transfer(command.ResidenteId), ct: ct));
            // La unidad de destino tiene que ser de su ámbito, con la misma regla del traslado.
            RequestAuthorizationContextResolver.RequireTransferDestination(
                await RequestAuthorizationContextResolver.ResolveAsync(
                    evidenceProvider, session, selection, new AuthorizationTarget.TransferDestination(command.UnidadDestinoId), ct: ct));
            return await transfers.TransferAsync(new TransferResidentInput(
                resident, profile, command.AmbitoPerfilId, command.OperacionId, command.UnidadOrigenEsperadaId, command.UnidadDestinoId,
                command.HabitacionId, command.PlazaId), ct);
        });
}
