using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadDraftCancelledNoticeCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>Aviso, en la ficha del residente, a quien creó un borrador de basal que se canceló porque otra persona trasladó al residente de unidad: «tu
/// borrador se canceló por un traslado el [fecha]». Solo lo ve la cuenta que lo creó, durante <see cref="DaysShown"/> días; no lleva el contenido del
/// borrador ni quién trasladó. Se autoriza como la lectura del basal vigente (Enfermería y Medicina, residente de su ámbito). Devuelve null si no hay aviso.</summary>
public sealed class ReadDraftCancelledNotice(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository, TimeProvider clock)
{
    public const int DaysShown = 14;

    public Task<ApplicationResult<DraftCancelledNotice?>> ExecuteAsync(ReadDraftCancelledNoticeCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Read(command.ResidenteId),
                ClinicalResourceType.BaselineCurrent, ct: ct);
            return await RequestAuthorizationContextResolver.ExecuteDraftCancelledNoticeAsync(
                context, repository, clock.GetUtcNow().AddDays(-DaysShown), ct);
        });
}
