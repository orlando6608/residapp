using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
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
}
