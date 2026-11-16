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

/// <summary>Ficha de antecedentes de salud, fase 3: medicación habitual (script 0097; docs/producto/ficha-antecedentes-salud-diseno.md). Reutiliza las tablas, el permiso y la
/// visibilidad de las fases anteriores; aquí se prueba lo propio: dosis y pauta obligatorias, estados sin elementos, tipo por apartado, límites de la BD (revisión del DPO,
/// 2026-11-16) y que un apartado ilegible no oculte a los otros. Los fármacos de las pruebas son inventados.</summary>
public class FichaAntecedentesMedicacionTests
{
    private static HealthBackgroundApplicationService Service(SeededProfile profile) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedMedicationSessionIdentityProvider(profile.ExternalSubject),
        new SqlHealthBackgroundRepository(TestDatabase.ConnectionFactory));

    private static Task<SeededProfile> WithPermissionAsync(SystemProfile profile, SeededProfile admin) =>
        SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId, [ProfilePermissions.HealthBackgroundEdit]);

    private static Task<ApplicationResult<HealthBackgroundView>> ReadAsync(SeededProfile profile, ResidentId residentId) =>
        Service(profile).ReadAsync(profile.ProfileScopeId, profile.CenterId, residentId);

    private static Task<ApplicationResult<int>> SaveAsync(
        SeededProfile profile, ResidentId residentId, int seenVersion, MedicationRecordContent content,
        HealthBackgroundReason? reason = HealthBackgroundReason.Actualizacion) =>
        Service(profile).SaveMedicationAsync(new SaveMedicationCommand(profile.ProfileScopeId, profile.CenterId, residentId, seenVersion, reason, content));

    private static MedicationRecordContent ConLista() => new(
        HealthBackgroundState.ConEntradas,
        [new MedicationItem("Fármaco Alfa", "1 g", "cada 8 h"), new MedicationItem("Fármaco Beta", "no consta", "si precisa")],
        HealthBackgroundSource.RecetaElectronica);

    private static MedicationRecordContent SinMedicacion() => new(HealthBackgroundState.NoRefiere, [], null);

    [Fact]
    public async Task SinNingunaVersion_ElApartadoEstaPendiente_YLoVenEnfermeriaYMedicina_PeroNoAuxiliarNiOtroCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria, [ProfilePermissions.HealthBackgroundEdit]);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Pendiente");

        var byNurse = (await ReadAsync(nurse, residentId)).Value!;
        var byDoctor = (await ReadAsync(doctor, residentId)).Value!;

        Assert.Null(byNurse.Medication);
        Assert.Null(byNurse.UnreliableMedicationVersion);
        Assert.False(byNurse.CanEdit);
        Assert.Null(byDoctor.Medication);
        Assert.True(byDoctor.CanEdit);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await ReadAsync(auxiliar, residentId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await Service(otherCenter).ReadAsync(otherCenter.ProfileScopeId, admin.CenterId, residentId)).Error?.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, (await SaveAsync(otherCenter, residentId, 0, ConLista(), null)).Error?.Code);
    }

    [Fact]
    public async Task EnfermeriaLaRellena_MedicinaLaCambia_YCadaGuardadoEsUnaVersionNuevaConSuAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Versiones");

        var first = await SaveAsync(nurse, residentId, 0, ConLista(), null);
        var second = await SaveAsync(doctor, residentId, 1, SinMedicacion());

        Assert.Equal(1, first.Value);
        Assert.Equal(2, second.Value);
        var afterSecond = (await ReadAsync(nurse, residentId)).Value!.Medication!;
        Assert.Equal(2, afterSecond.Version);
        Assert.Equal(SystemProfile.Medicina, afterSecond.ModifiedByProfile);
        Assert.Equal(HealthBackgroundState.NoRefiere, afterSecond.Content.State);
        Assert.Empty(afterSecond.Content.Items);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        // La versión 1 sigue guardada con sus dos medicamentos, en su orden, con dosis y pauta (y sin reacción ni año).
        var items = (await connection.QueryAsync<(string Type, string Name, string Dose, string Schedule, string? Reaction, int? Year)>("""
            SELECT e.tipo_codigo, e.nombre, e.dosis, e.pauta, e.reaccion, e.anio FROM dbo.fichas_antecedentes_elementos e
              JOIN dbo.fichas_antecedentes_versiones v ON v.id = e.version_id
             WHERE v.residente_id = @Id AND v.apartado_codigo = 'MEDICACION' AND v.numero_version = 1 ORDER BY e.posicion
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal([("MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h", (string?)null, (int?)null), ("MEDICAMENTO", "Fármaco Beta", "no consta", "si precisa", (string?)null, (int?)null)], items);
        var audit = (await connection.QueryAsync<(string Action, string Resource, string Profile)>("""
            SELECT accion_codigo, tipo_recurso, perfil_activo FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND' ORDER BY ocurrido_en
            """, new { Id = residentId.Value })).ToList();
        Assert.Equal([("HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND", "ENFERMERIA"), ("HEALTH_BACKGROUND_SAVE", "HEALTH_BACKGROUND", "MEDICINA")], audit);

        var third = await SaveAsync(nurse, residentId, 2, ConLista(), HealthBackgroundReason.Correccion);
        var again = (await ReadAsync(nurse, residentId)).Value!.Medication!;
        Assert.Equal(3, third.Value);
        Assert.Equal(["Fármaco Alfa", "Fármaco Beta"], again.Content.Items.Select(i => i.Name).ToArray());
        Assert.Equal(["no consta", "si precisa"], [again.Content.Items[1].Dose, again.Content.Items[1].Schedule]);
        Assert.Contains("HEALTH_BACKGROUND_CORRECT", await connection.QueryAsync<string>(
            "SELECT accion_codigo FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'", new { Id = residentId.Value }));
    }

    [Fact]
    public async Task LosTresApartadosSeVersionanPorSeparado_YNoSeMezclan_YUnoIlegibleNoOcultaALosOtros()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Aparte");
        Assert.True((await SaveAsync(nurse, residentId, 0, ConLista(), null)).Ok);
        Assert.True((await Service(nurse).SaveConditionsAsync(new SaveConditionsCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null,
            new ConditionRecordContent(HealthBackgroundState.ConEntradas, [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", 2010)], null)))).Ok);
        Assert.True((await Service(nurse).SaveAllergiesAsync(new SaveAllergiesCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null,
            new AllergyRecordContent(HealthBackgroundState.NoRefiere, [], null)))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        using (var transaction = connection.BeginTransaction())
        {
            // Una versión de medicación (la 2) que declara dos elementos y no trae ninguno.
            await connection.ExecuteAsync("""
                INSERT INTO dbo.fichas_antecedentes_versiones
                    (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, motivo_codigo, creado_por_cuenta_id, creado_por_perfil, creado_en)
                VALUES (NEWID(), @CenterId, @ResidentId, 'MEDICACION', 2, 'CON_ENTRADAS', 2, 'ACTUALIZACION', @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
                """, new { CenterId = admin.CenterId.Value, ResidentId = residentId.Value, AccountId = nurse.AccountId.Value }, transaction);
            transaction.Commit();
        }

        var view = (await ReadAsync(nurse, residentId)).Value!;

        Assert.Null(view.Medication);
        Assert.Equal(2, view.UnreliableMedicationVersion);
        Assert.Null(view.UnreliableVersion);
        Assert.Null(view.UnreliableConditionsVersion);
        Assert.Equal(1, view.Allergies!.Version);
        Assert.Equal("Enfermedad Alfa", Assert.Single(view.Conditions!.Content.Items).Name);
        // La versión ilegible se corrige guardando otra.
        var wrongSeen = await SaveAsync(nurse, residentId, 1, ConLista());
        var corrected = await SaveAsync(nurse, residentId, 2, ConLista(), HealthBackgroundReason.Correccion);
        Assert.Equal(ApplicationFailureCode.Conflict, wrongSeen.Error?.Code);
        Assert.Equal(3, corrected.Value);
        Assert.Null((await ReadAsync(nurse, residentId)).Value!.UnreliableMedicationVersion);
    }

    [Fact]
    public async Task SinElPermisoNoGuarda_UnaVersionVistaQueYaNoEsLaVigenteDaConflicto_YDosGuardadosAlMismoTiempoSoloGuardaUno()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var withoutPermission = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var doctor = await WithPermissionAsync(SystemProfile.Medicina, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Conflicto");
        var otherResident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Paralela");

        var denied = await SaveAsync(withoutPermission, residentId, 0, ConLista(), null);
        var first = await SaveAsync(nurse, residentId, 0, ConLista(), null);
        var stale = await SaveAsync(nurse, residentId, 0, SinMedicacion(), null);
        var future = await SaveAsync(nurse, residentId, 5, SinMedicacion());
        var parallel = await Task.WhenAll(SaveAsync(nurse, otherResident, 0, ConLista(), null), SaveAsync(doctor, otherResident, 0, SinMedicacion(), null));

        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error?.Code);
        Assert.Equal(1, first.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error?.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, future.Error?.Code);
        Assert.Equal(1, parallel.Count(r => r.Ok));
        Assert.Equal(1, parallel.Count(r => r.Error?.Code == ApplicationFailureCode.Conflict));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id", new { Id = residentId.Value }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND tipo_recurso = 'HEALTH_BACKGROUND'", new { Id = residentId.Value }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.fichas_antecedentes_versiones WHERE residente_id = @Id", new { Id = otherResident.Value }));
    }

    [Fact]
    public async Task UnaListaDeSesentaMedicamentos_SeGuardaYSeLeeEnOrden()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Sesenta");
        var items = Enumerable.Range(1, MedicationRecordContent.MaxItems).Select(i => new MedicationItem($"Fármaco {i}", $"{i} mg", i % 2 == 0 ? "cada 12 h" : "si precisa")).ToList();

        var saved = await SaveAsync(nurse, residentId, 0, new MedicationRecordContent(HealthBackgroundState.ConEntradas, items, null), null);

        Assert.Equal(1, saved.Value);
        var view = (await ReadAsync(nurse, residentId)).Value!;
        Assert.Null(view.UnreliableMedicationVersion);
        Assert.Equal(items.Select(i => (i.Name, i.Dose, i.Schedule)), view.Medication!.Content.Items.Select(i => (i.Name, i.Dose, i.Schedule)));
    }

    [Fact]
    public async Task UnMedicamentoConLosMaximos_SeGuardaYSeLeeIgual_ConAcentosYElMismoFarmacoConOtraDosis()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Maximos");
        var longName = "Fármaco Ñandú " + new string('á', MedicationItem.MaxNameLength - 14);
        var content = new MedicationRecordContent(HealthBackgroundState.ConEntradas,
        [
            new MedicationItem(longName, new string('1', MedicationItem.MaxDoseLength), new string('é', MedicationItem.MaxScheduleLength)),
            new MedicationItem("Fármaco Gamma", "10 u", "por la mañana"),
            new MedicationItem("Fármaco Gamma", "6 u", "por la noche"),
        ], HealthBackgroundSource.InformeMedico);

        var saved = await SaveAsync(nurse, residentId, 0, content, null);

        Assert.Equal(1, saved.Value);
        var read = (await ReadAsync(nurse, residentId)).Value!.Medication!;
        Assert.Equal(MedicationItem.MaxNameLength, read.Content.Items[0].Name.Length);
        Assert.Equal(content.Items.Select(i => (i.Name, i.Dose, i.Schedule)), read.Content.Items.Select(i => (i.Name, i.Dose, i.Schedule)));
    }

    [Fact]
    public async Task GuardadosDeResidentesDistintosAlMismoTiempo_EnLosTresApartados_SeCompletanTodosSinInterbloqueos()
    {
        // El disparador de coherencia busca la versión por clave (FORCESEEK): sin eso, en una BD recién creada, dos guardados de residentes distintos a la vez se esperaban entre sí (1205).
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residents = new List<ResidentId>();
        for (var i = 0; i < 12; i++)
        {
            residents.Add(await AdministracionResidentesTests.CreateResidentAsync(admin, $"Residente Medicacion Concurrente {i}"));
        }
        var service = Service(nurse);
        var medication = ConLista();
        var conditions = new ConditionRecordContent(HealthBackgroundState.ConEntradas, [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", 2010), new ConditionItem(ConditionItemType.Intervencion, "Intervención Beta", null)], null);
        var allergies = new AllergyRecordContent(HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Delta", null), new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Eta", "picor")], null);

        var results = await Task.WhenAll(residents.SelectMany(residentId => new[]
        {
            service.SaveMedicationAsync(new SaveMedicationCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null, medication)),
            service.SaveConditionsAsync(new SaveConditionsCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null, conditions)),
            service.SaveAllergiesAsync(new SaveAllergiesCommand(nurse.ProfileScopeId, nurse.CenterId, residentId, 0, null, allergies)),
        }));

        Assert.All(results, result => Assert.True(result.Ok, result.Error?.Message));
        foreach (var residentId in residents)
        {
            var view = (await ReadAsync(nurse, residentId)).Value!;
            Assert.NotNull(view.Medication);
            Assert.NotNull(view.Conditions);
            Assert.NotNull(view.Allergies);
        }
    }

    [Fact]
    public async Task LimitesDeLaBaseDeDatos_ApartadoTopeEstadosTipoDosisPautaYNombre()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Limites");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        async Task<Exception> VersionFailsAsync(string section, string state, int count)
        {
            using var transaction = connection.BeginTransaction();
            return await Assert.ThrowsAnyAsync<Exception>(() => InsertVersionAsync(connection, transaction, Guid.NewGuid(), residentId, nurse, state, count, section));
        }

        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("MEDICACION", "CON_ENTRADAS", 0)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("MEDICACION", "CON_ENTRADAS", 61)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("MEDICACION", "NO_REFIERE", 1)).Message);
        Assert.Contains("CK_fav_elementos", (await VersionFailsAsync("MEDICACION", "NO_CONSTA", 1)).Message);
        Assert.Matches("CK_fav_(apartado|elementos)", (await VersionFailsAsync("OTRO", "NO_REFIERE", 0)).Message);

        async Task<Exception> ItemFailsAsync(string section, string type, string name, string? dose, string? schedule, string? reaction = null, int? year = null, int position = 1)
        {
            using var transaction = connection.BeginTransaction();
            var versionId = Guid.NewGuid();
            await InsertVersionAsync(connection, transaction, versionId, residentId, nurse, "CON_ENTRADAS", 2, section);
            return await Assert.ThrowsAnyAsync<Exception>(() => InsertItemAsync(connection, versionId, residentId, admin.CenterId, position, type, name, dose, schedule, reaction, year, transaction));
        }

        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", null, "cada 8 h")).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1 g", null)).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "  ", "cada 8 h")).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco\nAlfa", "1 g", "cada 8 h")).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1\rg", "cada 8 h")).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada\n8 h")).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", new string('a', 101), "1 g", "cada 8 h")).Message);
        // La dosis y la pauta no caben más de lo que dice la columna: 50 y 100.
        Assert.Matches("(?i)trunc|CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", new string('1', 51), "cada 8 h")).Message);
        Assert.Matches("(?i)trunc|CK_fae_medicamento", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1 g", new string('a', 101))).Message);
        // La dosis y la pauta solo existen en un medicamento; un medicamento no lleva reacción ni año.
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("ENFERMEDADES", "ENFERMEDAD", "Enfermedad Alfa", "1 g", null)).Message);
        Assert.Contains("CK_fae_medicamento", (await ItemFailsAsync("ALERGIAS", "ALERGIA", "Fármaco Alfa", null, "cada 8 h")).Message);
        Assert.Contains("CK_fae_reaccion_tipo", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h", reaction: "náuseas")).Message);
        Assert.Contains("CK_fae_anio", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h", year: 2010)).Message);
        // El tipo debe ser el del apartado, en los dos sentidos.
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", (await ItemFailsAsync("ALERGIAS", "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h")).Message);
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", (await ItemFailsAsync("ENFERMEDADES", "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h")).Message);
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", (await ItemFailsAsync("MEDICACION", "INTOLERANCIA", "Fármaco Alfa", null, null, reaction: "picor")).Message);
        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", (await ItemFailsAsync("MEDICACION", "ENFERMEDAD", "Enfermedad Alfa", null, null)).Message);
        Assert.Contains("CK_fae_posicion", (await ItemFailsAsync("MEDICACION", "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h", position: 101)).Message);
    }

    [Fact]
    public async Task ElSqlNoDejaCambiarNiAnadirDespuesElementosDeMedicacion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await WithPermissionAsync(SystemProfile.Enfermeria, admin);
        var residentId = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Medicacion Cerrada");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var savedVersion = Guid.NewGuid();
        using (var transaction = connection.BeginTransaction())
        {
            await InsertVersionAsync(connection, transaction, savedVersion, residentId, nurse, "CON_ENTRADAS", 2, "MEDICACION");
            await InsertItemAsync(connection, savedVersion, residentId, admin.CenterId, 1, "MEDICAMENTO", "Fármaco Alfa", "1 g", "cada 8 h", null, null, transaction);
            transaction.Commit();
        }

        var late = await Assert.ThrowsAnyAsync<Exception>(() =>
            InsertItemAsync(connection, savedVersion, residentId, admin.CenterId, 2, "MEDICAMENTO", "Fármaco Beta", "1 g", "cada 8 h", null, null, null));
        var update = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "UPDATE dbo.fichas_antecedentes_elementos SET dosis = '2 g' WHERE version_id = @Id", new { Id = savedVersion }));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.fichas_antecedentes_elementos WHERE version_id = @Id", new { Id = savedVersion }));

        Assert.Contains("HEALTH_BACKGROUND_ELEMENT_INCOHERENT", late.Message);
        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", update.Message);
        Assert.Contains("HEALTH_BACKGROUND_IMMUTABLE", delete.Message);
    }

    private static Task<int> InsertVersionAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, Guid versionId, ResidentId residentId, SeededProfile author,
        string state, int count, string section) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.fichas_antecedentes_versiones
                (id, centro_id, residente_id, apartado_codigo, numero_version, estado_codigo, numero_elementos, motivo_codigo, creado_por_cuenta_id, creado_por_perfil, creado_en)
            VALUES (@Id, @CenterId, @ResidentId, @Section, 1, @State, @Count, 'REGISTRO_INICIAL', @AccountId, 'ENFERMERIA', SYSUTCDATETIME())
            """, new { Id = versionId, CenterId = author.CenterId.Value, ResidentId = residentId.Value, Section = section, State = state, Count = count, AccountId = author.AccountId.Value }, transaction);

    private static Task<int> InsertItemAsync(
        System.Data.Common.DbConnection connection, Guid versionId, ResidentId residentId, CenterId centerId, int position, string type, string name, string? dose, string? schedule,
        string? reaction, int? year, System.Data.Common.DbTransaction? transaction) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.fichas_antecedentes_elementos (id, version_id, residente_id, centro_id, posicion, tipo_codigo, nombre, reaccion, anio, dosis, pauta)
            VALUES (NEWID(), @VersionId, @ResidentId, @CenterId, @Position, @Type, @Name, @Reaction, @Year, @Dose, @Schedule)
            """, new
        {
            VersionId = versionId, ResidentId = residentId.Value, CenterId = centerId.Value, Position = position, Type = type, Name = name, Reaction = reaction, Year = year,
            Dose = dose, Schedule = schedule,
        }, transaction);
}

file sealed class FixedMedicationSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
