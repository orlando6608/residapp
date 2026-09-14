using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. AUX-04 (Sin cambios) y AUX-05 (No valorable), mismo
/// patrón de idempotencia que SqlResidentRepository/SqlBaselineRepository.</summary>
public class SqlDailyClosureRepositoryTests
{
    private readonly SqlDailyClosureRepository _repository = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task RegisterAsync_SinCambios_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cierre Sin Cambios", new DateOnly(1947, 7, 7), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyClosureInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId, DailyClosureType.SinCambios, null, Guid.NewGuid());
        var result = await _repository.RegisterAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(string TipoCodigo, string? Motivo)>(
            "SELECT tipo_codigo AS TipoCodigo, motivo_no_valorable AS Motivo FROM dbo.cierres_cotidianos_residente WHERE id = @Id",
            new { Id = result.ClosureId });
        Assert.Equal("SIN_CAMBIOS", row.TipoCodigo);
        Assert.Null(row.Motivo);
    }

    [Fact]
    public async Task RegisterAsync_NoValorableConMotivo_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cierre No Valorable", new DateOnly(1948, 8, 8), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyClosureInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId, DailyClosureType.NoValorable,
            "Residente ausente del centro durante el turno", Guid.NewGuid());
        var result = await _repository.RegisterAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var motivo = await connection.QuerySingleAsync<string>(
            "SELECT motivo_no_valorable FROM dbo.cierres_cotidianos_residente WHERE id = @Id", new { Id = result.ClosureId });
        Assert.Equal("Residente ausente del centro durante el turno", motivo);
    }

    [Fact]
    public async Task RegisterAsync_SameOperationIdTwice_DoesNotDuplicate()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cierre Idempotente", new DateOnly(1949, 9, 9), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));
        var operationId = Guid.NewGuid();
        var input = new RegisterDailyClosureInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId, DailyClosureType.SinCambios, null, operationId);

        var first = await _repository.RegisterAsync(input);
        var second = await _repository.RegisterAsync(input);

        Assert.Equal(first.ClosureId, second.ClosureId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.cierres_cotidianos_residente WHERE residente_id = @Id", new { Id = resident.ResidentId.Value });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RegisterAsync_NoValorableSinMotivo_ViolaElCheckDeDefensaEnProfundidad()
    {
        // RegisterDailyClosure (capa de aplicación) ya exige el motivo antes de llegar aquí; este test
        // comprueba que, si algo la saltara, el propio esquema SQL lo rechaza igual (CK_ccr_reason) —
        // mismo criterio de "defensa en profundidad" que el resto de docs/decisiones-arquitectura/
        // integridad-sql-basal-legado.md.
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cierre Sin Motivo", new DateOnly(1950, 10, 10), DocumentedSexCode.OtraCategoriaDocumentada, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyClosureInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId, DailyClosureType.NoValorable, null, Guid.NewGuid());

        await Assert.ThrowsAsync<SqlException>(() => _repository.RegisterAsync(input));
    }
}
