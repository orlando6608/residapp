using ResidApp.Application.Errors;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Traduce createResidentBaselineService de lib/application/resident-baseline-service.ts: fachada que
/// agrupa los 3 casos de uso del vertical residente/basal. Se registra en el contenedor de DI de
/// ResidApp.Web y es lo único que los controladores MVC deberían inyectar de este vertical.
/// </summary>
public sealed class ResidentBaselineApplicationService(
    CreateResident createResident, SignBaseline signBaseline, ReadDirectionBaseline readDirectionBaseline)
{
    public Task<ApplicationResult<CreateResidentResult>> CreateResidentAsync(
        CreateResidentCommand command, CancellationToken ct = default) =>
        createResident.ExecuteAsync(command, ct);

    public Task<ApplicationResult<SignBaselineDraftResult>> SignBaselineAsync(
        SignBaselineCommand command, CancellationToken ct = default) =>
        signBaseline.ExecuteAsync(command, ct);

    public Task<ApplicationResult<IReadOnlyList<AuditedBaselineHeader>>> ReadDirectionBaselineAsync(
        ReadDirectionBaselineCommand command, CancellationToken ct = default) =>
        readDirectionBaseline.ExecuteAsync(command, ct);
}
