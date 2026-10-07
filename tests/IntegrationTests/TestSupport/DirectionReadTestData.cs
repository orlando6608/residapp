using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>La entrada de la lectura auditada de Dirección Clínica con la finalidad, la justificación y la duración de la
/// declaración de acceso (CJ, 2026-10-06) por defecto, para los tests que solo quieren leer.</summary>
internal static class DirectionReadTestData
{
    public const string Justification = "Revisión de los hechos tras una reclamación familiar (prueba).";

    public static ClinicalDirectionReadInput Input(
        SeededProfile direction, CenterId centerId, UnitId unitId, ResidentId residentId, ClinicalResourceType resourceType,
        Guid operationId, Guid? reuseDeclarationId = null, ClinicalDetailAccessPurpose purpose = ClinicalDetailAccessPurpose.ContinuidadAsistencial,
        string justification = Justification, int declarationMinutes = 60) =>
        new(direction.AccountId, centerId, unitId, residentId, resourceType, purpose, operationId,
            direction.ProfileScopeId, justification, reuseDeclarationId, declarationMinutes);
}
