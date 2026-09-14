using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traduce los campos de entrada de resident-baseline-service.ts::createResident.</summary>
public sealed record CreateResidentCommand(
    Guid AmbitoPerfilId, CenterId CentroId, UnitId UnidadId, string NombreVisible, DateOnly FechaNacimiento,
    DocumentedSexCode SexoDocumentadoCodigo, string? ReferenciaInterna, Guid? EdificioId, Guid? PlantaId,
    Guid? HabitacionId, Guid? PlazaId, Guid OperacionId);

/// <summary>
/// Traduce createResident de lib/application/resident-baseline-service.ts. Composición interna por
/// petición: no es una Server Action ni un endpoint público por sí misma, la expone
/// ResidentBaselineApplicationService.
/// </summary>
public sealed class CreateResident(
    IAuthorizationEvidenceProvider evidenceProvider, ISessionIdentityProvider session, IResidentRepository repository)
{
    public Task<ApplicationResult<CreateResidentResult>> ExecuteAsync(CreateResidentCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var selection = new AuthorizationSelection(command.AmbitoPerfilId, command.CentroId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Create(command.UnidadId), ct: ct);
            var payload = new ResidentCreatePayload(
                command.NombreVisible, command.FechaNacimiento, command.SexoDocumentadoCodigo, command.ReferenciaInterna,
                command.EdificioId, command.PlantaId, command.HabitacionId, command.PlazaId, command.OperacionId);
            return await RequestAuthorizationContextResolver.ExecuteResidentCreateAsync(context, repository, payload, ct);
        });
}
