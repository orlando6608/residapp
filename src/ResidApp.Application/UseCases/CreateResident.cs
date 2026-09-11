using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Traduce los campos de entrada de resident-baseline-service.ts::createResident.</summary>
public sealed record CreateResidentCommand(
    Guid ProfileScopeId, CenterId CenterId, UnitId UnitId, string DisplayName, DateOnly BirthDate,
    DocumentedSexCode DocumentedSexCode, string? InternalReference, Guid? BuildingId, Guid? FloorId,
    Guid? RoomId, Guid? PlaceId, Guid OperationId);

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
            var selection = new AuthorizationSelection(command.ProfileScopeId, command.CenterId);
            var context = await RequestAuthorizationContextResolver.ResolveAsync(
                evidenceProvider, session, selection, new AuthorizationTarget.Create(command.UnitId), ct: ct);
            var payload = new ResidentCreatePayload(
                command.DisplayName, command.BirthDate, command.DocumentedSexCode, command.InternalReference,
                command.BuildingId, command.FloorId, command.RoomId, command.PlaceId, command.OperationId);
            return await RequestAuthorizationContextResolver.ExecuteResidentCreateAsync(context, repository, payload, ct);
        });
}
