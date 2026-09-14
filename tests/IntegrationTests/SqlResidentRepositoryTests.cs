using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server (ver TestDatabase): cierra el pendiente crítico
/// "SqlResidentRepository ... hoy solo están verificados por lectura, no ejecutados" de
/// docs/tareas/alta-prioridad/pendientes-migracion-inicial.md.</summary>
public class SqlResidentRepositoryTests
{
    private readonly SqlResidentRepository _repository = new(TestDatabase.ConnectionFactory);

    private static CreateResidentInput Input(SeededProfile seed, Guid operationId, string displayName = "Residente de Prueba") =>
        new(seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId, displayName,
            new DateOnly(1940, 1, 1), DocumentedSexCode.Mujer, "EXP-TEST", null, null, null, null, operationId);

    [Fact]
    public async Task CreateWithInitialLocationAsync_PersistsResidentAndAuditEvent()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var result = await _repository.CreateWithInitialLocationAsync(Input(seed, Guid.NewGuid()));

        Assert.NotEqual(Guid.Empty, result.ResidentId.Value);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var status = await connection.QuerySingleAsync<string>(
            "SELECT estado FROM dbo.residentes WHERE id = @Id", new { Id = result.ResidentId.Value });
        Assert.Equal("ACTIVE", status);

        var auditCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @Id AND accion_codigo = 'RESIDENT_CREATE'",
            new { Id = result.ResidentId.Value });
        Assert.Equal(1, auditCount);
    }

    [Fact]
    public async Task CreateWithInitialLocationAsync_CalledTwiceWithSameOperationId_ReturnsCachedResultWithoutDuplicating()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var input = Input(seed, Guid.NewGuid());

        var first = await _repository.CreateWithInitialLocationAsync(input);
        var second = await _repository.CreateWithInitialLocationAsync(input);

        Assert.Equal(first.ResidentId, second.ResidentId);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes WHERE id = @Id", new { Id = first.ResidentId.Value });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateWithInitialLocationAsync_SameOperationIdDifferentPayload_ThrowsIdempotencyKeyReused()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var operationId = Guid.NewGuid();
        await _repository.CreateWithInitialLocationAsync(Input(seed, operationId, "Primera Persona"));

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => _repository.CreateWithInitialLocationAsync(Input(seed, operationId, "Otra Persona Distinta")));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", ex.Code);
    }
}
