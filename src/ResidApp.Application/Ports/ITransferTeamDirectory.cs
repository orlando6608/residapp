using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un equipo activo al que se puede transferir un seguimiento (script 0028).</summary>
public sealed record TransferTeam(Guid TeamId, string Name);

/// <summary>Los equipos activos de la unidad de un evento clínico, para elegir el equipo entrante de una transferencia. La regla de ámbito
/// (el evento es visible para ese ámbito de Enfermería o Medicina) va en la consulta; si no lo es, la lista sale vacía.</summary>
public interface ITransferTeamDirectory
{
    Task<IReadOnlyList<TransferTeam>> ListAsync(Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default);
}
