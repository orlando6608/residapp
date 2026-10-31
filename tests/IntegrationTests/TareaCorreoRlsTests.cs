using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Ports;
using ResidApp.Domain.Appointments;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using static ResidApp.IntegrationTests.CitasTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>La identidad propia de la tarea programada del aviso de cambio (script 0083; docs/privacidad/tarea-correo-identidad-rls.md). Como en RlsCentroTests, se suplanta con
/// EXECUTE AS un usuario sin login: uno miembro del rol residapp_tareas (el real, en Azure, es un usuario contenido con contraseña) y otro con lectura general pero sin el rol ni ámbito.
/// Las consultas que se ejecutan son las reales del almacén (SqlChangeNoticeStore), no copias: si cambian de columna, esta prueba se rompe.</summary>
[Collection("AvisoCambioCita")]
public sealed class TareaCorreoRlsTests
{
    private const string TaskUser = "residapp_tareas_test";
    private const string NoRoleUser = "residapp_sinrol_test";
    private const string FamilyAddress = "familiar.ficticio@example.test";

    private sealed class NoTenant : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult<TenantScope?>(null);
    }

    // Una sola vez por proceso: cambiar la pertenencia a roles mientras otras pruebas consultan puede bloquearlas.
    private static readonly Lazy<Task> UsersCreated = new(async () =>
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{TaskUser}') IS NULL CREATE USER {TaskUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'residapp_tareas', N'{TaskUser}') = 0 ALTER ROLE residapp_tareas ADD MEMBER {TaskUser};
            IF DATABASE_PRINCIPAL_ID(N'{NoRoleUser}') IS NULL CREATE USER {NoRoleUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'db_datareader', N'{NoRoleUser}') = 0 ALTER ROLE db_datareader ADD MEMBER {NoRoleUser};
            """);
    });

    private static async Task<SqlConnection> OpenAsAsync(string user)
    {
        await UsersCreated.Value;
        // Sin pool ni MARS: ver RlsCentroTests.
        var impersonable = new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;
        var connection = await new SqlConnectionFactory(impersonable, new NoTenant()).OpenAsync();
        await connection.ExecuteAsync($"EXECUTE AS USER = N'{user}'");
        return connection;
    }

    /// <summary>Un centro con una cita reservada por la familia y un aviso de cambio reservado (la fila PENDIENTE).</summary>
    private sealed record Notice(Guid CenterId, Guid AppointmentId, Guid AccountId, Guid LinkId, Guid EmailId);

    private static async Task<Notice> SeedAsync(string name)
    {
        var setup = await SetupAsync(name);
        await ConfigureAsync(setup);
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = @FamilyAddress WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId)", new { FamilyAddress, linkId = setup.LinkId });
        var booked = await BookAsync(setup.Subject, setup.LinkId, (await SlotsAsync(setup.Subject, setup.LinkId))[0]);
        Assert.True(booked.Ok, booked.Error?.Message);
        await new SqlChangeNoticeStore(TestDatabase.ConnectionFactory).RegisterAsync(setup.Admin.CenterId, booked.Value!.Id, 1, "es", "1", DateTimeOffset.UtcNow);
        return await admin.QuerySingleAsync<Notice>("""
            SELECT c.centro_id AS CenterId, c.id AS AppointmentId, c.cuenta_id AS AccountId, c.vinculo_id AS LinkId, e.id AS EmailId
              FROM dbo.citas c JOIN dbo.envios_correo e ON e.referencia_id = c.id AND e.plantilla_codigo = 'AVISO_CAMBIO_CITA' WHERE c.id = @id
            """, new { id = booked.Value.Id });
    }

    private static async Task AssertDeniedAsync(SqlConnection connection, string sql)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql));
        Assert.Contains(error.Number, new[] { 229, 230 }); // permiso denegado sobre la tabla o sobre una columna
    }

    [Fact]
    public async Task ElRolDeLaTarea_EjecutaLasConsultasRealesDelAviso_EnVariosCentros()
    {
        var first = await SeedAsync("Residente Tarea Correo Uno");
        var second = await SeedAsync("Residente Tarea Correo Dos");
        using var task = await OpenAsAsync(TaskUser);

        var due = (await task.QueryAsync<(Guid Id, Guid CenterId, Guid AppointmentId, string ChangeKey, string Language)>(
            SqlChangeNoticeStore.ListDueSql, new { limit = 1000, template = EmailLog.ChangeTemplate, before = DateTime.UtcNow.AddMinutes(5) })).ToList();
        Assert.Contains(due, d => d.Id == first.EmailId && d.CenterId == first.CenterId);
        Assert.Contains(due, d => d.Id == second.EmailId && d.CenterId == second.CenterId);

        foreach (var notice in new[] { first, second })
        {
            Assert.Equal(1, await task.ExecuteAsync(SqlChangeNoticeStore.ClaimSql, new { id = notice.EmailId }));
            var change = await task.QuerySingleAsync(SqlChangeNoticeStore.ChangeSql, new { appointmentId = notice.AppointmentId, center = notice.CenterId, number = 1 });
            Assert.Equal("CONFIRMADA", (string)change.Status);
            var recipient = await task.QuerySingleAsync(SqlChangeNoticeStore.RecipientSql, new
            {
                AccountId = notice.AccountId, CenterId = notice.CenterId, Today = DateTime.Today, LinkId = notice.LinkId,
            });
            Assert.Equal(FamilyAddress, (string)recipient.Address);
            Assert.Equal(1, await task.ExecuteAsync(SqlChangeNoticeStore.ResolveSql, new
            {
                id = notice.EmailId, state = "ENVIADO", reason = (string?)null, operationId = "op", attempts = 1, now = DateTime.UtcNow,
            }));
        }
    }

    [Fact]
    public async Task ElRolDeLaTarea_NoPuedeLeerElMotivo_NiNombresNiTelefonos_NiTablasClinicas_NiCuentas()
    {
        await SeedAsync("Residente Tarea Correo Tres");
        using var task = await OpenAsAsync(TaskUser);

        foreach (var sql in new[]
        {
            "SELECT motivo FROM dbo.citas_cambios",
            "SELECT * FROM dbo.citas_cambios",
            "SELECT TOP (1) c.id FROM dbo.citas_cambios c WHERE c.motivo IS NOT NULL",
            "SELECT * FROM dbo.familiares",
            "SELECT telefono FROM dbo.familiares",
            "SELECT nombre_visible FROM dbo.residentes",
            "SELECT * FROM dbo.citas",
            "SELECT * FROM dbo.eventos_auditoria",
            "SELECT * FROM dbo.cuentas",
            "SELECT * FROM dbo.ambitos_perfil",
            "SELECT * FROM dbo.citas_solicitudes",
        })
        {
            await AssertDeniedAsync(task, sql);
        }
    }

    [Fact]
    public async Task ElRolDeLaTarea_NoInsertaNiBorraNiCambiaLaIdentidadDeUnaFila_SoloLasColumnasDeResolucion()
    {
        var notice = await SeedAsync("Residente Tarea Correo Cuatro");
        using var task = await OpenAsAsync(TaskUser);

        await AssertDeniedAsync(task, $"DELETE FROM dbo.envios_correo WHERE id = '{notice.EmailId}'");
        await AssertDeniedAsync(task, $"UPDATE dbo.envios_correo SET centro_id = centro_id WHERE id = '{notice.EmailId}'");
        await AssertDeniedAsync(task, $"UPDATE dbo.envios_correo SET referencia_clave = N'9' WHERE id = '{notice.EmailId}'");
        await AssertDeniedAsync(task, $"UPDATE dbo.citas SET estado_codigo = 'CANCELADA' WHERE id = '{notice.AppointmentId}'");
        await AssertDeniedAsync(task, $"UPDATE dbo.familiares SET correo = N'otra@example.test' WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = '{notice.LinkId}')");
        await AssertDeniedAsync(task, $"""
            INSERT INTO dbo.envios_correo (id, centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, referencia_clave, estado_codigo, intentos, creado_en)
            SELECT NEWID(), centro_id, plantilla_codigo, plantilla_version, idioma, familiar_id, referencia_id, N'77', 'PENDIENTE', 0, SYSUTCDATETIME() FROM dbo.envios_correo WHERE id = '{notice.EmailId}'
            """);
    }

    [Fact]
    public async Task SinElRolNiAmbito_NoSeVeNingunAviso_YLaTareaLoDetecta()
    {
        await SeedAsync("Residente Tarea Correo Cinco");
        using var noRole = await OpenAsAsync(NoRoleUser);
        using var task = await OpenAsAsync(TaskUser);

        Assert.Equal(0, await noRole.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.envios_correo"));
        Assert.Equal(0, await noRole.ExecuteScalarAsync<int>(SqlChangeNoticeStore.IdentitySql));
        Assert.Equal(1, await task.ExecuteScalarAsync<int>(SqlChangeNoticeStore.IdentitySql));
        Assert.True(await new SqlChangeNoticeStore(TestDatabase.ConnectionFactory).HasTaskIdentityAsync()); // db_owner, como en local y en las pruebas
    }

    [Fact]
    public async Task ElUsuarioDeLaAplicacion_NoEsDelRolDeLaTarea()
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await UsersCreated.Value;

        Assert.Equal(0, await admin.ExecuteScalarAsync<int>($"SELECT CASE WHEN IS_ROLEMEMBER(N'residapp_tareas', N'{NoRoleUser}') = 1 THEN 1 ELSE 0 END"));
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.database_role_members m WHERE m.role_principal_id = DATABASE_PRINCIPAL_ID(N'residapp_tareas') AND USER_NAME(m.member_principal_id) NOT LIKE N'residapp_tarea%test%' AND USER_NAME(m.member_principal_id) <> N'residapp_tarea'"));
    }
}
