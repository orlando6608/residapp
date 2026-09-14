using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// Contra la instancia real de SQL Server. No hay todavía, ni en este puerto ni en el prototipo legado, un
/// caso de uso para crear el contenido de un borrador de basal (9 áreas + Barthel) — ver
/// src/ResidApp.Web/Controllers/BaselineController.cs — así que aquí solo se cubren los caminos que no
/// requieren un borrador previo válido: ausencia de borrador y "auditoría o nada" sin basal firmado.
/// </summary>
public class SqlBaselineRepositoryTests
{
    private readonly SqlBaselineRepository _repository = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task SignDraftAsync_WhenDraftDoesNotExist_ThrowsNotAuthorized()
    {
        var seed = await SeedFixture.CreateProfileAsync(
            SystemProfile.Enfermeria, [ResidentBaselinePermission.BaselineInitialComplete.ToCode()]);
        var input = new SignBaselineDraftInput(
            seed.AccountId, SystemProfile.Enfermeria, seed.CenterId, seed.UnitId,
            ResidentId.New(), BaselineDraftId.New(), 1, Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => _repository.SignDraftAsync(input));
        Assert.Equal("BASELINE_SIGN_NOT_AUTHORIZED", ex.Code);
    }

    [Fact]
    public async Task ReadAsClinicalDirectionAsync_WhenNoBaselineVersionExists_ThrowsNotAuthorized_AuditOrNothing()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Sin Basal Firmado", new DateOnly(1945, 6, 1), DocumentedSexCode.Male, null, null, null, null, null, Guid.NewGuid()));

        var directionSeed = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, adminSeed.CenterId, adminSeed.UnitId, [ResidentBaselinePermission.ClinicalDetailRead.ToCode()]);

        var operationId = Guid.NewGuid();
        var input = new ClinicalDirectionReadInput(
            directionSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            ClinicalResourceType.BaselineHistory, ClinicalDetailAccessPurpose.SupervisionClinica, operationId);

        var ex = await Assert.ThrowsAsync<SqlException>(() => _repository.ReadAsClinicalDirectionAsync(input));
        Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", ex.Message);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var auditCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @Id AND accion_codigo = 'CLINICAL_DETAIL_READ'",
            new { Id = resident.ResidentId.Value });
        Assert.Equal(0, auditCount);

        // Un intento fallido no debe quedar cacheado como idempotencia: la transacción completa
        // (incluida la fila IN_PROGRESS) se revierte, así que un reintento con el mismo operationId
        // vuelve a fallar igual, no devuelve un resultado inventado.
        var idempotencyCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.operaciones_idempotencia WHERE cuenta_id = @AccountId AND operacion_id = @OperationId",
            new { AccountId = directionSeed.AccountId.Value, OperationId = operationId });
        Assert.Equal(0, idempotencyCount);

        var retryEx = await Assert.ThrowsAsync<SqlException>(() => _repository.ReadAsClinicalDirectionAsync(input));
        Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", retryEx.Message);
    }
}
