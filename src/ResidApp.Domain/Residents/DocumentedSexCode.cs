using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Traduce DOCUMENTED_SEX_CODES de lib/domain/residents/resident.ts. Procede de documentación
/// administrativa, nunca se infiere y no admite texto libre.</summary>
public enum DocumentedSexCode
{
    [Code("male")] [Display(Name = "Hombre")] Hombre,
    [Code("female")] [Display(Name = "Mujer")] Mujer,
    [Code("other")] [Display(Name = "Otra categoría documentada")] OtraCategoriaDocumentada,
    [Code("unknown")] [Display(Name = "No consta")] NoConsta,
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
