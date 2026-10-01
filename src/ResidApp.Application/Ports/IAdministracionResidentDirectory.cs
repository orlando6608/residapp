using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-02: un residente del ámbito de Administración. Solo identidad administrativa, unidad y fecha de alta:
/// nada de basal, Barthel ni contenido clínico (historia 1).</summary>
public sealed record AdministrativeResidentSummary(
    ResidentId ResidentId, string DisplayName, DateOnly BirthDate, DocumentedSexCode DocumentedSex, UnitId UnitId,
    string UnitName, DateTimeOffset AdmittedAt);

/// <summary>RES-04: un intervalo del historial de ubicación. Until es null en el vigente.</summary>
public sealed record ResidentLocationInterval(string UnitName, DateTimeOffset From, DateTimeOffset? Until, SystemProfile RecordedBy);

/// <summary>ADM-03 (0021): una corrección de identidad, con los valores anteriores y los nuevos.</summary>
public sealed record ResidentIdentityCorrectionEntry(
    int Number, ResidentIdentity Before, ResidentIdentity After, string Reason, DateTimeOffset CorrectedAt);

/// <summary>ADM-03: la ficha administrativa. Corrections va de la más antigua a la más reciente.</summary>
public sealed record AdministrativeResidentDetail(
    AdministrativeResidentSummary Resident, IReadOnlyList<ResidentLocationInterval> Locations,
    IReadOnlyList<ResidentIdentityCorrectionEntry> Corrections);

/// <summary>ADM-02/ADM-03: lectura de Administración. Aplica en la propia consulta la regla de ámbito de
/// SqlEnfermeriaResidentDirectory (ámbito activo de ADMINISTRACION, sus unidades y, si los restringe, sus residentes),
/// así que un residente ajeno devuelve null, sin distinguir si existe.</summary>
public interface IAdministracionResidentDirectory
{
    Task<IReadOnlyList<AdministrativeResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    Task<AdministrativeResidentDetail?> FindAsync(
        Guid profileScopeId, CenterId centerId, ResidentId residentId, CancellationToken ct = default);
}

public sealed record CorrectResidentIdentityInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId, ResidentIdentity Identity, string Reason,
    int ExpectedCorrections);

/// <summary>ADM-03 (0021): guarda una corrección de identidad y devuelve cuántas tiene ya el residente.</summary>
public interface IResidentIdentityRepository
{
    Task<int> CorrectAsync(CorrectResidentIdentityInput input, CancellationToken ct = default);
}
