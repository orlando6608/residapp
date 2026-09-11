using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Traduce ResidentAdministrativeLocation de resident.ts.</summary>
public sealed record ResidentAdministrativeLocation(string? Building = null, string? Floor = null, string? Room = null, string? Place = null)
{
    public static readonly ResidentAdministrativeLocation Empty = new();
}

/// <summary>
/// Traduce ResidentAdministrativeIdentity de lib/domain/residents/resident.ts (identidad administrativa
/// mínima exigida por RES-01). DisplayName y BirthDate usan propiedades respaldadas por 'field' (C# 14)
/// para validación compacta de una sola propiedad, tal como se pidió explícitamente: no vacío/recortado
/// y fecha de nacimiento no futura, replicando las comprobaciones de
/// db/repositories/resident-repository.ts (assertInput).
/// </summary>
public sealed class Resident
{
    public required ResidentId Id { get; init; }
    public required CenterId CenterId { get; init; }
    public required UnitId UnitId { get; init; }

    public required string DisplayName
    {
        get;
        init => field = !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new DomainValidationException("RESIDENT_CREATE_INPUT_INVALID");
    }

    public required DateOnly BirthDate
    {
        get;
        init => field = value <= DateOnly.FromDateTime(DateTime.UtcNow)
            ? value
            : throw new DomainValidationException("RESIDENT_CREATE_INPUT_INVALID");
    }

    public required DocumentedSexCode DocumentedSex { get; init; }
    public required ResidentStatus Status { get; init; }
    public ResidentAdministrativeLocation Location { get; init; } = ResidentAdministrativeLocation.Empty;
}
