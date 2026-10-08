using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente dentro del ámbito de un perfil Enfermería (ENF-17). A diferencia de
/// AssignedResidentSummary (Auxiliar, que exige siempre una fila en ambitos_perfil_residente),
/// Enfermería sigue la regla de "ámbito por defecto" ya usada en SqlAuthorizationEvidenceProvider: sin
/// ninguna restricción explícita, todos los residentes ubicados en las unidades concedidas son visibles;
/// si existe al menos una restricción, solo esos residentes lo son.</summary>
public sealed record ScopeResidentSummary(
    ResidentId ResidentId, string DisplayName, UnitId UnitId, string? UnitName, bool TieneBasalVigente,
    string? RoomName = null, string? PlaceName = null)
{
    /// <summary>La unidad con la habitación y la plaza actuales, si las tiene (script 0029).</summary>
    public string LocationLabel => ResidentLocationLabel.Format(UnitName, RoomName, PlaceName);
}

/// <summary>Texto de ubicación actual de un residente para las pantallas de Enfermería, Medicina y Auxiliar: «Unidad · Habitación · Plaza»,
/// omitiendo lo que no consta.</summary>
public static class ResidentLocationLabel
{
    public static string Format(string? unitName, string? roomName, string? placeName) =>
        string.Join(" · ", new[] { unitName ?? "Sin unidad", roomName, placeName }.Where(part => !string.IsNullOrEmpty(part)));
}

/// <summary>
/// Descubre el conjunto de residentes visibles para un ámbito de perfil Enfermería (ENF-17), aplicando el
/// mismo criterio "ámbito por defecto o restringido" que SqlAuthorizationEvidenceProvider aplica para un
/// único residente — para que "aparece en mi lista" y "puedo abrirlo" sean siempre la misma cosa.
/// </summary>
public interface IEnfermeriaResidentDirectory
{
    Task<IReadOnlyList<ScopeResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>ADM-08 (0022, 0044): los contactos urgentes vigentes de un residente que el caso de uso ya ha comprobado que está en
    /// el ámbito (FindEmergencyContact), en el orden en que se designaron; vacío si no tiene.</summary>
    Task<IReadOnlyList<EmergencyContactSummary>> FindEmergencyContactAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);
}

/// <summary>ADM-08 (0022): el contacto urgente designado por Administración, tal como lo leen Enfermería y Medicina (la
/// matriz les da lectura): nombre, relación y teléfono, sin correo.</summary>
public sealed record EmergencyContactSummary(string DisplayName, string Relationship, string Phone);
