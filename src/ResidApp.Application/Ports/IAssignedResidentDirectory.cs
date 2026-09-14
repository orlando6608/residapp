using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente con asignación vigente para un ámbito de perfil Auxiliar (AUX-01). UnitId viaja
/// aquí (y no solo UnitName) porque los cierres cotidianos (AUX-04/AUX-05, grupo A2) necesitan la unidad
/// real para dejar constancia de dónde se registró el cierre. CerradoHoy sustituye al placeholder fijo
/// "Pendiente" del grupo A1, ahora que existe la tabla de cierres cotidianos.</summary>
public sealed record AssignedResidentSummary(
    ResidentId ResidentId, string DisplayName, UnitId UnitId, string? UnitName, bool TieneBasalVigente, bool CerradoHoy);

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
