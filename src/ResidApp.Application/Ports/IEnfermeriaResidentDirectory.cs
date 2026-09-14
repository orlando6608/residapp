using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente dentro del ámbito de un perfil Enfermería (ENF-17). A diferencia de
/// AssignedResidentSummary (Auxiliar, que exige siempre una fila en ambitos_perfil_residente),
/// Enfermería sigue la regla de "ámbito por defecto" ya usada en SqlAuthorizationEvidenceProvider: sin
/// ninguna restricción explícita, todos los residentes ubicados en las unidades concedidas son visibles;
/// si existe al menos una restricción, solo esos residentes lo son.</summary>
public sealed record ScopeResidentSummary(
    ResidentId ResidentId, string DisplayName, UnitId UnitId, string? UnitName, bool TieneBasalVigente);

/// <summary>
/// Descubre el conjunto de residentes visibles para un ámbito de perfil Enfermería (ENF-17), aplicando el
/// mismo criterio "ámbito por defecto o restringido" que SqlAuthorizationEvidenceProvider aplica para un
/// único residente — para que "aparece en mi lista" y "puedo abrirlo" sean siempre la misma cosa.
/// </summary>
public interface IEnfermeriaResidentDirectory
{
    Task<IReadOnlyList<ScopeResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);
}
