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
/// Contra la instancia real de SQL Server. Caminos que no requieren un borrador previo válido: ausencia de
/// borrador y "auditoría o nada" sin basal firmado. El camino de éxito (firma y lectura de Dirección sobre
/// un basal firmado) está en SqlBaselineRepositoryDraftTests.FullCycle_*.
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
    public async Task ReadAsClinicalDirectionAsync_WhenNoBaselineVersionExists_ReturnsEmpty_WithoutAudit()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Sin Basal Firmado", new DateOnly(1945, 6, 1), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var directionSeed = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, adminSeed.CenterId, adminSeed.UnitId, [ResidentBaselinePermission.ClinicalDetailRead.ToCode()]);

        var input = new ClinicalDirectionReadInput(
            directionSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            ClinicalResourceType.BaselineHistory, ClinicalDetailAccessPurpose.SupervisionClinica, Guid.NewGuid());

        // Lector autorizado: la pantalla puede decir que no hay basal en vez de denegar. Sin contenido clínico leído, no
        // se audita nada; un reintento con la misma operación devuelve lo mismo.
        var headers = await _repository.ReadAsClinicalDirectionAsync(input);
        var retry = await _repository.ReadAsClinicalDirectionAsync(input);

        Assert.Empty(headers);
        Assert.Empty(retry);
        Assert.Equal(0, await ClinicalReadAuditCountAsync(resident.ResidentId));
    }

    [Fact]
    public async Task ReadAsClinicalDirectionAsync_WithoutPermission_ThrowsNotAuthorized_AuditOrNothing()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Sin Basal Ni Permiso", new DateOnly(1945, 6, 1), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        // Sin permiso CLINICAL_DETAIL_READ: se deniega igual que siempre, sin revelar que el residente no tiene basal.
        var directionSeed = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, adminSeed.CenterId, adminSeed.UnitId);

        var operationId = Guid.NewGuid();
        var input = new ClinicalDirectionReadInput(
            directionSeed.AccountId, adminSeed.CenterId, adminSeed.UnitId, resident.ResidentId,
            ClinicalResourceType.BaselineHistory, ClinicalDetailAccessPurpose.SupervisionClinica, operationId);

        var ex = await Assert.ThrowsAsync<SqlException>(() => _repository.ReadAsClinicalDirectionAsync(input));
        Assert.Contains("CLINICAL_DETAIL_READ_NOT_AUTHORIZED", ex.Message);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(0, await ClinicalReadAuditCountAsync(resident.ResidentId));

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

    [Fact]
    public async Task ReadCurrentSummaryAsync_WhenNoBaselineVersionExists_ReturnsNull()
    {
        var adminSeed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            adminSeed.AccountId, SystemProfile.Administracion, adminSeed.CenterId, adminSeed.UnitId,
            "Residente Sin Basal Para Auxiliar", new DateOnly(1946, 2, 2), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));

        var summary = await _repository.ReadCurrentSummaryAsync(
            new ReadCurrentBaselineSummaryInput(adminSeed.CenterId, resident.ResidentId));

        Assert.Null(summary);
    }

    private static async Task<int> ClinicalReadAuditCountAsync(ResidentId residentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND accion_codigo = 'CLINICAL_DETAIL_READ'",
            new { Id = residentId.Value });
    }
}
