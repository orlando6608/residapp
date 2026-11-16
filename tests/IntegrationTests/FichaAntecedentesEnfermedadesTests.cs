using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Ficha de antecedentes de salud, fase 2: enfermedades e intervenciones (script 0096; docs/producto/ficha-antecedentes-salud-diseno.md). Reutiliza las tablas, el
/// permiso y la visibilidad de las alergias (FichaAntecedentesAlergiasTests); aquí se prueba lo propio: el año opcional, los estados sin elementos, el tipo por apartado, los
/// límites de la BD (revisión del DPO, 2026-11-16) y que un apartado ilegible no oculte al otro. Las enfermedades de las pruebas son inventadas.</summary>
public class FichaAntecedentesEnfermedadesTests
{
    private static HealthBackgroundApplicationService Service(SeededProfile profile) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedConditionsSessionIdentityProvider(profile.ExternalSubject),
        new SqlHealthBackgroundRepository(TestDatabase.ConnectionFactory));

    private static Task<SeededProfile> WithPermissionAsync(SystemProfile profile, SeededProfile admin) =>
        SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId, [ProfilePermissions.HealthBackgroundEdit]);

    private static Task<ApplicationResult<HealthBackgroundView>> ReadAsync(SeededProfile profile, ResidentId residentId) =>
        Service(profile).ReadAsync(profile.ProfileScopeId, profile.CenterId, residentId);

    private static Task<ApplicationResult<int>> SaveAsync(
        SeededProfile profile, ResidentId residentId, int seenVersion, ConditionRecordContent content,
        HealthBackgroundReason? reason = HealthBackgroundReason.Actualizacion) =>
        Service(profile).SaveConditionsAsync(new SaveConditionsCommand(profile.ProfileScopeId, profile.CenterId, residentId, seenVersion, reason, content));

    private static ConditionRecordContent ConLista() => new(
        HealthBackgroundState.ConEntradas,
        [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", null), new ConditionItem(ConditionItemType.Intervencion, "Intervención Beta", 1999)],
        HealthBackgroundSource.InformeMedico);

    private static ConditionRecordContent SinEnfermedades() => new(HealthBackgroundState.NoRefiere, [], null);

    [Fact]
    public async Task SinNingunaVersion_ElApartadoEstaPendiente_YLoVenEnfermeriaYMedicina_PeroNoAuxiliarNiOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria, [ProfilePermissions.HealthBackgroundEdit]);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Pendiente");

        var byNurse = (await ReadAsync(nurse, residentId)).Value!;
        var byDoctor = (await ReadAsync(doctor, residentId)).Value!;

        Assert.Null(byNurse.Conditions);
        Assert.Null(byNurse.UnreliableConditionsVersion);
        Assert.False(byNurse.CanEdit);
        Assert.Null(byDoctor.Conditions);
        Assert.True(byDoctor.CanEdit);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(auxiliar, residentId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(otherCenter).ReadAsync(otherCenter.ProfileScopeId, admin.CenterId, residentId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(otherCenter, residentId, 0, ConLista(), null)).Error?.Code);
    }

    [Fact]
    public async Task EnfermeriaLaRellena_MedicinaLaCambia_ElAnioOpcionalSeConservaYCadaGuardadoEsUnaVersionNuevaConSuAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Versiones");

        var first = await SaveAsync(nurse, residentId, 0, ConLista(), null);
        var second = await SaveAsync(doctor, residentId, 1, SinEnfermedades());

        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        var afterSecond = (await ReadAsync(nurse, residentId)).Value!.Conditions!;
        Assert.Equal(2, afterSecond.Version);
        Assert.Equal(SystemProfile.Medicina, afterSecond.ModifiedByProfile);
        Assert.Equal(HealthBackgroundState.NoRefiere, afterSecond.Content.State);
        Assert.Empty(afterSecond.Content.Items);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // La versión 1 sigue guardada con sus dos elementos, en su orden y con el año nulo o con valor.
        var items = (await connection.QueryAsync<(string Type, string Name, int? Year)>("""
            SELECT e.tipo_codigo, e.nombre, e.anio FROM dbo.fichas_antecedentes_elementos e
              JOIN dbo.fichas_antecedentes_versiones v ON v.id = e.version_id
             WHERE v.residente_id = @Id AND v.apartado_codigo = 'ENFERMEDADES' AND v.numero_version = 1 ORDER BY e.posicion
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal([("ENFERMEDAD", "Enfermedad Alfa", (int?)null), ("INTERVENCION", "Intervención Beta", 1999)], items);
        var audit = (await connection.QueryAsync<(string Action, string Resource, string Profile)>("""
            SELECT accion_codigo, tipo_recurso, perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND' ORDER BY ocurrido_en
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal([("HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND", "ENFERMERIA"), ("HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND", "MEDICINA")], audit);

        // Y se lee tal cual se guardó la primera vez (con el año nulo y el de 1999), guardando otra con lista para volver a verla.
        var third = await SaveAsync(nurse, residentId, 2, ConLista());
        var again = (await ReadAsync(nurse, residentId)).Value!.Conditions!;
        Assert.Equal(3, third.Value);
        Assert.Equal([null, (int?)1999], again.Content.Items.Select(i => i.Year).ToArray());
        Assert.Equal(HealthBackgroundSource.InformeMedico, again.Content.Source);
    }

    [Fact]
    public async Task LosDosApartadosSeVersionanPorSeparado_YNoSeMezclan()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Aparte");

        var conditions = await SaveAsync(nurse, residentId, 0, ConLista(), null);
        var allergies = await Service(nurse).SaveAllergiesAsync(new SaveAllergiesCommand(
            nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null,
            new AllergyRecordContent(HealthBackgroundState.NoRefiere, [], null)));

        // Cada apartado empieza en su versión 1: el número de versión que vio quien edita es el de su apartado.
        Assert.Equal(1, conditions.Value);
        Assert.Equal(1, allergies.Value);
        var view = (await ReadAsync(nurse, residentId)).Value!;
        Assert.Equal(1, view.Conditions!.Version);
        Assert.Equal(2, view.Conditions.Content.Items.Count);
        Assert.Equal(1, view.Allergies!.Version);
        Assert.Empty(view.Allergies.Content.Items);
    }

    [Fact]
    public async Task SinElPermisoDeEdicion_NoPuedeGuardar_YUnaVersionVistaQueYaNoEsLaVigenteDaConflicto_SinPisarNada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var withoutPermission = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Conflicto");

        var denied = await SaveAsync(withoutPermission, residentId, 0, ConLista(), null);
        var first = await SaveAsync(nurse, residentId, 0, ConLista(), null);
        var stale = await SaveAsync(nurse, residentId, 0, SinEnfermedades(), null);
        var future = await SaveAsync(nurse, residentId, 5, SinEnfermedades());

        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error?.Code);
        Assert.Equal(1, first.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error?.Code);
        Assert.False(future.Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id", new { Id = residentId.Value }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'", new { Id = residentId.Value }));
    }

    [Fact]
    public async Task DosGuardadosAlMismoTiempoSobreLaMismaVersion_UnoSeGuarda_YElOtroDaConflicto()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Paralela");

        var results = await Task.WhenAll(SaveAsync(nurse, residentId, 0, ConLista(), null), SaveAsync(doctor, residentId, 0, SinEnfermedades(), null));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, results.Count(r => r.Error?.Code == ApplicationFailureCode.Conflict));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal([1], (await connection.QueryAsync<int>(
            "SELECT numero_version FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id ORDER BY numero_version", new { Id = residentId.Value })).ToList());
    }

    [Fact]
    public async Task UnaVersionSinTodosSusElementos_NoSeEnsenaComoValida_YNoOcultaLasAlergias()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Incompleta");
        var allergies = await Service(nurse).SaveAllergiesAsync(new SaveAllergiesCommand(
            nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null,
            new AllergyRecordContent(HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Gamma", null)], null)));
        Assert.True(allergies.Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
            // Una versión que declara dos elementos y solo tiene uno: la BD no puede impedirlo, la lectura sí lo detecta.
            var versionId = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, versionId, residentId, nurse, "CON_ENTRADAS", 2);
            await InsertItemAsync(connection, versionId, residentId, admin.CenterId, 1, "ENFERMEDAD", "Enfermedad Delta", null, transaction);
            transaction.Commit();
        }

        var read = (await ReadAsync(nurse, residentId)).Value!;

        Assert.Null(read.Conditions);
        Assert.Equal(1, read.UnreliableConditionsVersion);
        Assert.Null(read.UnreliableVersion);
        Assert.Equal("Fármaco Gamma", Assert.Single(read.Allergies!.Content.Items).Name);
        var wrongSeen = await SaveAsync(nurse, residentId, 0, ConLista(), null);
        var corrected = await SaveAsync(nurse, residentId, 1, ConLista(), HealthBackgroundReason.Correccion);
        var after = (await ReadAsync(nurse, residentId)).Value!;
        Assert.Equal(ApplicationFailureCode.Conflict, wrongSeen.Error?.Code);
        Assert.Equal(2, corrected.Value);
        Assert.Null(after.UnreliableConditionsVersion);
        Assert.Equal(2, after.Conditions!.Version);
        Assert.Contains("HEALTH_BACKGROUND_CORRECT", await connection.QueryAsync<string>(
            "SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'", new { Id = residentId.Value }));
    }

    [Fact]
    public async Task LimitesDeLaBaseDeDatos_ApartadoTopeEstadosSinElementosTipoAnioYNombre()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Limites");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        async Task<Exception> VersionFailsAsync(string section, string state, int count)
        {
            using var transaction = connection.BeginTransaction();
            return await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync("""
                INSERT INTO dbo.fichas_antecedentes_versiones
                    (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, motivo_codigo, creado_por_cuenta_id, creado_por_perfil, creado_en)
                VALUES (NEWID(), @CenterId, @ResidentId, @Section, 1, @State, @Count, 'REGISTRO_INICIAL', @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
                """, new { CenterId = admin.CenterId.Value, ResidentId = residentId.Value, Section = section, State = state, Count = count, AccountId = nurse.AccountId.Value }, transaction));
        }

        Assert.Contains("CK_fav_apartado", (await VersionFailsAsync("MEDICACION", "NO_REFIERE", 0)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("ENFERMEDADES", "CON_ENTRADAS", 0)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("ENFERMEDADES", "CON_ENTRADAS", 101)).Message);
        // En enfermedades, «No refiere» y «No consta» no admiten ningún elemento (las alergias sí admiten intolerancias).
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("ENFERMEDADES", "NO_REFIERE", 1)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("ENFERMEDADES", "NO_CONSTA", 1)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("ALERGIAS", "CON_ENTRADAS", 51)).Message);

        async Task<Exception> ItemFailsAsync(string section, string type, string name, string? reaction, int? year, int position = 1)
        {
            using var transaction = connection.BeginTransaction();
            var versionId = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, versionId, residentId, nurse, "CON_ENTRADAS", 2, section);
            return await Assert.ThrowsAnyAsync<Exception>(() => InsertItemAsync(connection, versionId, residentId, admin.CenterId, position, type, name, reaction, transaction, year));
        }

        Assert.Contains("CK_fae_anio", (await ItemFailsAsync("ALERGIAS", "ALERGIA", "Fármaco Eta", null, 2001)).Message);
        Assert.Contains("CK_fae_reaccion_tipo", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", "Enfermedad Zeta", "náuseas", null)).Message);
        Assert.Contains("CK_fae_anio", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", "Enfermedad Zeta", null, 1850)).Message);
        Assert.Contains("CK_fae_nombre_enfermedad", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", new string('a', 101), null, null)).Message);
        Assert.Contains("CK_fae_nombre_enfermedad", (await ItemFailsAsync("ENFERMEDADES", "INTERVENCION", "Primera\nlínea", null, null)).Message);
        Assert.Contains("CK_fae_nombre_enfermedad", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", "Primera\rlínea", null, null)).Message);
        Assert.Contains("CK_fae_posicion", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", "Enfermedad Zeta", null, null, position: 101)).Message);
        // Año futuro: lo rechaza el disparador (el año del día de mañana en UTC es el techo).
        var future = DateTime.UtcNow.Year + 2;
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", "Enfermedad Zeta", null, future)).Message);
        // El tipo debe ser el del apartado, en los dos sentidos.
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", (await ItemFailsAsync("ENFERMEDADES", "INTOLERANCIA", "Fármaco Eta", "picor", null)).Message);
    }

    [Fact]
    public async Task ElSqlRechazaUnElementoDeEnfermedadEnUnaVersionDeAlergiasYViceversa_YLosAnadidosDespues()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Tipos");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
            var allergiesVersion = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, allergiesVersion, residentId, nurse, "CON_ENTRADAS", 1, "ALERGIAS");
            var wrongType = await Assert.ThrowsAnyAsync<Exception>(() =>
                InsertItemAsync(connection, allergiesVersion, residentId, admin.CenterId, 1, "ENFERMEDAD", "Enfermedad Eta", null, transaction));
            Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", wrongType.Message);
        }
        var savedVersion = Guid.NewGuid();
        using (var transaction = connection.BeginTransaction())
        {
            await InsertVersionAsync(connection, transaction, savedVersion, residentId, nurse, "CON_ENTRADAS", 2);
            await InsertItemAsync(connection, savedVersion, residentId, admin.CenterId, 1, "ENFERMEDAD", "Enfermedad Eta", null, transaction, 2010);
            transaction.Commit();
        }
        // Otra transacción no puede rellenar el hueco de una versión ya cerrada.
        var late = await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertItemAsync(connection, savedVersion, residentId, admin.CenterId, 2, "INTERVENCION", "Intervención Theta", null, null, 2011));
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", late.Message);
        // Y nada se puede cambiar ni borrar.
        var update = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.fichas_antecedentes_elementos SET anio = 2012 WHERE version_id = @Id", new { Id = savedVersion }));
        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", update.Message);
    }

    [Fact]
    public async Task UnaListaDeCienElementos_SeGuardaYSeLeeEnOrden_YLaDeAlergiasSigueAdmitiendoCincuenta()
    {
        // El requisito central de CJ: «deben constar todas… no importa que crezca la lista». Cien es el tope técnico; las alergias conservan el suyo de 50 tras ampliar la tabla.
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Cien");
        var items = Enumerable.Range(1, ConditionRecordContent.MaxItems)
            .Select(i => new ConditionItem(i % 2 == 0 ? ConditionItemType.Enfermedad : ConditionItemType.Intervencion, $"Elemento {i}", i % 3 == 0 ? 1950 + i % 60 : null)).ToList();

        var saved = await SaveAsync(nurse, residentId, 0, new ConditionRecordContent(HealthBackgroundState.ConEntradas, items, null), null);
        var allergies = await Service(nurse).SaveAllergiesAsync(new SaveAllergiesCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null,
            new AllergyRecordContent(HealthBackgroundState.ConEntradas,
                Enumerable.Range(1, AllergyRecordContent.MaxItems).Select(i => new AllergyItem(AllergyItemType.Alergia, $"Fármaco {i}", null)).ToList(), null)));

        Assert.Equal(1, saved.Value);
        Assert.Equal(1, allergies.Value);
        var view = (await ReadAsync(nurse, residentId)).Value!;
        Assert.Null(view.UnreliableConditionsVersion);
        Assert.Equal(items.Select(i => (i.Type, i.Name, i.Year)), view.Conditions!.Content.Items.Select(i => (i.Type, i.Name, i.Year)));
        Assert.Equal(AllergyRecordContent.MaxItems, view.Allergies!.Content.Items.Count);
    }

    [Fact]
    public async Task UnApartadoDeAlergiasIlegible_NoOcultaLasEnfermedades()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Alergias Ilegibles");
        Assert.True((await SaveAsync(nurse, residentId, 0, ConLista(), null)).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
            // Una versión de alergias que declara dos elementos y no trae ninguno.
            await InsertVersionAsync(connection, transaction, Guid.NewGuid(), residentId, nurse, "CON_ENTRADAS", 2, "ALERGIAS");
            transaction.Commit();
        }

        var view = (await ReadAsync(nurse, residentId)).Value!;

        Assert.Null(view.Allergies);
        Assert.Equal(1, view.UnreliableVersion);
        Assert.Null(view.UnreliableConditionsVersion);
        Assert.Equal(2, view.Conditions!.Content.Items.Count);
    }

    [Fact]
    public async Task ElAnioTieneLimitesEnLaBaseDeDatos_1900SiYPorDebajoNo_PorEncimaDeLaCotaDelCheckNo_YElFuturoLoRechazaElDisparador()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Enfermedades Anios");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        async Task<Exception?> InsertWithYearAsync(int year)
        {
            using var transaction = connection.BeginTransaction();
            var versionId = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, versionId, residentId, nurse, "CON_ENTRADAS", 1);
            try
            {
                await InsertItemAsync(connection, versionId, residentId, admin.CenterId, 1, "ENFERMEDAD", "Enfermedad Alfa", null, transaction, year);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
            finally
            {
                transaction.Rollback();
            }
        }

        Assert.Contains("CK_fae_anio", (await InsertWithYearAsync(1899))!.Message);
        Assert.Null(await InsertWithYearAsync(1900));
        // Por encima de 2200 lo rechaza el CHECK; entre el año actual y 2200, el disparador (el techo es el año del día de mañana en UTC).
        Assert.Contains("CK_fae_anio", (await InsertWithYearAsync(2201))!.Message);
        Assert.NotNull(await InsertWithYearAsync(2200));
    }

    private static Task<int> InsertVersionAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, Guid versionId, ResidentId residentId, SeededProfile author,
        string state, int count, string section = "ENFERMEDADES") =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.fichas_antecedentes_versiones
                (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, motivo_codigo, creado_por_cuenta_id, creado_por_perfil, creado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Section, 1, @State, @Count, 'REGISTRO_INICIAL', @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
            """, new { Id = versionId, CenterId = author.CenterId.Value, ResidentId = residentId.Value, Section = section, State = state, Count = count, AccountId = author.AccountId.Value }, transaction);

    private static Task<int> InsertItemAsync(
        System.Data.Common.DbConnection connection, Guid versionId, ResidentId residentId, CenterId centerId, int position, string type, string name, string? reaction,
        System.Data.Common.DbTransaction? transaction = null, int? year = null) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.fichas_antecedentes_elementos (id, version_id, residente_id, centro_id, posicion, tipo_codigo, nombre, reaccion, anio)
            VALUES (NEWID(), @VersionId, @ResidentId, @CenterId, @Position, @Type, @Name, @Reaction, @Year)
            """, new
        {
            VersionId = versionId, ResidentId = residentId.Value, CenterId = centerId.Value, Position = position, Type = type, Name = name, Reaction = reaction, Year = year,
        }, transaction);
}

file sealed class FixedConditionsSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
