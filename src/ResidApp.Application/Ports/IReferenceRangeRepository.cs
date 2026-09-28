using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una fila del historial de rangos de referencia. Anterior a null: no había rango; nuevo a null: se
/// quitó. De quien lo cambió solo se expone el perfil y si fue la propia cuenta.</summary>
public sealed record ReferenceRangeChange(
    VitalSignCode Code, decimal? PreviousMin, decimal? PreviousMax, decimal? NewMin, decimal? NewMax,
    SystemProfile ChangedByProfile, bool ChangedByCurrentAccount, DateTimeOffset ChangedAt);

/// <summary>Rangos vigentes del centro, su historial (más reciente primero) y la versión para la concurrencia
/// optimista (número de cambios registrados en el centro).</summary>
public sealed record ReferenceRangesView(IReadOnlyList<VitalSignRange> Ranges, IReadOnlyList<ReferenceRangeChange> History, int Version);

public sealed record ReferenceRangesAccess(Guid ProfileScopeId, AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId);

/// <summary>Ranges contiene el rango deseado de cada constante; una constante ausente se queda sin rango.</summary>
public sealed record SaveReferenceRangesInput(ReferenceRangesAccess Access, int ExpectedVersion, IReadOnlyList<VitalSignRange> Ranges);

/// <summary>
/// Rangos de referencia de constantes por centro (decisión de 2026-09-28): solo con el permiso activo
/// REFERENCE_RANGES_MANAGE en ese ámbito (si no, REFERENCE_RANGES_NOT_AUTHORIZED). Guardar exige la versión
/// leída (si otro cambió los rangos entretanto, REFERENCE_RANGES_REVISION_CONFLICT), registra una fila de
/// historial por constante que cambia y devuelve la nueva versión.
/// </summary>
public interface IReferenceRangeRepository
{
    Task<ReferenceRangesView> ReadAsync(ReferenceRangesAccess access, CancellationToken ct = default);

    Task<int> SaveAsync(SaveReferenceRangesInput input, CancellationToken ct = default);
}
