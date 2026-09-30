using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadBaselineHistoryCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

/// <summary>ENF-24 (historia 11 de Enfermería, 9 de Medicina): versiones firmadas del basal de un residente,
/// vigente e históricas, con motivo, perfil firmante, fecha, Barthel y la versión a la que sustituyó. Gemelo de
/// ReadCurrentBaseline: la política (BaselineHistoryRead) la permite a Enfermería y Medicina sin permiso ni
/// auditoría; Dirección Clínica sigue entrando solo por su lectura auditada. Lista vacía = sin basal firmado.</summary>
public sealed class ReadBaselineHistory(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<IReadOnlyList<BaselineHistoryEntry>>> ExecuteAsync(
        ReadBaselineHistoryCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Read(command.ResidenteId),
                ClinicalResourceType.BaselineHistory, ct: ct);
            return await RequestAuthorizationContextResolver.ExecuteBaselineHistoryReadAsync(context, repository, ct);
        });
}
