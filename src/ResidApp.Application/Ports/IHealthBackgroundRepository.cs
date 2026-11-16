using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Un residente sobre el que el caso de uso ya ha comprobado el ámbito y el perfil (HealthBackgroundApplicationService), con la cuenta, el ámbito y
/// el perfil que firman el cambio y la unidad en la que está.</summary>
public sealed record HealthBackgroundTarget(
    AccountId AccountId, Guid ProfileScopeId, SystemProfile Profile, CenterId CenterId, UnitId UnitId, ResidentId ResidentId);

/// <summary>La versión vigente del apartado de alergias: su número, su contenido y quién y cuándo la guardó (la «última modificación», que se enseña por
/// apartado: la ficha no se revisa de forma periódica, así que lo honesto es decir cuándo cambió por última vez y no que está revisada).</summary>
public sealed record AllergyRecord(int Version, AllergyRecordContent Content, SystemProfile ModifiedByProfile, DateTimeOffset ModifiedAt);

/// <summary>La versión vigente del apartado de enfermedades e intervenciones (script 0096), con lo mismo que AllergyRecord.</summary>
public sealed record ConditionRecord(int Version, ConditionRecordContent Content, SystemProfile ModifiedByProfile, DateTimeOffset ModifiedAt);

/// <summary>
/// Ficha de antecedentes de salud, fases 1 y 2: alergias e intolerancias a medicamentos (script 0094) y enfermedades e intervenciones (script 0096). Cada guardado es una versión nueva y no se pisa la anterior;
/// «pendiente» es que no hay ninguna. Guardar exige haber visto la versión vigente (expectedVersion, 0 si no había ninguna): si otra persona guardó mientras
/// tanto, falla con HEALTH_BACKGROUND_REVISION_CONFLICT en vez de pisarla. Cada guardado deja HEALTH_BACKGROUND_SAVE en dbo.eventos_auditoria, en la misma
/// transacción y sin el texto de la ficha. El permiso HEALTH_BACKGROUND_EDIT se vuelve a comprobar dentro de la transacción.
/// </summary>
public interface IHealthBackgroundRepository
{
    /// <summary>La versión vigente, o null si el apartado está pendiente.</summary>
    Task<AllergyRecord?> ReadAllergiesAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Devuelve el número de la versión nueva. El motivo del registro inicial es el de la versión 1 y no otro.</summary>
    Task<int> SaveAllergiesAsync(
        HealthBackgroundTarget target, int expectedVersion, HealthBackgroundReason reason, AllergyRecordContent content, CancellationToken ct = default);

    /// <summary>La versión vigente del apartado de enfermedades e intervenciones, o null si está pendiente.</summary>
    Task<ConditionRecord?> ReadConditionsAsync(CenterId centerId, ResidentId residentId, CancellationToken ct = default);

    /// <summary>Como SaveAllergiesAsync, para el apartado de enfermedades e intervenciones (sus versiones se cuentan aparte).</summary>
    Task<int> SaveConditionsAsync(
        HealthBackgroundTarget target, int expectedVersion, HealthBackgroundReason reason, ConditionRecordContent content, CancellationToken ct = default);
}

/// <summary>La versión vigente existe pero no se puede dar por fiable (no tiene todos sus elementos o no cumple las reglas del apartado; la BD no puede impedirlo del todo,
/// script 0094). Lleva el número de esa versión para poder guardar otra que la corrija. No lleva ningún texto de la ficha.</summary>
public sealed class HealthBackgroundInconsistentException(int version) : Exception("HEALTH_BACKGROUND_INCONSISTENT")
{
    public int Version { get; } = version;
}
