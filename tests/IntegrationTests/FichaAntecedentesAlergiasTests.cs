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

/// <summary>Ficha de antecedentes de salud, fase 1: alergias e intolerancias (script 0094; docs/producto/ficha-antecedentes-salud-diseno.md). La ven Enfermería y
/// Medicina en su ámbito y la cambian con el permiso HEALTH_BACKGROUND_EDIT; no la ven Auxiliar ni Administración ni otro centro. Cada guardado es una versión
/// nueva (no se pisa la anterior), con control de concurrencia y una fila de auditoría sin el texto de la ficha. «Pendiente» es no tener ninguna versión.
/// Los nombres de fármacos de las pruebas son inventados.</summary>
public class FichaAntecedentesAlergiasTests
{
    private static HealthBackgroundApplicationService Service(SeededProfile profile) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedHealthSessionIdentityProvider(profile.ExternalSubject),
        new SqlHealthBackgroundRepository(TestDatabase.ConnectionFactory));

    private static Task<SeededProfile> WithPermissionAsync(SystemProfile profile, SeededProfile admin) =>
        SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId, [ProfilePermissions.HealthBackgroundEdit]);

    private static Task<ApplicationResult<HealthBackgroundView>> ReadAsync(SeededProfile profile, ResidentId residentId) =>
        Service(profile).ReadAsync(profile.ProfileScopeId, profile.CenterId, residentId);

    private static Task<ApplicationResult<int>> SaveAsync(
        SeededProfile profile, ResidentId residentId, int seenVersion, AllergyRecordContent content,
        HealthBackgroundReason? reason = HealthBackgroundReason.Actualizacion) =>
        Service(profile).SaveAllergiesAsync(new SaveAllergiesCommand(profile.ProfileScopeId, profile.CenterId, residentId, seenVersion, reason, content));

    private static AllergyRecordContent NoRefiereConIntolerancia() => new(
        HealthBackgroundState.NoRefiere, [new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Alfa", "náuseas")], HealthBackgroundSource.Residente);

    private static AllergyRecordContent ConAlergia() => new(
        HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", null)], null);

    [Fact]
    public async Task SinNingunaVersion_ElApartadoEstaPendiente_YSoloLoVenEnfermeriaYMedicina()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Pendiente");

        var byNurse = await ReadAsync(nurse, residentId);
        var byDoctor = await ReadAsync(doctor, residentId);

        Assert.True(byNurse.Ok);
        Assert.Null(byNurse.Value!.Allergies);
        Assert.False(byNurse.Value.CanEdit);
        Assert.True(byDoctor.Ok);
        Assert.Null(byDoctor.Value!.Allergies);
        Assert.True(byDoctor.Value.CanEdit);
        var byAdmin = await ReadAsync(admin, residentId);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byAdmin.Error?.Code);
    }

    [Fact]
    public async Task Auxiliar_NoLaVe_NiLaVeOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var otherCenterNurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria, [ProfilePermissions.HealthBackgroundEdit]);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Ajena");

        var byAuxiliar = await ReadAsync(auxiliar, residentId);
        var crossCenter = await Service(otherCenterNurse).ReadAsync(otherCenterNurse.ProfileScopeId, admin.CenterId, residentId);
        var crossCenterSave = await SaveAsync(otherCenterNurse, residentId, 0, NoRefiereConIntolerancia(), null);

        Assert.Equal(ApplicationFailureCode.AccessDenied, byAuxiliar.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, crossCenter.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, crossCenterSave.Error?.Code);
    }

    [Fact]
    public async Task EnfermeriaConPermisoLaRellena_MedicinaLaCambia_YCadaGuardadoEsUnaVersionNuevaConSuAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Versiones");

        var first = await SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), reason: null);
        var second = await SaveAsync(doctor, residentId, 1, ConAlergia());

        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        var read = (await ReadAsync(nurse, residentId)).Value!;
        var record = read.Allergies!;
        Assert.Equal(2, record.Version);
        Assert.Equal(SystemProfile.Medicina, record.ModifiedByProfile);
        Assert.Equal(HealthBackgroundState.ConEntradas, record.Content.State);
        Assert.Equal("Fármaco Beta", Assert.Single(record.Content.Items).Name);
        Assert.True(record.ModifiedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // La versión 1 sigue guardada: no se pisa (Llei 21/2000, art. 9.3), aunque ninguna pantalla la enseñe.
        var versions = (await connection.QueryAsync<(int Number, string State, string Reason, string Profile)>("""
            SELECT numero_version, estado_codigo, motivo_codigo, creado_por_perfil FROM dbo.fichas_antecedentes_versiones
             WHERE residente_id = @Id ORDER BY numero_version
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal([(1, "NO_REFIERE", "REGISTRO_INICIAL", "ENFERMERIA"), (2, "CON_ENTRADAS", "ACTUALIZACION", "MEDICINA")], versions);
        var audit = (await connection.QueryAsync<(string Action, string Resource, string Profile)>("""
            SELECT accion_codigo, tipo_recurso, perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE residente_id = @Id AND accion_codigo = 'HEALTH_BACKGROUND_SAVE' ORDER BY ocurrido_en
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal([("HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND", "ENFERMERIA"), ("HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND", "MEDICINA")], audit);
    }

    [Fact]
    public async Task SinElPermisoDeEdicion_NoPuedeGuardar_NiAunqueLaVea()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Sin Permiso");

        var save = await SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), null);

        Assert.Equal(ApplicationFailureCode.AccessDenied, save.Error?.Code);
        Assert.Null((await ReadAsync(nurse, residentId)).Value!.Allergies);
    }

    [Fact]
    public async Task GuardarSobreUnaVersionQueYaNoEsLaVigente_DaConflicto_YNoPisaNada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Conflicto");
        Assert.True((await SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), null)).Ok);

        var staleInitial = await SaveAsync(doctor, residentId, 0, ConAlergia(), null);
        var ok = await SaveAsync(doctor, residentId, 1, ConAlergia());
        var stale = await SaveAsync(nurse, residentId, 1, NoRefiereConIntolerancia());

        Assert.Equal(ApplicationFailureCode.Conflict, staleInitial.Error?.Code);
        Assert.True(ok.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error?.Code);
        Assert.Equal(2, (await ReadAsync(nurse, residentId)).Value!.Allergies!.Version);
    }

    [Fact]
    public async Task DespuesDelRegistroInicial_ElMotivoEsObligatorio_YNuncaEsElInicial()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Motivo");
        Assert.True((await SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), null)).Ok);

        var withoutReason = await SaveAsync(nurse, residentId, 1, ConAlergia(), null);
        var initialAgain = await SaveAsync(nurse, residentId, 1, ConAlergia(), HealthBackgroundReason.RegistroInicial);
        var correction = await SaveAsync(nurse, residentId, 1, ConAlergia(), HealthBackgroundReason.Correccion);

        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error?.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, initialAgain.Error?.Code);
        Assert.True(correction.Ok);
    }

    [Fact]
    public async Task ElSqlRechazaCambiarOBorrarLasVersionesYSusElementos_YLosElementosIncoherentes()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Inmutable");
        Assert.True((await SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), null)).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var versionId = await connection.QuerySingleAsync<Guid>(
            "SELECT id FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id", new { Id = residentId.Value });

        var updateVersion = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.fichas_antecedentes_versiones SET estado_codigo = 'NO_CONSTA' WHERE id = @Id", new { Id = versionId }));
        var deleteVersion = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.fichas_antecedentes_versiones WHERE id = @Id", new { Id = versionId }));
        var updateItem = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.fichas_antecedentes_elementos SET nombre = N'Otro' WHERE version_id = @Id", new { Id = versionId }));
        var deleteItem = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.fichas_antecedentes_elementos WHERE version_id = @Id", new { Id = versionId }));

        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", updateVersion.Message);
        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", deleteVersion.Message);
        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", updateItem.Message);
        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", deleteItem.Message);
    }

    [Fact]
    public async Task UnaCorreccionDejaSuPropiaAccionEnLaAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Correccion");
        Assert.True((await SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), null)).Ok);
        Assert.True((await SaveAsync(nurse, residentId, 1, ConAlergia(), HealthBackgroundReason.Correccion)).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var actions = (await connection.QueryAsync<string>("""
            SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND' ORDER BY ocurrido_en
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal(["HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND_CORRECT"], actions);
    }

    [Fact]
    public async Task UnaVersionSinTodosSusElementos_NoSeEnsenaComoValida()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Incompleta");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
            // Una versión que declara dos elementos y solo tiene uno: la BD no puede impedirlo, la lectura sí lo detecta.
            var versionId = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, versionId, residentId, nurse, "CON_ENTRADAS", 2);
            await InsertItemAsync(connection, versionId, residentId, admin.CenterId, 1, "ALERGIA", "Farmaco Epsilon", null, transaction);
            transaction.Commit();
        }

        var read = await ReadAsync(nurse, residentId);

        // La lectura no falla: dice que la versión 1 no es fiable (sin enseñar su contenido) y deja guardar otra que la corrija.
        Assert.True(read.Ok);
        Assert.Null(read.Value!.Allergies);
        Assert.Equal(1, read.Value.UnreliableVersion);
        Assert.True(read.Value.CanEdit);
        var wrongSeen = await SaveAsync(nurse, residentId, 0, ConAlergia(), null);
        var corrected = await SaveAsync(nurse, residentId, 1, ConAlergia(), HealthBackgroundReason.Correccion);
        var after = (await ReadAsync(nurse, residentId)).Value!;

        Assert.Equal(ApplicationFailureCode.Conflict, wrongSeen.Error?.Code);
        Assert.Equal(2, corrected.Value);
        Assert.Null(after.UnreliableVersion);
        Assert.Equal(2, after.Allergies!.Version);
    }

    [Fact]
    public async Task MedicinaPuedeSerLaPrimeraEnRellenarla_YElAuditoriaGuardaElIdDeLaVersionSinTexto()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Medicina");

        var saved = await SaveAsync(doctor, residentId, 0, NoRefiereConIntolerancia(), null);

        Assert.Equal(1, saved.Value);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var version = await connection.QuerySingleAsync<(Guid Id, string Profile)>(
            "SELECT id, creado_por_perfil FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id", new { Id = residentId.Value });
        var audit = await connection.QuerySingleAsync<(Guid? ResourceId, Guid? UnitId, Guid CenterId, string Profile)>("""
            SELECT recurso_id, unidad_id, centro_id, perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'
            """, new { Id = residentId.Value });
        Assert.Equal("MEDICINA", version.Profile);
        Assert.Equal(version.Id, audit.ResourceId);
        Assert.Equal(admin.UnitId.Value, audit.UnitId);
        Assert.Equal(admin.CenterId.Value, audit.CenterId);
        Assert.Equal("MEDICINA", audit.Profile);
    }

    [Fact]
    public async Task DosGuardadosAlMismoTiempoSobreLaMismaVersion_UnoSeGuarda_YElOtroDaConflicto_SinHuecos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Paralela");

        var results = await Task.WhenAll(
            SaveAsync(nurse, residentId, 0, NoRefiereConIntolerancia(), null),
            SaveAsync(doctor, residentId, 0, ConAlergia(), null));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Equal(1, results.Count(r => r.Error?.Code == ApplicationFailureCode.Conflict));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal([1], (await connection.QueryAsync<int>(
            "SELECT numero_version FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id ORDER BY numero_version", new { Id = residentId.Value })).ToList());
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'", new { Id = residentId.Value }));
    }

    [Fact]
    public async Task UnGuardadoRechazado_NoDejaNiVersionNiAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var withoutPermission = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Rechazada");

        var denied = await SaveAsync(withoutPermission, residentId, 0, NoRefiereConIntolerancia(), null);
        var conflict = await SaveAsync(nurse, residentId, 5, NoRefiereConIntolerancia());

        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error?.Code);
        Assert.False(conflict.Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id", new { Id = residentId.Value }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'", new { Id = residentId.Value }));
    }

    [Fact]
    public async Task DireccionClinica_NoLaVe_NiPuedeTenerElPermisoDeEdicion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Direccion");

        var read = await ReadAsync(direction, residentId);
        var save = await SaveAsync(direction, residentId, 0, NoRefiereConIntolerancia(), null);
        // La BD impide que un perfil que no sea Enfermería ni Medicina tenga el permiso (TR_pp_profile_catalog).
        var grant = await Assert.ThrowsAnyAsync<Exception>(() => SeedFixture.AddProfileToCenterAsync(
            SystemProfile.DireccionClinica, admin.CenterId, admin.UnitId, [ProfilePermissions.HealthBackgroundEdit]));
        var grantToAuxiliar = await Assert.ThrowsAnyAsync<Exception>(() => SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Auxiliar, admin.CenterId, admin.UnitId, [ProfilePermissions.HealthBackgroundEdit]));

        Assert.Equal(ApplicationFailureCode.AccessDenied, read.Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, save.Error?.Code);
        Assert.Contains("PROFILE_PERMISSION_NOT_ALLOWED", grant.Message);
        Assert.Contains("PROFILE_PERMISSION_NOT_ALLOWED", grantToAuxiliar.Message);
    }

    [Fact]
    public async Task LimitesDeLaBaseDeDatos_FuenteMotivoReaccionYTopeDeElementos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Limites");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        async Task<Exception> VersionFailsAsync(string state, int count, string reason, int number, string? source)
        {
            using var transaction = connection.BeginTransaction();
            return await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync("""
                INSERT INTO dbo.fichas_antecedentes_versiones
                    (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, fuente_codigo, motivo_codigo, creado_por_cuenta_id, creado_por_perfil, creado_en)
                VALUES (NEWID(), @CenterId, @ResidentId, 'ALERGIAS', @Number, @State, @Count, @Source, @Reason, @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
                """, new
            {
                CenterId = admin.CenterId.Value, ResidentId = residentId.Value, Number = number, State = state, Count = count, Source = source, Reason = reason,
                AccountId = nurse.AccountId.Value,
            }, transaction));
        }

        Assert.Contains("CK_fav_fuente", (await VersionFailsAsync("NO_REFIERE", 0, "REGISTRO_INICIAL", 1, "UN_VECINO")).Message);
        Assert.Contains("CK_fav_motivo", (await VersionFailsAsync("NO_REFIERE", 0, "OTRO", 1, null)).Message);
        Assert.Contains("CK_fav_motivo_inicial", (await VersionFailsAsync("NO_REFIERE", 0, "ACTUALIZACION", 1, null)).Message);
        Assert.Contains("CK_fav_motivo_inicial", (await VersionFailsAsync("NO_REFIERE", 0, "REGISTRO_INICIAL", 2, null)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("CON_ENTRADAS", 0, "REGISTRO_INICIAL", 1, null)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("CON_ENTRADAS", 51, "REGISTRO_INICIAL", 1, null)).Message);
        Assert.Contains("CK_fav_numero", (await VersionFailsAsync("NO_REFIERE", 0, "REGISTRO_INICIAL", 0, null)).Message);

        // Una intolerancia sin reacción y una posición fuera de 1 a 50 las rechaza el SQL aunque la aplicación se saltara el dominio.
        using var itemTransaction = connection.BeginTransaction();
        var versionId = Guid.NewGuid();
        await InsertVersionAsync(connection, itemTransaction, versionId, residentId, nurse, "NO_REFIERE", 2);
        var withoutReaction = await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertItemAsync(connection, versionId, residentId, admin.CenterId, 1, "INTOLERANCIA", "Fármaco Eta", null, itemTransaction));
        Assert.Contains("CK_fae_intolerancia_reaccion", withoutReaction.Message);
    }

    [Fact]
    public async Task ElSqlRechazaLosElementosIncoherentesEnLaMismaTransaccionYLosAnadidosDespues()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Ficha Incoherente");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Guid closedVersion;
        using (var transaction = connection.BeginTransaction())
        {
            closedVersion = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, closedVersion, residentId, nurse, "NO_REFIERE", 1);
            // En la misma transacción: una ALERGIA en «No refiere» y una posición mayor que el número declarado.
            var allergyInNoRefiere = await Assert.ThrowsAnyAsync<Exception>(() =>
                InsertItemAsync(connection, closedVersion, residentId, admin.CenterId, 1, "ALERGIA", "Fármaco Gamma", null, transaction));
            Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", allergyInNoRefiere.Message);
        }
        using (var transaction = connection.BeginTransaction())
        {
            var version = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, version, residentId, nurse, "NO_REFIERE", 1);
            var beyondCount = await Assert.ThrowsAnyAsync<Exception>(() =>
                InsertItemAsync(connection, version, residentId, admin.CenterId, 2, "INTOLERANCIA", "Fármaco Delta", "picor", transaction));
            Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", beyondCount.Message);
        }
        // Una versión ya guardada (confirmada) que declara dos elementos y tiene uno: queda un hueco libre, pero otra transacción no puede rellenarlo.
        var savedVersion = Guid.NewGuid();
        using (var transaction = connection.BeginTransaction())
        {
            await InsertVersionAsync(connection, transaction, savedVersion, residentId, nurse, "NO_REFIERE", 2);
            await InsertItemAsync(connection, savedVersion, residentId, admin.CenterId, 1, "INTOLERANCIA", "Fármaco Eta", "picor", transaction);
            transaction.Commit();
        }
        var late = await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertItemAsync(connection, savedVersion, residentId, admin.CenterId, 2, "INTOLERANCIA", "Fármaco Zeta", "picor", null));
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", late.Message);
    }

    private static Task<int> InsertVersionAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, Guid versionId, ResidentId residentId, SeededProfile author,
        string state, int count) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.fichas_antecedentes_versiones
                (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, motivo_codigo, creado_por_cuenta_id, creado_por_perfil, creado_en)
            VALUES (@Id, @CenterId, @ResidentId, 'ALERGIAS', 1, @State, @Count, 'REGISTRO_INICIAL', @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
            """, new { Id = versionId, CenterId = author.CenterId.Value, ResidentId = residentId.Value, State = state, Count = count, AccountId = author.AccountId.Value }, transaction);

    private static Task<int> InsertItemAsync(
        System.Data.Common.DbConnection connection, Guid versionId, ResidentId residentId, CenterId centerId, int position, string type, string name, string? reaction,
        System.Data.Common.DbTransaction? transaction = null) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.fichas_antecedentes_elementos (id, version_id, residente_id, centro_id, posicion, tipo_codigo, nombre, reaccion)
            VALUES (NEWID(), @VersionId, @ResidentId, @CenterId, @Position, @Type, @Name, @Reaction)
            """, new
        {
            VersionId = versionId, ResidentId = residentId.Value, CenterId = centerId.Value, Position = position, Type = type, Name = name, Reaction = reaction,
        }, transaction);
}

file sealed class FixedHealthSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
