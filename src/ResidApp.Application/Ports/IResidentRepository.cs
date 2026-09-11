using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Traduce CreateResidentInput de db/repositories/resident-repository.ts.</summary>
public sealed record CreateResidentInput(
    AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId, UnitId UnitId,
    string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSexCode,
    string? InternalReference, Guid? BuildingId, Guid? FloorId, Guid? RoomId, Guid? PlaceId, Guid OperationId);

/// <summary>Traduce CreateResidentResult de resident-repository.ts.</summary>
public sealed record CreateResidentResult(ResidentId ResidentId, Guid EpisodeId, Guid LocationIntervalId);

/// <summary>Traduce createResidentWithInitialLocation de resident-repository.ts: alta de residente
/// idempotente por hash de petición, con episodio y ubicación iniciales en una única transacción.</summary>
public interface IResidentRepository
{
    Task<CreateResidentResult> CreateWithInitialLocationAsync(CreateResidentInput input, CancellationToken ct = default);
}
