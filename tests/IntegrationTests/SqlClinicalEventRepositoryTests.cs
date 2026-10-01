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

    [Fact]
    public async Task RegisterAsync_Medicina_NaceEnValoracionMedica_ConSuAutoria_SinValoracionDeEnfermeria()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Medicina", new DateOnly(1946, 6, 16), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));

        var result = await _repository.RegisterAsync(new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Soplo sistólico no conocido en la exploración.", DailyChangeClassification.Ordinario, null, Guid.NewGuid(), SystemProfile.Medicina));

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(string Perfil, string Origen, string Estado, Guid? IniciadaMedica, Guid? IniciadaEnfermeria, string Auditoria)>("""
            SELECT ec.registrado_por_perfil AS Perfil, ea.origen_codigo AS Origen, ea.estado_codigo AS Estado,
                   ea.valoracion_medica_iniciada_por_cuenta_id AS IniciadaMedica, ea.valoracion_iniciada_por_cuenta_id AS IniciadaEnfermeria,
                   audit.perfil_activo AS Auditoria
              FROM dbo.eventos_clinicos ec
              JOIN dbo.eventos_asistenciales ea ON ea.id = ec.id
              JOIN dbo.eventos_auditoria audit WITH (NOLOCK) ON audit.recurso_id = ec.id AND audit.accion_codigo = 'CLINICAL_EVENT_REGISTER'
             WHERE ec.id = @Id
            """, new { Id = result.EventId });
        Assert.Equal("MEDICINA", row.Perfil);
        Assert.Equal("EVENTO_MEDICINA", row.Origen);
        Assert.Equal("EN_VALORACION_MEDICA", row.Estado);
        Assert.Equal(seed.AccountId.Value, row.IniciadaMedica);
        Assert.Null(row.IniciadaEnfermeria);
        Assert.Equal("MEDICINA", row.Auditoria);
    }

    [Fact]
    public async Task EventoDeMedicina_NuncaEntraEnLosEstadosDeEnfermeria()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Medicina Estados", new DateOnly(1947, 7, 17), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var result = await _repository.RegisterAsync(new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Edemas maleolares nuevos.", DailyChangeClassification.Ordinario, null, Guid.NewGuid(), SystemProfile.Medicina));

        // CK_ea_inicio (0017) salta antes que TR_ea_transition_guard.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'PENDIENTE', revision = revision + 1 WHERE id = @Id",
            "UPDATE dbo.eventos_asistenciales SET estado_codigo = 'EN_VALORACION', revision = revision + 1, valoracion_iniciada_por_cuenta_id = @AccountId, valoracion_iniciada_en = SYSUTCDATETIME() WHERE id = @Id",
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { Id = result.EventId, AccountId = seed.AccountId.Value }));
            Assert.Contains("\"CK_ea_inicio\"", error.Message);
        }
    }

    [Fact]
    public async Task RegisterAsync_GuardaSuContexto_QueNoSePuedeModificar()
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Contexto", new DateOnly(1949, 9, 19), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var result = await _repository.RegisterAsync(new RegisterClinicalEventInput(
            seed.AccountId, seed.CenterId, seed.UnitId, resident.ResidentId,
            "Mareo al levantarse.", DailyChangeClassification.Ordinario, null, Guid.NewGuid()));

        // HIS-03 (0019): ubicación vigente al registrarse; el residente aún no tiene basal firmado.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var context = await connection.QuerySingleAsync<(Guid? Version, Guid? Interval)>(
            "SELECT version_basal_id AS Version, intervalo_ubicacion_id AS Interval FROM dbo.eventos_contexto WHERE evento_id = @Id",
            new { Id = result.EventId });
        Assert.Null(context.Version);
        Assert.Equal(await connection.QuerySingleAsync<Guid>(
            "SELECT id FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @Id AND vigente_hasta IS NULL",
            new { Id = resident.ResidentId.Value }), context.Interval);

        foreach (var sql in new[]
        {
            "UPDATE dbo.eventos_contexto SET version_basal_id = NULL WHERE evento_id = @Id",
            "DELETE FROM dbo.eventos_contexto WHERE evento_id = @Id",
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { Id = result.EventId }));
            Assert.Contains("CLINICAL_EVENT_CONTEXT_IMMUTABLE", error.Message);
        }
    }

    [Theory]
    [InlineData("ENFERMERIA", "EVENTO_MEDICINA", "EN_VALORACION_MEDICA")]
    [InlineData("MEDICINA", "EVENTO_ENFERMERIA", "PENDIENTE")]
    public async Task OrigenDistintoDelPerfilQueRegistra_LoRechazaLaClaveForanea(string perfil, string origen, string estado)
    {
        var seed = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await _residents.CreateWithInitialLocationAsync(new CreateResidentInput(
            seed.AccountId, SystemProfile.Administracion, seed.CenterId, seed.UnitId,
            "Residente Evento Origen Cruzado", new DateOnly(1948, 8, 18), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        var parameters = new
        {
            Id = Guid.NewGuid(), ResidentId = resident.ResidentId.Value, CenterId = seed.CenterId.Value, UnitId = seed.UnitId.Value,
            AccountId = seed.AccountId.Value, Perfil = perfil, Origen = origen, Estado = estado,
            Medico = estado == "EN_VALORACION_MEDICA",
        };

        // El resto de columnas es válido, para que salte la clave foránea (0018) y no un CHECK.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.eventos_clinicos
                (id, residente_id, centro_id, unidad_id, observacion, clasificacion_codigo, registrado_por_cuenta_id, registrado_por_perfil, ocurrido_en)
            VALUES (@Id, @ResidentId, @CenterId, @UnitId, 'Observación con origen cruzado.', 'ORDINARIO', @AccountId, @Perfil, SYSUTCDATETIME())
            """, parameters);
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.eventos_asistenciales
                (id, residente_id, centro_id, unidad_id, origen_codigo, evento_clinico_id, clasificacion_codigo, estado_codigo,
                 valoracion_medica_iniciada_por_cuenta_id, valoracion_medica_iniciada_en, recibido_en)
            VALUES (@Id, @ResidentId, @CenterId, @UnitId, @Origen, @Id, 'ORDINARIO', @Estado,
                    CASE WHEN @Medico = 1 THEN @AccountId END, CASE WHEN @Medico = 1 THEN SYSUTCDATETIME() END, SYSUTCDATETIME())
            """, parameters));
        Assert.Contains("\"FK_ea_evento_clinico_perfil\"", error.Message);
    }
}
