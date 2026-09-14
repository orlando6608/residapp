using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Contra la instancia real de SQL Server. AUX-06 a AUX-12 "Registrar cambio", tercera acción de
/// cierre cotidiano, sobre dbo.cierres_cotidianos_residente + dbo.cierres_cotidianos_cambio_areas.</summary>
public class SqlDailyClosureRepositoryChangeTests
{
    private readonly SqlDailyClosureRepository _repository = new(TestDatabase.ConnectionFactory);
    private readonly SqlResidentRepository _residents = new(TestDatabase.ConnectionFactory);

    [Fact]
    public async Task RegisterChangeAsync_Ordinario_Succeeds()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cambio Ordinario", new DateOnly(1951, 1, 11), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyChangeInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.DolorMalestar, [], "Se queja de dolor en rodilla derecha")],
            37.2m, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid());

        var result = await _repository.RegisterChangeAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var closure = await connection.QuerySingleAsync<(string TipoCodigo, string ClasificacionCodigo, decimal? Temperatura)>(
            "SELECT tipo_codigo AS TipoCodigo, clasificacion_codigo AS ClasificacionCodigo, temperatura_celsius AS Temperatura FROM dbo.cierres_cotidianos_residente WHERE id = @Id",
            new { Id = result.ClosureId });
        Assert.Equal("CAMBIO_ENVIADO", closure.TipoCodigo);
        Assert.Equal("ORDINARIO", closure.ClasificacionCodigo);
        Assert.Equal(37.2m, closure.Temperatura);

        var areaCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id = @Id", new { Id = result.ClosureId });
        Assert.Equal(1, areaCount);
    }

    [Fact]
    public async Task RegisterChangeAsync_ConOpcionesRapidas_PersistsOptionsAndAllowsNullFreeText()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cambio Opciones Rápidas", new DateOnly(1951, 5, 15), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));

        // AUX-07: área con checklist, sin texto libre — las opciones rápidas ya son el contenido.
        var input = new RegisterDailyChangeInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(
                DailyChangeAreaCode.AlimentacionHidratacion,
                [DailyChangeAreaOptionCode.RechazaIngesta, DailyChangeAreaOptionCode.Atragantamiento], null)],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid());

        var result = await _repository.RegisterChangeAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var area = await connection.QuerySingleAsync<(Guid Id, string? TextoLibre)>(
            "SELECT id AS Id, texto_libre AS TextoLibre FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id = @Id",
            new { Id = result.ClosureId });
        Assert.Null(area.TextoLibre);

        var opciones = (await connection.QueryAsync<string>(
            "SELECT opcion_codigo FROM dbo.cierres_cotidianos_cambio_area_opciones WHERE area_id = @AreaId ORDER BY opcion_codigo",
            new { AreaId = area.Id })).ToList();
        Assert.Equal(["ATRAGANTAMIENTO", "RECHAZA_INGESTA"], opciones);
    }

    [Fact]
    public async Task RegisterChangeAsync_Prioritario_PersistsReasonAndDirectNotice()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cambio Prioritario", new DateOnly(1951, 2, 12), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyChangeInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            [
                new DailyChangeAreaInput(DailyChangeAreaCode.IncidenciasCaidas, [], "Caída en el baño a las 10:00"),
                new DailyChangeAreaInput(DailyChangeAreaCode.EstadoConciencia, [], "Algo desorientada tras la caída"),
            ],
            null, DailyChangeClassification.Prioritario, DailyChangePriorityReason.CaidaLesionTraumatismo,
            "Avisado el enfermero de guardia por teléfono a las 10:05", Guid.NewGuid());

        var result = await _repository.RegisterChangeAsync(input);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var closure = await connection.QuerySingleAsync<(string MotivoPrioritario, string AvisoDirecto)>(
            "SELECT motivo_prioritario_codigo AS MotivoPrioritario, aviso_directo_documentado AS AvisoDirecto FROM dbo.cierres_cotidianos_residente WHERE id = @Id",
            new { Id = result.ClosureId });
        Assert.Equal("CAIDA_LESION_TRAUMATISMO", closure.MotivoPrioritario);
        Assert.Equal("Avisado el enfermero de guardia por teléfono a las 10:05", closure.AvisoDirecto);

        var areaCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.cierres_cotidianos_cambio_areas WHERE cierre_id = @Id", new { Id = result.ClosureId });
        Assert.Equal(2, areaCount);
    }

    [Fact]
    public async Task RegisterChangeAsync_SameOperationIdTwice_DoesNotDuplicate()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cambio Idempotente", new DateOnly(1951, 3, 13), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));
        var operationId = Guid.NewGuid();
        var input = new RegisterDailyChangeInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.Sueno, [], "Durmió mal, se despertó varias veces")],
            null, DailyChangeClassification.Ordinario, null, null, operationId);

        var first = await _repository.RegisterChangeAsync(input);
        var second = await _repository.RegisterChangeAsync(input);

        Assert.Equal(first.ClosureId, second.ClosureId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.cierres_cotidianos_residente WHERE residente_id = @Id AND tipo_codigo = 'CAMBIO_ENVIADO'",
            new { Id = resident.ResidentId.Value });
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RegisterChangeAsync_PrioritarioSinAvisoDirecto_ViolaElCheckDeDefensaEnProfundidad()
    {
        // RegisterDailyChange (capa de aplicación) ya exige el aviso directo antes de llegar aquí; este
        // test comprueba que, si algo la saltara, el propio esquema SQL lo rechaza igual (CK_ccr_priority).
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cambio Sin Aviso", new DateOnly(1951, 4, 14), DocumentedSexCode.OtraCategoriaDocumentada, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyChangeInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.EstadoConciencia, [], "Dificultad para respirar tras el almuerzo")],
            null, DailyChangeClassification.Prioritario, DailyChangePriorityReason.DificultadRespiratoria, null, Guid.NewGuid());

        await Assert.ThrowsAsync<SqlException>(() => _repository.RegisterChangeAsync(input));
    }

    [Fact]
    public async Task RegisterChangeAsync_AreaSinChecklistSinTexto_ViolaElCheckDeDefensaEnProfundidad()
    {
        // RegisterDailyChange (capa de aplicación) ya exige texto en las áreas sin checklist (AUX-07:
        // participación/relación social, incidencias/caídas, estado de conciencia) antes de llegar aquí;
        // este test comprueba que, si algo la saltara, el propio esquema SQL lo rechaza igual (CK_ccca_text).
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Cambio Sin Texto", new DateOnly(1951, 6, 16), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var input = new RegisterDailyChangeInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            [new DailyChangeAreaInput(DailyChangeAreaCode.IncidenciasCaidas, [], null)],
            null, DailyChangeClassification.Ordinario, null, null, Guid.NewGuid());

        await Assert.ThrowsAsync<SqlException>(() => _repository.RegisterChangeAsync(input));
    }
}
