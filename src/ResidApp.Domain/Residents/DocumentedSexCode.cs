using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Traduce DOCUMENTED_SEX_CODES de lib/domain/residents/resident.ts. Procede de documentación
/// administrativa, nunca se infiere y no admite texto libre.</summary>
public enum DocumentedSexCode
{
    [Code("male")] Male,
    [Code("female")] Female,
    [Code("other")] Other,
    [Code("unknown")] Unknown,
}

/// <summary>
/// Traduce RESIDENT_STATUSES de resident.ts. El TS declara "ACTIVO"/"INACTIVO" pero ese tipo nunca se
/// referencia: el esquema D1 y los repositorios reales usan "ACTIVE"/"INACTIVE" en todo el flujo (ver
/// resident-repository.ts, baseline-repository.ts, authorization-subject-repository.ts). Se traduce lo
/// que el sistema realmente usa, no el tipo muerto.
/// </summary>
public enum ResidentStatus
{
    [Code("ACTIVE")] Active,
    [Code("INACTIVE")] Inactive,
}
