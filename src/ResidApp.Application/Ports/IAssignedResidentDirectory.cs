using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente con asignación vigente para un ámbito de perfil Auxiliar (AUX-01).</summary>
public sealed record AssignedResidentSummary(ResidentId ResidentId, string DisplayName, string? UnitName, bool TieneBasalVigente);

/// <summary>
/// A diferencia de IAuthorizationEvidenceProvider (evidencia para un ResidentId ya conocido, pensada para
/// Create/Read/Sign de un único residente), este puerto descubre el conjunto completo de residentes con
/// ambitos_perfil_residente vigente para un ámbito de perfil, dentro de las unidades que ese ámbito tiene
/// concedidas — la definición de "residente asignado" del vertical Auxiliar
/// (docs/flujos-clinicos/registro-cotidiano-auxiliar.md).
/// </summary>
public interface IAssignedResidentDirectory
{
    Task<IReadOnlyList<AssignedResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);
}
