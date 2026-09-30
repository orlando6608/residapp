using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traduce los campos de entrada de ENF-19/ENF-20 "crear borrador".</summary>
public sealed record CreateBaselineDraftCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, BaselineReason Motivo,
    InformationSourceCode FuenteInformacionComun, string? FuenteInformacionComunOtroTexto,
    DateOnly FechaInformacionComun, Guid OperacionId);

/// <summary>ENF-19/ENF-20: crea el borrador con permiso BASELINE_INITIAL_COMPLETE (motivo Alta) o
/// BASELINE_REEVALUATE (cualquier otro motivo), comprobado por el mismo motor de autorización que ya usa
/// la firma.</summary>
public sealed class CreateBaselineDraft(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<CreateBaselineDraftResult>> ExecuteAsync(
        CreateBaselineDraftCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Draft(command.ResidenteId, command.Motivo), ct: ct);
            var payload = new BaselineDraftCreatePayload(
                command.FuenteInformacionComun, command.FuenteInformacionComunOtroTexto, command.FechaInformacionComun, command.OperacionId);
            return await RequestAuthorizationContextResolver.ExecuteBaselineDraftCreateAsync(context, repository, payload, ct);
        });

    /// <summary>MED-21 (historia 8 de Medicina): si la cuenta podría crear un borrador para el residente, de alta o de
    /// reevaluación, con la misma autorización que ExecuteAsync. Sirve para ofrecer el acceso al basal solo a quien
    /// tiene el permiso; no escribe nada.</summary>
    public Task<ApplicationResult<bool>> CanCreateAsync(
        Guid ambitoPerfilId, CenterId centroId, ResidentId residenteId, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            foreach (var reason in new[] { BaselineReason.Alta, BaselineReason.RevisionProgramada })
            {
                try
                {
                    await RequestAuthorizationContextResolver.ResolveAsync(
                        evidenceProvider, session, new AuthorizationSelection(ambitoPerfilId, centroId),
                        new AuthorizationTarget.Draft(residenteId, reason), ct: ct);
                    return true;
                }
                catch (AccessDeniedException)
                {
                }
            }
            return false;
        });
}
