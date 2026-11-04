using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Authorization;
using ResidApp.Application.Ports;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using Xunit;

namespace ResidApp.IntegrationTests;

/// <summary>Seguridad por filas por centro (ADR 0008, script 0030). La política deja pasar a los miembros de db_owner, que es lo que usa
/// el resto de tests, así que estas pruebas abren la conexión con la fábrica de producción (que fija el contexto de sesión) y después
/// suplantan con EXECUTE AS un usuario sin login ni db_owner, como será el usuario real de la aplicación.</summary>
public sealed class RlsCentroTests
{
    private const string LimitedUser = "residapp_rls_test";

    private sealed class FixedTenant(TenantScope? scope) : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult(scope);
    }

    private sealed record Seeded(SeededProfile Profile, Guid ResidentId);

    // Una sola vez por proceso: cambiar la pertenencia a roles mientras otras pruebas consultan puede bloquearlas.
    private static readonly Lazy<Task> LimitedUserCreated = new(async () =>
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{LimitedUser}') IS NULL CREATE USER {LimitedUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'db_datareader', N'{LimitedUser}') = 0 ALTER ROLE db_datareader ADD MEMBER {LimitedUser};
            IF IS_ROLEMEMBER(N'db_datawriter', N'{LimitedUser}') = 0 ALTER ROLE db_datawriter ADD MEMBER {LimitedUser};
            """);
    });

    private static async Task<SqlConnection> OpenLimitedAsync(TenantScope? scope)
    {
        await LimitedUserCreated.Value;
        // Sin pool: una conexión suplantada que vuelve al pool sin REVERT no se puede restablecer ("session is in the kill state").
        // Sin MARS: EXECUTE AS falla de forma intermitente con sesiones múltiples ("a simultaneous batch has called it").
        var impersonable = new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;
        var connection = await new SqlConnectionFactory(impersonable, new FixedTenant(scope)).OpenAsync();
        await connection.ExecuteAsync($"EXECUTE AS USER = N'{LimitedUser}'");
        return connection;
    }

    private static async Task<(string? Subject, string? ProfileScopeId)> SessionContextAsync(TenantScope? scope)
    {
        using var connection = await new SqlConnectionFactory(TestDatabase.ConnectionString, new FixedTenant(scope)).OpenAsync();
        return await connection.QuerySingleAsync<(string?, string?)>("""
            SELECT CONVERT(NVARCHAR(200), SESSION_CONTEXT(N'sujeto_externo')) AS Subject,
                   CONVERT(NVARCHAR(36), SESSION_CONTEXT(N'ambito_perfil_id')) AS ProfileScopeId
            """);
    }

    private static TenantScope ScopeOf(SeededProfile profile) => new(profile.ExternalSubject, profile.ProfileScopeId);

    private static async Task<Seeded> SeedCenterWithResidentAsync()
    {
        var profile = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.residentes (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, creado_en, creado_por_cuenta_id, creado_por_perfil)
            VALUES (@residentId, @centerId, N'Residente RLS', '1940-01-01', 'unknown', SYSUTCDATETIME(), @accountId, 'ADMINISTRACION')
            """, new { residentId, centerId = profile.CenterId.Value, accountId = profile.AccountId.Value });
        return new Seeded(profile, residentId);
    }

    private static async Task<List<Guid>> VisibleAsync(TenantScope? scope, params Guid[] residentIds)
    {
        using var connection = await OpenLimitedAsync(scope);
        return (await connection.QueryAsync<Guid>(
            "SELECT id FROM dbo.residentes WHERE id IN @residentIds", new { residentIds })).ToList();
    }

    [Fact]
    public async Task Con_ambito_activo_solo_se_ven_los_residentes_de_su_centro()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();

        var visibleA = await VisibleAsync(ScopeOf(a.Profile), a.ResidentId, b.ResidentId);
        var visibleB = await VisibleAsync(ScopeOf(b.Profile), a.ResidentId, b.ResidentId);

        Assert.Equal([a.ResidentId], visibleA);
        Assert.Equal([b.ResidentId], visibleB);
    }

    [Fact]
    public async Task La_politica_tambien_cubre_los_familiares()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();
        var familyIds = new List<Guid>();
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            foreach (var seeded in new[] { a, b })
            {
                var familyId = Guid.NewGuid();
                familyIds.Add(familyId);
                await admin.ExecuteAsync("""
                    INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, creado_por_cuenta_id, creado_en)
                    VALUES (@familyId, @centerId, N'Familiar RLS', N'600000000', @accountId, SYSUTCDATETIME())
                    """, new { familyId, centerId = seeded.Profile.CenterId.Value, accountId = seeded.Profile.AccountId.Value });
            }
        }

        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));
        var visible = (await connection.QueryAsync<Guid>("SELECT id FROM dbo.familiares WHERE id IN @familyIds", new { familyIds })).ToList();

        Assert.Equal([familyIds[0]], visible);
    }

    private static readonly string[] BaselineTables =
    [
        "basales_borrador", "basales_borrador_areas", "basales_borrador_barthel", "basales_borrador_barthel_items",
        "basales_version", "basales_version_areas", "basales_version_barthel", "basales_version_barthel_items",
        "basales_vigentes_residente", "basales_sustituciones",
    ];

    /// <summary>Un residente con un basal firmado y otro firmado después (la reevaluación genera la sustitución): todas las tablas
    /// del basal reciben filas del centro.</summary>
    private static async Task<SeededProfile> SeedCenterWithSignedBaselinesAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(
            SystemProfile.Enfermeria, admin.CenterId, admin.UnitId, ["BASELINE_INITIAL_COMPLETE", "BASELINE_REEVALUATE"]);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId,
            "Residente Basal RLS", new DateOnly(1940, 5, 5), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        var repository = new SqlBaselineRepository(TestDatabase.ConnectionFactory);
        foreach (var reason in new[] { BaselineReason.Alta, BaselineReason.RevisionProgramada })
        {
            var created = await repository.CreateDraftAsync(new CreateBaselineDraftInput(
                nurse.AccountId, SystemProfile.Enfermeria, nurse.CenterId, nurse.UnitId, resident.ResidentId, reason,
                InformationSourceCode.ValoracionDirecta, null, new DateOnly(2026, 9, 14), Guid.NewGuid()));
            var owner = new OwnedActiveDraftInput(nurse.AccountId, SystemProfile.Enfermeria, nurse.CenterId, resident.ResidentId);
            foreach (var (area, answer) in BaselineTestData.NineAreas())
            {
                await repository.SaveAreaAsync(new SaveBaselineDraftAreaInput(owner, area, answer, null));
            }
            await repository.SaveBarthelAsync(new SaveBaselineDraftBarthelInput(owner, new DateOnly(2026, 9, 14), BaselineTestData.FullBarthelItems()));
            await repository.SignDraftAsync(new SignBaselineDraftInput(
                nurse.AccountId, SystemProfile.Enfermeria, nurse.CenterId, nurse.UnitId, resident.ResidentId,
                created.DraftId, created.DraftRevision, Guid.NewGuid()));
        }
        return nurse;
    }

    [Fact]
    public async Task Las_tablas_de_basal_solo_se_ven_en_su_centro()
    {
        var a = await SeedCenterWithSignedBaselinesAsync();
        var b = await SeedCenterWithSignedBaselinesAsync();

        async Task<Dictionary<string, (int Own, int Other)>> CountAsync(SeededProfile viewer, SeededProfile other)
        {
            using var connection = await OpenLimitedAsync(ScopeOf(viewer));
            var counts = new Dictionary<string, (int, int)>();
            foreach (var table in BaselineTables)
            {
                counts[table] = (
                    await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE centro_id = @CenterId", new { CenterId = viewer.CenterId.Value }),
                    await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE centro_id = @CenterId", new { CenterId = other.CenterId.Value }));
            }
            return counts;
        }

        foreach (var (viewer, other) in new[] { (a, b), (b, a) })
        {
            foreach (var (table, (own, foreign)) in await CountAsync(viewer, other))
            {
                Assert.True(own > 0, $"{table}: el centro propio debería tener filas (si no, la siembra no llena la tabla y la prueba no prueba nada).");
                Assert.True(foreign == 0, $"{table}: se ven {foreign} filas de otro centro.");
            }
        }
    }

    [Fact]
    public async Task Sin_sesion_o_sin_ambito_no_se_ve_ninguna_fila()
    {
        var a = await SeedCenterWithResidentAsync();

        Assert.Empty(await VisibleAsync(null, a.ResidentId));
    }

    [Fact]
    public async Task Un_ambito_manipulado_en_la_cookie_no_da_acceso_al_centro_ajeno()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();

        // Sujeto de A con el id de ámbito de B, y al revés: el predicado exige que el ámbito sea de esa cuenta.
        var aConAmbitoDeB = new TenantScope(a.Profile.ExternalSubject, b.Profile.ProfileScopeId);
        var bConAmbitoDeA = new TenantScope(b.Profile.ExternalSubject, a.Profile.ProfileScopeId);

        Assert.Empty(await VisibleAsync(aConAmbitoDeB, a.ResidentId, b.ResidentId));
        Assert.Empty(await VisibleAsync(bConAmbitoDeA, a.ResidentId, b.ResidentId));
    }

    [Fact]
    public async Task Un_ambito_revocado_deja_de_ver_filas()
    {
        var a = await SeedCenterWithResidentAsync();
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await admin.ExecuteAsync("""
                UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @accountId
                 WHERE id = @profileScopeId
                """, new { accountId = a.Profile.AccountId.Value, profileScopeId = a.Profile.ProfileScopeId });
        }

        Assert.Empty(await VisibleAsync(ScopeOf(a.Profile), a.ResidentId));
    }

    [Fact]
    public async Task No_se_puede_insertar_un_residente_en_otro_centro()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();

        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.residentes (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, creado_en, creado_por_cuenta_id, creado_por_perfil)
            VALUES (NEWID(), @centerId, N'Intruso', '1940-01-01', 'unknown', SYSUTCDATETIME(), @accountId, 'ADMINISTRACION')
            """, new { centerId = b.Profile.CenterId.Value, accountId = a.Profile.AccountId.Value }));
        Assert.Equal(33504, error.Number);
    }

    [Fact]
    public async Task Si_se_puede_insertar_un_residente_en_su_propio_centro()
    {
        var a = await SeedCenterWithResidentAsync();
        var newResidentId = Guid.NewGuid();

        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));
        await connection.ExecuteAsync("""
            INSERT INTO dbo.residentes (id, centro_id, nombre_visible, fecha_nacimiento, sexo_documentado_codigo, creado_en, creado_por_cuenta_id, creado_por_perfil)
            VALUES (@newResidentId, @centerId, N'Propio', '1940-01-01', 'unknown', SYSUTCDATETIME(), @accountId, 'ADMINISTRACION')
            """, new { newResidentId, centerId = a.Profile.CenterId.Value, accountId = a.Profile.AccountId.Value });

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.residentes WHERE id = @newResidentId", new { newResidentId }));
    }

    [Fact]
    public async Task No_se_puede_mover_un_residente_a_otro_centro()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();

        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.residentes SET centro_id = @centerId WHERE id = @residentId",
            new { centerId = b.Profile.CenterId.Value, residentId = a.ResidentId }));
        Assert.Equal(33504, error.Number);
    }

    [Fact]
    public async Task Las_conexiones_reutilizadas_del_pool_no_arrastran_el_ambito_anterior()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();

        // Misma cadena y mismo pool: la conexión liberada con el ámbito de A se reutiliza con el de B, y después sin ámbito.
        // Se comprueba el contexto que deja la fábrica (la política deja pasar a este usuario, db_owner, así que no se ve por filas).
        for (var vuelta = 0; vuelta < 3; vuelta++)
        {
            Assert.Equal((a.Profile.ExternalSubject, a.Profile.ProfileScopeId.ToString().ToUpperInvariant()), await SessionContextAsync(ScopeOf(a.Profile)));
            Assert.Equal((b.Profile.ExternalSubject, b.Profile.ProfileScopeId.ToString().ToUpperInvariant()), await SessionContextAsync(ScopeOf(b.Profile)));
            Assert.Equal((null, null), await SessionContextAsync(null));
        }
    }

    [Fact]
    public async Task El_contexto_de_la_sesion_no_se_puede_cambiar_desde_una_consulta()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();

        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));
        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "EXEC sys.sp_set_session_context @key = N'ambito_perfil_id', @value = @ambito",
            new { ambito = b.Profile.ProfileScopeId }));
    }

    private const int BlockPredicateViolation = 33504;

    private static async Task InsertAuditAsync(SqlConnection connection, Guid centerId, Guid accountId, string profile, string action = "TEST_ACTION") =>
        await connection.ExecuteAsync("""
            INSERT INTO dbo.eventos_auditoria (id, cuenta_id, perfil_activo, centro_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
            VALUES (NEWID(), @accountId, @profile, @centerId, N'TEST', NEWID(), @action, SYSUTCDATETIME())
            """, new { centerId, accountId, profile, action });

    private static async Task<int> AuditCountAsync(SqlConnection connection, string action) =>
        await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE accion_codigo = @action", new { action });

    /// <summary>Un operador de Plataforma: su ámbito vive en el centro reservado (un disparador lo exige) y no ve el de ningún otro centro.</summary>
    private static async Task<(TenantScope Scope, Guid AccountId, Guid ProfileScopeId)> SeedPlatformOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var subject = $"test-plat-{Guid.NewGuid():N}"[..28];
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador RLS', 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
            """, new { accountId, subject, profileScopeId, centerId = ResidApp.Domain.Platform.PlatformCenter.Id });
        return (new TenantScope(subject, profileScopeId), accountId, profileScopeId);
    }

    [Fact]
    public async Task La_auditoria_solo_se_ve_la_del_centro_del_ambito()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();
        var action = $"RLS_{Guid.NewGuid():N}"[..30];
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await InsertAuditAsync(admin, a.Profile.CenterId.Value, a.Profile.AccountId.Value, "ADMINISTRACION", action);
            await InsertAuditAsync(admin, b.Profile.CenterId.Value, b.Profile.AccountId.Value, "ADMINISTRACION", action);
        }

        using var asA = await OpenLimitedAsync(ScopeOf(a.Profile));
        Assert.Equal(1, await AuditCountAsync(asA, action));
        using var withoutScope = await OpenLimitedAsync(null);
        Assert.Equal(0, await AuditCountAsync(withoutScope, action));
    }

    [Fact]
    public async Task Un_ambito_no_puede_escribir_auditoria_en_otro_centro_ni_a_nombre_de_otra_cuenta_o_perfil()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();
        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));

        await InsertAuditAsync(connection, a.Profile.CenterId.Value, a.Profile.AccountId.Value, "ADMINISTRACION");
        var otherCenter = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAuditAsync(connection, b.Profile.CenterId.Value, a.Profile.AccountId.Value, "ADMINISTRACION"));
        var otherAccount = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAuditAsync(connection, a.Profile.CenterId.Value, b.Profile.AccountId.Value, "ADMINISTRACION"));
        var otherProfile = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAuditAsync(connection, a.Profile.CenterId.Value, a.Profile.AccountId.Value, "MEDICINA"));
        var impersonatedPlatform = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAuditAsync(connection, b.Profile.CenterId.Value, a.Profile.AccountId.Value, "PLATAFORMA"));

        Assert.All([otherCenter, otherAccount, otherProfile, impersonatedPlatform], error => Assert.Equal(BlockPredicateViolation, error.Number));
    }

    [Fact]
    public async Task Plataforma_deja_constancia_en_cualquier_centro_pero_no_lee_la_de_otros()
    {
        var center = await SeedCenterWithResidentAsync();
        var operatorScope = await SeedPlatformOperatorAsync();
        var action = $"RLS_{Guid.NewGuid():N}"[..30];
        using var connection = await OpenLimitedAsync(operatorScope.Scope);

        await InsertAuditAsync(connection, center.Profile.CenterId.Value, operatorScope.AccountId, "PLATAFORMA", action);
        Assert.Equal(0, await AuditCountAsync(connection, action));

        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await AuditCountAsync(admin, action));
        // El centro afectado sí la ve en su auditoría.
        using var asCenter = await OpenLimitedAsync(ScopeOf(center.Profile));
        Assert.Equal(1, await AuditCountAsync(asCenter, action));
    }

    [Fact]
    public async Task El_salto_de_Plataforma_exige_ser_Plataforma_con_ambito_vigente_y_firmar_con_su_cuenta()
    {
        var center = await SeedCenterWithResidentAsync();
        var operatorScope = await SeedPlatformOperatorAsync();
        using (var connection = await OpenLimitedAsync(operatorScope.Scope))
        {
            var otherAccount = await Assert.ThrowsAsync<SqlException>(() =>
                InsertAuditAsync(connection, center.Profile.CenterId.Value, center.Profile.AccountId.Value, "PLATAFORMA"));
            var otherProfile = await Assert.ThrowsAsync<SqlException>(() =>
                InsertAuditAsync(connection, center.Profile.CenterId.Value, operatorScope.AccountId, "ADMINISTRACION"));
            Assert.All([otherAccount, otherProfile], error => Assert.Equal(BlockPredicateViolation, error.Number));
        }

        // Un ámbito revocado ya no escribe en ningún centro.
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await admin.ExecuteAsync(
                "UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = cuenta_id WHERE id = @id", new { id = operatorScope.ProfileScopeId });
        }

        using var revoked = await OpenLimitedAsync(operatorScope.Scope);
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAuditAsync(revoked, center.Profile.CenterId.Value, operatorScope.AccountId, "PLATAFORMA"));
        Assert.Equal(BlockPredicateViolation, error.Number);
    }

    // ---- Estructura del centro (script 0090): unidades, asignaciones a unidad, cambios de Administración principal y logos ----

    private static async Task<Guid> InsertUnitAsync(SqlConnection connection, Guid centerId)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en)
            VALUES (@id, @centerId, @code, N'Unidad RLS', 'ACTIVE', SYSUTCDATETIME())
            """, new { id, centerId, code = $"RLS-{id:N}"[..24] });
        return id;
    }

    private static Task InsertAssignmentAsync(SqlConnection connection, Guid profileScopeId, Guid centerId, Guid unitId, Guid grantedBy) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @profileScopeId, @centerId, @unitId, SYSUTCDATETIME(), @grantedBy)
            """, new { profileScopeId, centerId, unitId, grantedBy });

    private static Task InsertPrincipalChangeAsync(SqlConnection connection, Guid centerId, Guid profileScopeId, int number, Guid changedBy, string profile, bool principal = true) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.administraciones_principales_cambios (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @centerId, @profileScopeId, @number, @principal, @changedBy, @profile, SYSUTCDATETIME())
            """, new { centerId, profileScopeId, number, changedBy, profile, principal });

    private static Task InsertLogoAsync(SqlConnection connection, Guid centerId, Guid updatedBy, string profile) =>
        connection.ExecuteAsync("""
            INSERT INTO dbo.logos_centro (centro_id, tipo, contenido, actualizado_por_cuenta_id, actualizado_por_perfil, actualizado_en)
            VALUES (@centerId, 'PNG', 0x89504E47, @updatedBy, @profile, SYSUTCDATETIME())
            """, new { centerId, updatedBy, profile });

    private static async Task<int> CountAsync(SqlConnection connection, string table, Guid centerId) =>
        await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE centro_id = @centerId", new { centerId });

    /// <summary>Un centro con su Administración, una asignación a unidad (la que siembra el fixture), un cambio de principal y un logo, escritos como db_owner.</summary>
    private static async Task<Seeded> SeedCenterWithStructureAsync()
    {
        var center = await SeedCenterWithResidentAsync();
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await InsertPrincipalChangeAsync(admin, center.Profile.CenterId.Value, center.Profile.ProfileScopeId, 1, center.Profile.AccountId.Value, "ADMINISTRACION");
        await InsertLogoAsync(admin, center.Profile.CenterId.Value, center.Profile.AccountId.Value, "ADMINISTRACION");
        return center;
    }

    private static readonly string[] StructureTables = ["unidades", "ambitos_perfil_unidad", "administraciones_principales_cambios", "logos_centro"];

    [Fact]
    public async Task La_Administracion_solo_ve_la_estructura_de_su_centro()
    {
        var a = await SeedCenterWithStructureAsync();
        var b = await SeedCenterWithStructureAsync();

        using var asA = await OpenLimitedAsync(ScopeOf(a.Profile));
        foreach (var table in StructureTables)
        {
            Assert.True(await CountAsync(asA, table, a.Profile.CenterId.Value) >= 1, $"{table}: no ve la de su centro");
            Assert.Equal(0, await CountAsync(asA, table, b.Profile.CenterId.Value));
        }

        using var withoutScope = await OpenLimitedAsync(null);
        foreach (var table in StructureTables)
        {
            Assert.Equal(0, await CountAsync(withoutScope, table, a.Profile.CenterId.Value));
        }
    }

    [Fact]
    public async Task Plataforma_ve_unidades_asignaciones_y_cambios_de_cualquier_centro_pero_no_sus_logos()
    {
        var center = await SeedCenterWithStructureAsync();
        var operatorScope = await SeedPlatformOperatorAsync();

        using var connection = await OpenLimitedAsync(operatorScope.Scope);
        var centerId = center.Profile.CenterId.Value;
        Assert.True(await CountAsync(connection, "unidades", centerId) >= 1);
        Assert.True(await CountAsync(connection, "ambitos_perfil_unidad", centerId) >= 1);
        Assert.Equal(1, await CountAsync(connection, "administraciones_principales_cambios", centerId));
        Assert.Equal(0, await CountAsync(connection, "logos_centro", centerId));

        // Quien tiene el ámbito de Plataforma pero lo ha perdido ya no ve nada.
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await admin.ExecuteAsync(
                "UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = cuenta_id WHERE id = @id", new { id = operatorScope.ProfileScopeId });
        }

        using var revoked = await OpenLimitedAsync(operatorScope.Scope);
        foreach (var table in StructureTables)
        {
            Assert.Equal(0, await CountAsync(revoked, table, centerId));
        }

        // Y no escribe en ninguna de las cuatro (un centro nuevo, para que el único obstáculo sea la política y no una clave repetida).
        var fresh = await SeedCenterWithResidentAsync();
        var freshId = fresh.Profile.CenterId.Value;
        Guid freshUnit;
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            freshUnit = await InsertUnitAsync(admin, freshId);
        }

        var attempts = new Func<Task>[]
        {
            () => InsertUnitAsync(revoked, freshId),
            () => InsertAssignmentAsync(revoked, fresh.Profile.ProfileScopeId, freshId, freshUnit, operatorScope.AccountId),
            () => InsertPrincipalChangeAsync(revoked, freshId, fresh.Profile.ProfileScopeId, 1, operatorScope.AccountId, "PLATAFORMA"),
            () => InsertLogoAsync(revoked, freshId, operatorScope.AccountId, "PLATAFORMA"),
        };
        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<SqlException>(attempt);
            Assert.Equal(BlockPredicateViolation, error.Number);
        }
    }

    [Fact]
    public async Task Plataforma_provisiona_en_otro_centro_firmando_con_su_cuenta_y_su_perfil()
    {
        var center = await SeedCenterWithResidentAsync();
        var operatorScope = await SeedPlatformOperatorAsync();
        var centerId = center.Profile.CenterId.Value;
        using var connection = await OpenLimitedAsync(operatorScope.Scope);

        var unitId = await InsertUnitAsync(connection, centerId);
        await InsertAssignmentAsync(connection, center.Profile.ProfileScopeId, centerId, unitId, operatorScope.AccountId);
        await InsertPrincipalChangeAsync(connection, centerId, center.Profile.ProfileScopeId, 1, operatorScope.AccountId, "PLATAFORMA");
        await InsertLogoAsync(connection, centerId, operatorScope.AccountId, "PLATAFORMA");

        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await CountAsync(admin, "administraciones_principales_cambios", centerId));
        Assert.Equal(1, await CountAsync(admin, "logos_centro", centerId));

        // No puede firmar con la cuenta de otra persona ni con otro perfil.
        var unit2 = await InsertUnitAsync(connection, centerId);
        var otherAccount = await Assert.ThrowsAsync<SqlException>(() =>
            InsertAssignmentAsync(connection, center.Profile.ProfileScopeId, centerId, unit2, center.Profile.AccountId.Value));
        var changeOtherAccount = await Assert.ThrowsAsync<SqlException>(() =>
            InsertPrincipalChangeAsync(connection, centerId, center.Profile.ProfileScopeId, 2, center.Profile.AccountId.Value, "PLATAFORMA", principal: false));
        var changeOtherProfile = await Assert.ThrowsAsync<SqlException>(() =>
            InsertPrincipalChangeAsync(connection, centerId, center.Profile.ProfileScopeId, 2, operatorScope.AccountId, "ADMINISTRACION", principal: false));
        var other = await SeedCenterWithResidentAsync();
        var logoOtherAccount = await Assert.ThrowsAsync<SqlException>(() =>
            InsertLogoAsync(connection, other.Profile.CenterId.Value, other.Profile.AccountId.Value, "PLATAFORMA"));
        var logoOtherProfile = await Assert.ThrowsAsync<SqlException>(() =>
            InsertLogoAsync(connection, other.Profile.CenterId.Value, operatorScope.AccountId, "ADMINISTRACION"));
        Assert.All([otherAccount, changeOtherAccount, changeOtherProfile, logoOtherAccount, logoOtherProfile], error => Assert.Equal(BlockPredicateViolation, error.Number));
    }

    [Fact]
    public async Task Una_Administracion_no_escribe_estructura_en_otro_centro_ni_a_nombre_de_otra_cuenta_o_perfil()
    {
        var a = await SeedCenterWithResidentAsync();
        var b = await SeedCenterWithResidentAsync();
        var centerA = a.Profile.CenterId.Value;
        var centerB = b.Profile.CenterId.Value;
        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));

        // En su centro, firmando como ella misma, sí.
        var unitA = await InsertUnitAsync(connection, centerA);
        await InsertAssignmentAsync(connection, a.Profile.ProfileScopeId, centerA, unitA, a.Profile.AccountId.Value);
        await InsertPrincipalChangeAsync(connection, centerA, a.Profile.ProfileScopeId, 1, a.Profile.AccountId.Value, "ADMINISTRACION");
        await InsertLogoAsync(connection, centerA, a.Profile.AccountId.Value, "ADMINISTRACION");

        var unitA2 = await InsertUnitAsync(connection, centerA);
        Guid unitB;
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            unitB = await InsertUnitAsync(admin, centerB); // una unidad sin asignar: así el único obstáculo del intento es la política
        }

        var attempts = new Func<Task>[]
        {
            () => InsertUnitAsync(connection, centerB),
            () => InsertAssignmentAsync(connection, b.Profile.ProfileScopeId, centerB, unitB, a.Profile.AccountId.Value),
            () => InsertPrincipalChangeAsync(connection, centerB, b.Profile.ProfileScopeId, 1, a.Profile.AccountId.Value, "ADMINISTRACION"),
            () => InsertLogoAsync(connection, centerB, a.Profile.AccountId.Value, "ADMINISTRACION"),
            () => InsertAssignmentAsync(connection, a.Profile.ProfileScopeId, centerA, unitA2, b.Profile.AccountId.Value),
            () => InsertPrincipalChangeAsync(connection, centerA, a.Profile.ProfileScopeId, 2, b.Profile.AccountId.Value, "ADMINISTRACION", principal: false),
            () => InsertPrincipalChangeAsync(connection, centerA, a.Profile.ProfileScopeId, 2, a.Profile.AccountId.Value, "PLATAFORMA", principal: false),
        };
        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<SqlException>(attempt);
            Assert.Equal(BlockPredicateViolation, error.Number);
        }
    }

    [Fact]
    public async Task Ver_la_estructura_de_otro_centro_no_permite_cambiarla_ni_borrarla()
    {
        var center = await SeedCenterWithStructureAsync();
        var operatorScope = await SeedPlatformOperatorAsync();
        var centerId = center.Profile.CenterId.Value;
        using var connection = await OpenLimitedAsync(operatorScope.Scope);

        string? nameBefore;
        using (var reader = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            nameBefore = await reader.ExecuteScalarAsync<string>("SELECT nombre_visible FROM dbo.unidades WHERE id = @id", new { id = center.Profile.UnitId.Value });
        }

        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.unidades SET nombre_visible = N'Cambiada' WHERE id = @id", new { id = center.Profile.UnitId.Value }));
        Assert.Equal(BlockPredicateViolation, update.Number);
        // Borrar lo prohíben ya los disparadores INSTEAD OF de estas tablas (no hay salto de DELETE en la política).
        var deleteAssignment = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.ambitos_perfil_unidad WHERE centro_id = @centerId", new { centerId }));
        var deleteUnit = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.unidades WHERE id = @id", new { id = center.Profile.UnitId.Value }));
        Assert.Equal(50111, deleteAssignment.Number); // TR_pus_no_delete
        Assert.Equal(50431, deleteUnit.Number); // TR_units_no_delete

        // El logo de un centro ya creado ni se ve ni se cambia ni se borra desde Plataforma: las filas no existen para ella.
        Assert.Equal(0, await connection.ExecuteAsync("UPDATE dbo.logos_centro SET tipo = 'JPEG' WHERE centro_id = @centerId", new { centerId }));
        Assert.Equal(0, await connection.ExecuteAsync("DELETE FROM dbo.logos_centro WHERE centro_id = @centerId", new { centerId }));

        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(nameBefore, await admin.ExecuteScalarAsync<string>("SELECT nombre_visible FROM dbo.unidades WHERE id = @id", new { id = center.Profile.UnitId.Value }));
        Assert.Equal(1, await CountAsync(admin, "logos_centro", centerId));
        Assert.Equal("PNG", await admin.ExecuteScalarAsync<string>("SELECT tipo FROM dbo.logos_centro WHERE centro_id = @centerId", new { centerId }));
    }

    private static async Task SetAccountStatusAsync(Guid accountId, string status)
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync("UPDATE dbo.cuentas SET estado = @status WHERE id = @accountId", new { accountId, status });
    }

    [Fact]
    public async Task Con_la_cuenta_suspendida_ni_la_Administracion_ni_Plataforma_ven_ni_escriben_estructura()
    {
        var center = await SeedCenterWithStructureAsync();
        var operatorScope = await SeedPlatformOperatorAsync();
        var centerId = center.Profile.CenterId.Value;

        await SetAccountStatusAsync(center.Profile.AccountId.Value, "SUSPENDED");
        await SetAccountStatusAsync(operatorScope.AccountId, "SUSPENDED");
        try
        {
            foreach (var scope in new[] { ScopeOf(center.Profile), operatorScope.Scope })
            {
                using var suspended = await OpenLimitedAsync(scope);
                foreach (var table in StructureTables)
                {
                    Assert.Equal(0, await CountAsync(suspended, table, centerId));
                }

                var error = await Assert.ThrowsAsync<SqlException>(() => InsertUnitAsync(suspended, centerId));
                Assert.Equal(BlockPredicateViolation, error.Number);
            }
        }
        finally
        {
            await SetAccountStatusAsync(center.Profile.AccountId.Value, "ACTIVE");
            await SetAccountStatusAsync(operatorScope.AccountId, "ACTIVE");
        }

        // Al reactivarlas vuelven a ver.
        using var back = await OpenLimitedAsync(ScopeOf(center.Profile));
        Assert.True(await CountAsync(back, "unidades", centerId) >= 1);
    }

    [Fact]
    public async Task Un_sujeto_con_el_ambito_de_otra_cuenta_no_ve_ni_escribe_estructura()
    {
        var a = await SeedCenterWithStructureAsync();
        var b = await SeedCenterWithStructureAsync();
        var operatorScope = await SeedPlatformOperatorAsync();
        var centerA = a.Profile.CenterId.Value;
        Guid freshUnit;
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            freshUnit = await InsertUnitAsync(admin, centerA); // sin asignar: así el único obstáculo del intento es la política
        }

        // El ámbito es de A, pero el sujeto de la sesión es el de B (o el de otra cuenta con ámbito de Plataforma): no coinciden.
        var forged = new[]
        {
            new TenantScope(b.Profile.ExternalSubject, a.Profile.ProfileScopeId),
            new TenantScope(a.Profile.ExternalSubject, operatorScope.ProfileScopeId),
            new TenantScope(a.Profile.ExternalSubject, b.Profile.ProfileScopeId),
        };
        foreach (var scope in forged)
        {
            using var connection = await OpenLimitedAsync(scope);
            foreach (var table in StructureTables)
            {
                Assert.Equal(0, await CountAsync(connection, table, centerA));
            }

            var unit = await Assert.ThrowsAsync<SqlException>(() => InsertUnitAsync(connection, centerA));
            var assignment = await Assert.ThrowsAsync<SqlException>(() => InsertAssignmentAsync(connection, a.Profile.ProfileScopeId, centerA, freshUnit, a.Profile.AccountId.Value));
            var change = await Assert.ThrowsAsync<SqlException>(() => InsertPrincipalChangeAsync(connection, centerA, a.Profile.ProfileScopeId, 2, a.Profile.AccountId.Value, "ADMINISTRACION", principal: false));
            Assert.All([unit, assignment, change], error => Assert.Equal(BlockPredicateViolation, error.Number));
        }
    }

    [Fact]
    public async Task La_Administracion_con_una_sola_unidad_ve_todas_las_unidades_y_asignaciones_del_centro()
    {
        var center = await SeedCenterWithStructureAsync();
        var centerId = center.Profile.CenterId.Value;
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await InsertUnitAsync(admin, centerId);
            await InsertUnitAsync(admin, centerId);
        }

        await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, center.Profile.CenterId, center.Profile.UnitId);

        // El filtro es por centro, nunca por unidad: ve las 3 unidades y también la asignación de otro perfil a su unidad.
        using var connection = await OpenLimitedAsync(ScopeOf(center.Profile));
        Assert.True(await CountAsync(connection, "unidades", centerId) >= 3);
        Assert.True(await CountAsync(connection, "ambitos_perfil_unidad", centerId) >= 2);
    }

    [Fact]
    public async Task Una_cuenta_con_ambitos_en_dos_centros_solo_ve_la_estructura_del_ambito_activo()
    {
        var a = await SeedCenterWithStructureAsync();
        var b = await SeedCenterWithStructureAsync();
        var secondScope = Guid.NewGuid();
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await admin.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@secondScope, @accountId, @centerB, 'ADMINISTRACION', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { secondScope, accountId = a.Profile.AccountId.Value, centerB = b.Profile.CenterId.Value });
        }

        var asA = new TenantScope(a.Profile.ExternalSubject, a.Profile.ProfileScopeId);
        var asB = new TenantScope(a.Profile.ExternalSubject, secondScope);
        using (var connection = await OpenLimitedAsync(asA))
        {
            foreach (var table in StructureTables)
            {
                Assert.True(await CountAsync(connection, table, a.Profile.CenterId.Value) >= 1);
                Assert.Equal(0, await CountAsync(connection, table, b.Profile.CenterId.Value));
            }
        }

        using (var connection = await OpenLimitedAsync(asB))
        {
            foreach (var table in StructureTables.Where(t => t is "unidades" or "logos_centro" or "administraciones_principales_cambios"))
            {
                Assert.True(await CountAsync(connection, table, b.Profile.CenterId.Value) >= 1);
                Assert.Equal(0, await CountAsync(connection, table, a.Profile.CenterId.Value));
            }
        }
    }

    [Fact]
    public async Task La_Administracion_cambia_la_estructura_de_su_centro_y_no_la_de_otro()
    {
        var a = await SeedCenterWithStructureAsync();
        var b = await SeedCenterWithStructureAsync();
        var centerA = a.Profile.CenterId.Value;
        var centerB = b.Profile.CenterId.Value;
        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));

        // En su centro: renombrar una unidad, revocar una asignación, sustituir y quitar el logo.
        Assert.Equal(1, await connection.ExecuteAsync("UPDATE dbo.unidades SET nombre_visible = N'Renombrada' WHERE id = @id", new { id = a.Profile.UnitId.Value }));
        Assert.Equal(1, await connection.ExecuteAsync(
            "UPDATE dbo.ambitos_perfil_unidad SET revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @account WHERE centro_id = @centerId AND unidad_id = @unit AND revocado_en IS NULL",
            new { account = a.Profile.AccountId.Value, centerId = centerA, unit = a.Profile.UnitId.Value }));
        Assert.Equal(1, await connection.ExecuteAsync("UPDATE dbo.logos_centro SET tipo = 'JPEG' WHERE centro_id = @centerId", new { centerId = centerA }));
        Assert.Equal(1, await connection.ExecuteAsync("DELETE FROM dbo.logos_centro WHERE centro_id = @centerId", new { centerId = centerA }));

        // En el de otro, no existe nada que cambiar: 0 filas.
        Assert.Equal(0, await connection.ExecuteAsync("UPDATE dbo.unidades SET nombre_visible = N'Ajena' WHERE id = @id", new { id = b.Profile.UnitId.Value }));
        Assert.Equal(0, await connection.ExecuteAsync("UPDATE dbo.logos_centro SET tipo = 'JPEG' WHERE centro_id = @centerId", new { centerId = centerB }));
        Assert.Equal(0, await connection.ExecuteAsync("DELETE FROM dbo.logos_centro WHERE centro_id = @centerId", new { centerId = centerB }));
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await CountAsync(admin, "logos_centro", centerB));
    }

    [Fact]
    public async Task Un_logo_no_se_puede_mover_a_otro_centro_ni_la_Administracion_firma_con_otra_cuenta()
    {
        var a = await SeedCenterWithStructureAsync();
        var b = await SeedCenterWithResidentAsync();
        using var connection = await OpenLimitedAsync(ScopeOf(a.Profile));

        var move = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.logos_centro SET centro_id = @other WHERE centro_id = @centerId", new { other = b.Profile.CenterId.Value, centerId = a.Profile.CenterId.Value }));
        Assert.Equal(BlockPredicateViolation, move.Number);

        // Dentro de su propio centro tampoco firma un logo nuevo con la cuenta o el perfil de otra persona.
        await connection.ExecuteAsync("DELETE FROM dbo.logos_centro WHERE centro_id = @centerId", new { centerId = a.Profile.CenterId.Value });
        var otherAccount = await Assert.ThrowsAsync<SqlException>(() => InsertLogoAsync(connection, a.Profile.CenterId.Value, b.Profile.AccountId.Value, "ADMINISTRACION"));
        var otherProfile = await Assert.ThrowsAsync<SqlException>(() => InsertLogoAsync(connection, a.Profile.CenterId.Value, a.Profile.AccountId.Value, "PLATAFORMA"));
        Assert.All([otherAccount, otherProfile], error => Assert.Equal(BlockPredicateViolation, error.Number));
    }

    [Fact]
    public async Task El_procedimiento_de_la_hoja_devuelve_las_unidades_solo_de_una_hoja_viva_y_a_quien_no_tiene_ambito()
    {
        var center = await SeedCenterWithResidentAsync();
        var outsider = await SeedCenterWithResidentAsync(); // una persona de otro centro, sin ámbito en el primero
        var centerId = center.Profile.CenterId.Value;
        string Token() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        string live = Token(), annulled = Token(), redeemed = Token(), expired = Token();
        string unitName;
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            unitName = (await admin.ExecuteScalarAsync<string>("SELECT nombre_visible FROM dbo.unidades WHERE id = @id", new { id = center.Profile.UnitId.Value }))!;
            foreach (var (token, annulledAt, redeemedAt, expiresInDays) in new (string, bool, bool, int)[] { (live, false, false, 2), (annulled, true, false, 2), (redeemed, false, true, 2), (expired, false, false, -1) })
            {
                var id = Guid.NewGuid();
                await admin.ExecuteAsync("""
                    INSERT INTO dbo.vinculaciones_cuenta
                        (id, centro_id, perfil_codigo, resumen_token, emitida_en, emitida_por_cuenta_id, caduca_en, anulada_en, canjeada_en, canjeada_por_cuenta_id)
                    VALUES (@id, @centerId, 'ENFERMERIA', @token, DATEADD(DAY, -5, SYSUTCDATETIME()), @issuer, DATEADD(DAY, @days, SYSUTCDATETIME()),
                            CASE WHEN @annulledAt = 1 THEN SYSUTCDATETIME() END, CASE WHEN @redeemedAt = 1 THEN SYSUTCDATETIME() END, CASE WHEN @redeemedAt = 1 THEN @issuer END);
                    INSERT INTO dbo.vinculaciones_cuenta_unidades (vinculacion_id, centro_id, unidad_id) VALUES (@id, @centerId, @unitId);
                    """, new { id, centerId, token, issuer = center.Profile.AccountId.Value, days = expiresInDays, annulledAt, redeemedAt, unitId = center.Profile.UnitId.Value });
            }
        }

        // Quien no tiene ámbito en ese centro (ni sesión) no lee la tabla de unidades, pero sí el nombre de las de una hoja viva.
        foreach (var scope in new TenantScope?[] { null, ScopeOf(outsider.Profile) })
        {
            using var connection = await OpenLimitedAsync(scope);
            Assert.Equal(0, await CountAsync(connection, "unidades", centerId));
            var now = DateTime.UtcNow;
            Assert.Equal([unitName], (await connection.QueryAsync<string>("EXEC dbo.nombres_unidades_vinculacion @token, @now", new { token = live, now })).ToList());
            foreach (var dead in new[] { annulled, redeemed, expired, Token() })
            {
                Assert.Empty(await connection.QueryAsync<string>("EXEC dbo.nombres_unidades_vinculacion @token, @now", new { token = dead, now }));
            }
        }
    }

    [Fact]
    public async Task Un_perfil_que_no_es_Administracion_ni_Plataforma_no_inserta_unidades_ni_asignaciones()
    {
        var center = await SeedCenterWithResidentAsync();
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, center.Profile.CenterId, center.Profile.UnitId);
        var centerId = center.Profile.CenterId.Value;
        Guid unit;
        using (var admin = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            unit = await InsertUnitAsync(admin, centerId);
        }

        using var connection = await OpenLimitedAsync(ScopeOf(nurse));
        // Ve las unidades de su centro, pero no puede crear una ni darse otra.
        Assert.True(await CountAsync(connection, "unidades", centerId) >= 2);
        var newUnit = await Assert.ThrowsAsync<SqlException>(() => InsertUnitAsync(connection, centerId));
        var selfAssign = await Assert.ThrowsAsync<SqlException>(() => InsertAssignmentAsync(connection, nurse.ProfileScopeId, centerId, unit, nurse.AccountId.Value));
        Assert.All([newUnit, selfAssign], error => Assert.Equal(BlockPredicateViolation, error.Number));
    }
}
