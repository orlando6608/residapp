using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record ReadBaselineHistoryCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId);

public sealed record ReadBaselineVersionCommand(Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, int NumeroVersion);

/// <summary>ENF-24 (historia 11 de Enfermería, 9 de Medicina): versiones firmadas del basal de un residente,
/// vigente e históricas, con motivo, perfil firmante, fecha, Barthel y la versión a la que sustituyó, y el contenido
/// de cada una. Gemelo de ReadCurrentBaseline: la política (BaselineHistoryRead) la permite a Enfermería y Medicina
/// sin permiso ni auditoría; Dirección Clínica sigue entrando solo por su lectura auditada. Lista vacía = sin basal
/// firmado; versión null = el residente no tiene esa versión.</summary>
public sealed class ReadBaselineHistory(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IBaselineRepository repository)
{
    public Task<ApplicationResult<IReadOnlyList<BaselineHistoryEntry>>> ExecuteAsync(
        ReadBaselineHistoryCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await RequestAuthorizationContextResolver.ExecuteBaselineHistoryReadAsync(
                await ResolveAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct), repository, ct));

    public Task<ApplicationResult<BaselineVersionDetail?>> ExecuteVersionAsync(
        ReadBaselineVersionCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
            await RequestAuthorizationContextResolver.ExecuteBaselineVersionReadAsync(
                await ResolveAsync(command.AmbitoPerfilId, command.CentroId, command.ResidenteId, ct), repository, command.NumeroVersion, ct));

    private Task<RequestAuthorizationContext> ResolveAsync(Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct) =>
        RequestAuthorizationContextResolver.ResolveAsync(
            evidenceProvider, session, new AuthorizationSelection(profileScopeId, centerId), new AuthorizationTarget.Read(residentId),
            ClinicalResourceType.BaselineHistory, ct: ct);
}
