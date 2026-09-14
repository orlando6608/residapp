using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. ENF-16 "Registrar un evento observado por
/// Enfermería", en su alcance mínimo: solo registrar y guardar, mismo patrón de idempotencia que
/// SqlDailyClosureRepository.</summary>
public class SqlClinicalEventRepositoryTests
{
    private readonly SqlClinicalEventRepository _repository = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task RegisterAsync_Ordinario_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Repositorio", new DateOnly(1951, 1, 11), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Se observa tos persistente sin fiebre durante la tarde.", DailyChangeClassification.Ordinario, null, Guid.NewGuid());
        var result = await _repository.RegisterAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(string EstadoCodigo, string ClasificacionCodigo, string Perfil, string? Datos)>(
            "SELECT estado_codigo AS EstadoCodigo, clasificacion_codigo AS ClasificacionCodigo, " +
            "registrado_por_perfil AS Perfil, datos_clinicos_pertinentes AS Datos FROM dbo.eventos_clinicos WHERE id = @Id",
            new { Id = result.EventId });
        Assert.Equal("PENDIENTE", row.EstadoCodigo);
        Assert.Equal("ORDINARIO", row.ClasificacionCodigo);
        Assert.Equal("ENFERMERIA", row.Perfil);
        Assert.Null(row.Datos);
    }

    [Fact]
    public async Task RegisterAsync_PrioritarioConDatosClinicos_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Prioritario", new DateOnly(1952, 2, 12), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Caída sin testigos en el pasillo, refiere dolor en cadera derecha.",
            DailyChangeClassification.Prioritario, "TA 130/80, FC 88, afebril.", Guid.NewGuid());
        var result = await _repository.RegisterAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var datos = await connection.QuerySingleAsync<string>(
            "SELECT datos_clinicos_pertinentes FROM dbo.eventos_clinicos WHERE id = @Id", new { Id = result.EventId });
        Assert.Equal("TA 130/80, FC 88, afebril.", datos);
    }

    [Fact]
    public async Task RegisterAsync_SameOperationIdTwice_DoesNotDuplicate()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Idempotente", new DateOnly(1953, 3, 13), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));
        var operationId = Guid.NewGuid();
        var input = new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Observación repetida por reintento de red.", DailyChangeClassification.Ordinario, null, operationId);

        var first = await _repository.RegisterAsync(input);
        var second = await _repository.RegisterAsync(input);

        Assert.Equal(first.EventId, second.EventId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_clinicos WHERE residente_id = @Id", new { Id = resident.ResidentId.Value });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RegisterAsync_ObservacionVacia_ViolaElCheckDeDefensaEnProfundidad()
    {
        // RegisterClinicalEvent (capa de aplicación) ya exige la observación antes de llegar aquí; este
        // test comprueba que, si algo la saltara, el propio esquema SQL lo rechaza igual (CK_ec_observacion)
        // — mismo criterio de "defensa en profundidad" que el resto del esquema.
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Sin Observación", new DateOnly(1954, 4, 14), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId, "   ", DailyChangeClassification.Ordinario, null, Guid.NewGuid());

        await Assert.ThrowsAsync<SqlException>(() => _repository.RegisterAsync(input));
    }

    [Fact]
    public async Task RegisterAsync_ThenUpdate_IsRejectedByImmutabilityTrigger()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Inmutable", new DateOnly(1955, 5, 15), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var input = new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Observación original que no debe poder editarse.", DailyChangeClassification.Ordinario, null, Guid.NewGuid());
        var result = await _repository.RegisterAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_clinicos SET observacion = 'Editado' WHERE id = @Id", new { Id = result.EventId }));
        Assert.Contains("CLINICAL_EVENT_IMMUTABLE", error.Message);
    }
}
