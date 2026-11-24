using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Audit;
using ResidApp.Domain.Platform;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>
/// Tipo de servicio del centro (ADR 0017, script 0113): se elige al crear el centro, queda como columna y como fila 1 de la constancia, y después solo Plataforma corrige un error de alta con el centro
/// sin personas, por el procedimiento <c>corregir_tipo_servicio_centro</c> (única vía: la aplicación no puede escribir la columna). Estas pruebas abren las conexiones con el contexto de sesión de
/// producción (la fábrica con tenant), porque el procedimiento comprueba por sí mismo el ámbito de Plataforma, y las llamadas a mano se hacen suplantando a un usuario como el real de la aplicación.
/// Va en la colección <c>AvisoCambioCita</c> porque una de ellas deja la corrección parada unos segundos con bloqueos sobre la fila del centro, y las pruebas de esa colección recorren todos los centros
/// (<c>reservar_avisos_citas_externas_en</c>): en paralelo, una de ellas era la víctima de un interbloqueo (1205) y fallaba la CI.
/// </summary>
[Collection("AvisoCambioCita")]
public class TipoServicioCentroTests
{
    private const string AppUser = "residapp_tipo_servicio_test";

    private sealed class FixedTenant(TenantScope? scope) : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult(scope);
    }

    private sealed class FixedIdentity(string externalSubject) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
            Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
    }

    private sealed record Operator(string ExternalSubject, AccountId AccountId, Guid ProfileScopeId);

    private sealed record NewCenter(Guid CenterId, string AdminSubject, Guid AdminAccountId);

    // Una sola vez por proceso: cambiar la pertenencia a roles mientras otras pruebas consultan puede bloquearlas.
    private static readonly Lazy<Task> AppUserCreated = new(async () =>
    {
        using var admin = await TestDatabase.ConnectionFactory.OpenAsync();
        await admin.ExecuteAsync($"""
            IF DATABASE_PRINCIPAL_ID(N'{AppUser}') IS NULL CREATE USER {AppUser} WITHOUT LOGIN;
            IF IS_ROLEMEMBER(N'db_datareader', N'{AppUser}') = 0 ALTER ROLE db_datareader ADD MEMBER {AppUser};
            IF IS_ROLEMEMBER(N'residapp_aplicacion', N'{AppUser}') = 0 ALTER ROLE residapp_aplicacion ADD MEMBER {AppUser};
            """);
    });

    private static async Task<Operator> CreateOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var subject = $"test-tipo-{Guid.NewGuid():N}"[..28];
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de tipos de servicio', 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
            """, new { accountId, subject, profileScopeId, centerId = PlatformCenter.Id });
        return new Operator(subject, AccountId.From(accountId), profileScopeId);
    }

    private static TenantScope ScopeOf(Operator op) => new(op.ExternalSubject, op.ProfileScopeId);

    private static SqlConnectionFactory FactoryFor(Operator op) => new(TestDatabase.ConnectionString, new FixedTenant(ScopeOf(op)));

    private static PlatformApplicationService BuildPlatform(Operator op)
    {
        var factory = FactoryFor(op);
        return new PlatformApplicationService(
            new SqlProfileScopeDirectoryProvider(factory), new FixedIdentity(op.ExternalSubject), new SqlPlatformCenterDirectory(factory), new SqlPlatformCenterRepository(factory),
            new SqlAccessInvitationRepository(factory), new SqlCenterModuleRepository(factory), TimeProvider.System, new SqlCenterTimeZones(factory), new SqlCenterTimeZones(factory),
            new SqlCenterServiceTypes(factory));
    }

    private static string Code() => $"tip-{Guid.NewGuid():N}"[..20];

    private static CreateCenterCommand CreateCommand(Operator op, Guid centerId, string adminSubject, CenterServiceType? type) => new(
        op.ProfileScopeId, CenterId.From(PlatformCenter.Id), centerId, Code(), "Centro de tipos (ficticio)", Code(), "Sala 1", adminSubject, "Admin Nuevo", TipoDeServicio: type);

    private static async Task<NewCenter> CreateCenterAsync(Operator op, PlatformApplicationService service, CenterServiceType type)
    {
        var centerId = Guid.NewGuid();
        var adminSubject = $"test-adm-{Guid.NewGuid():N}"[..28];
        var created = await service.CreateCenterAsync(CreateCommand(op, centerId, adminSubject, type));
        Assert.True(created.Ok, created.Error?.Message);
        return new NewCenter(centerId, adminSubject, await ScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @adminSubject", new { adminSubject }));
    }

    private static PlatformCenterQuery QueryOf(Operator op, Guid centerId) => new(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), CenterId.From(centerId));

    private static ChangeCenterServiceTypeCommand ChangeOf(Operator op, Guid centerId, CenterServiceType? type) =>
        new(op.ProfileScopeId, CenterId.From(PlatformCenter.Id), CenterId.From(centerId), type);

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Sin resultado");
    }

    private static Task<string> TypeOfAsync(Guid centerId) => ScalarAsync<string>("SELECT tipo_de_servicio FROM dbo.centros WHERE id = @centerId", new { centerId });

    private const string InsertResident = """
        INSERT INTO dbo.residentes (id, centro_id, nombre_visible, fecha_nacimiento, fecha_ingreso, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil)
        VALUES (NEWID(), @centerId, N'Persona ficticia de tipos', '1940-01-01', '2026-01-01', 'female', 'ACTIVE', SYSUTCDATETIME(), @accountId, 'ADMINISTRACION')
        """;

    private static async Task AddResidentAsync(Guid centerId, Guid accountId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(InsertResident, new { centerId, accountId });
    }

    private static async Task<SqlConnection> OpenAsAppUserAsync(TenantScope? scope)
    {
        await AppUserCreated.Value;
        // Sin pool y sin MARS: una conexión suplantada no vuelve bien al pool y EXECUTE AS falla con sesiones múltiples.
        var impersonable = new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { Pooling = false, MultipleActiveResultSets = false }.ConnectionString;
        var connection = await new SqlConnectionFactory(impersonable, new FixedTenant(scope)).OpenAsync();
        await connection.ExecuteAsync($"EXECUTE AS USER = N'{AppUser}'");
        return connection;
    }

    private const string Correct = "EXEC dbo.corregir_tipo_servicio_centro @centerId, @type, @accountId";

    [Fact]
    public async Task Un_centro_nuevo_guarda_su_tipo_en_la_columna_y_como_fila_1_de_alta_y_un_reenvio_con_otro_tipo_se_rechaza()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var centerId = Guid.NewGuid();
        var adminSubject = $"test-adm-{Guid.NewGuid():N}"[..28];
        var code = Code();
        var unit = Code();
        CreateCenterCommand Create(CenterServiceType type) => new(
            op.ProfileScopeId, CenterId.From(PlatformCenter.Id), centerId, code, "Centro de Día (ficticio)", unit, "Sala 1", adminSubject, "Admin Nuevo", TipoDeServicio: type);

        var created = await service.CreateCenterAsync(Create(CenterServiceType.CentroDeDia));
        Assert.True(created.Ok, created.Error?.Message);

        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(centerId));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var row = await connection.QuerySingleAsync<(int Number, string Type, string Reason, string Profile)>(
            "SELECT numero, tipo_codigo, motivo_codigo, cambiado_por_perfil FROM dbo.tipos_servicio_centro WHERE centro_id = @centerId", new { centerId });
        Assert.Equal((1, "CENTRO_DE_DIA", "ALTA", "PLATAFORMA"), row);

        // El mismo envío repetido no lo duplica; con otro tipo es la misma petición con otros datos.
        Assert.True((await service.CreateCenterAsync(Create(CenterServiceType.CentroDeDia))).Ok);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.tipos_servicio_centro WHERE centro_id = @centerId", new { centerId }));
        Assert.False((await service.CreateCenterAsync(Create(CenterServiceType.Residencia))).Ok);
        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(centerId));
    }

    [Fact]
    public async Task Sin_elegir_el_tipo_de_servicio_el_centro_no_se_crea()
    {
        var op = await CreateOperatorAsync();
        var centerId = Guid.NewGuid();

        var result = await BuildPlatform(op).CreateCenterAsync(CreateCommand(op, centerId, $"test-adm-{Guid.NewGuid():N}"[..28], null));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
        Assert.Equal("Elige el tipo de servicio: Residencia o Centro de Día.", result.Error.Message);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.centros WHERE id = @centerId", new { centerId }));
    }

    [Fact]
    public async Task Un_reenvio_del_alta_con_el_tipo_original_tras_corregirlo_se_rechaza_y_no_deshace_la_correccion()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var centerId = Guid.NewGuid();
        var adminSubject = $"test-adm-{Guid.NewGuid():N}"[..28];
        var code = Code();
        var unit = Code();
        CreateCenterCommand Create(CenterServiceType type) => new(
            op.ProfileScopeId, CenterId.From(PlatformCenter.Id), centerId, code, "Centro reenviado (ficticio)", unit, "Sala 1", adminSubject, "Admin Nuevo", TipoDeServicio: type);
        Assert.True((await service.CreateCenterAsync(Create(CenterServiceType.CentroDeDia))).Ok);
        Assert.True((await service.ChangeCenterServiceTypeAsync(ChangeOf(op, centerId, CenterServiceType.Residencia))).Ok);

        // El navegador reenvía el formulario original (Centro de Día) con el mismo identificador de operación.
        var resent = await service.CreateCenterAsync(Create(CenterServiceType.CentroDeDia));

        Assert.False(resent.Ok);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(centerId));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.tipos_servicio_centro WHERE centro_id = @centerId", new { centerId }));
    }

    [Theory]
    [InlineData(CenterServiceType.Residencia, true)]
    [InlineData(CenterServiceType.CentroDeDia, false)]
    public async Task Un_residente_sin_habitacion_solo_tiene_la_ubicacion_pendiente_en_una_residencia(CenterServiceType type, bool expectedPending)
    {
        var op = await CreateOperatorAsync();
        var center = await CreateCenterAsync(op, BuildPlatform(op), type);
        var scopeId = await ScalarAsync<Guid>("SELECT id FROM dbo.ambitos_perfil WHERE centro_id = @CenterId AND perfil_codigo = 'ADMINISTRACION'", new { center.CenterId });
        var unitId = await ScalarAsync<Guid>("SELECT TOP (1) id FROM dbo.unidades WHERE centro_id = @CenterId", new { center.CenterId });
        var resident = (await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            AccountId.From(center.AdminAccountId), SystemProfile.Administracion, CenterId.From(center.CenterId), UnitId.From(unitId), "Sin habitación (ficticia)",
            new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid(), LocationPending: true))).ResidentId;

        var attention = await new SqlAdministracionResidentDirectory(TestDatabase.ConnectionFactory)
            .ListAttentionAsync(scopeId, CenterId.From(center.CenterId), CenterTime.Today);

        Assert.Equal(expectedPending, attention.Single(row => row.ResidentId == resident).LocationPending);
    }

    [Fact]
    public async Task La_lectura_del_tipo_para_un_ambito_solo_vale_para_su_cuenta_y_su_ambito_vigente_y_no_da_tipo_a_Plataforma()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var day = await CreateCenterAsync(op, service, CenterServiceType.CentroDeDia);
        var residence = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        async Task<Guid> AdminScope(NewCenter center) => await ScalarAsync<Guid>(
            "SELECT id FROM dbo.ambitos_perfil WHERE centro_id = @CenterId AND perfil_codigo = 'ADMINISTRACION'", new { center.CenterId });
        var directory = new SqlCenterServiceTypes(TestDatabase.ConnectionFactory);
        var dayScope = await AdminScope(day);

        Assert.Equal(CenterServiceType.CentroDeDia, await directory.FindForScopeAsync(day.AdminSubject, dayScope));
        Assert.Equal(CenterServiceType.Residencia, await directory.FindForScopeAsync(residence.AdminSubject, await AdminScope(residence)));
        // El ámbito de otra cuenta (una cookie manipulada), un ámbito que no existe y el centro de Plataforma no dan tipo.
        Assert.Null(await directory.FindForScopeAsync(residence.AdminSubject, dayScope));
        Assert.Null(await directory.FindForScopeAsync(day.AdminSubject, Guid.NewGuid()));
        Assert.Null(await directory.FindForScopeAsync(op.ExternalSubject, op.ProfileScopeId));

        // Con la cuenta suspendida o el ámbito revocado, tampoco.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @AdminAccountId", new { day.AdminAccountId });
        Assert.Null(await directory.FindForScopeAsync(day.AdminSubject, dayScope));
        await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'ACTIVE' WHERE id = @AdminAccountId", new { day.AdminAccountId });
        Assert.Equal(CenterServiceType.CentroDeDia, await directory.FindForScopeAsync(day.AdminSubject, dayScope));
        await connection.ExecuteAsync(
            "UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @Op WHERE id = @dayScope", new { Op = op.AccountId.Value, dayScope });
        Assert.Null(await directory.FindForScopeAsync(day.AdminSubject, dayScope));
    }

    [Fact]
    public async Task Dos_correcciones_a_la_vez_a_un_mismo_tipo_dejan_una_sola_fila_y_la_otra_se_rechaza()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);

        var results = await Task.WhenAll(
            Task.Run(() => service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia))),
            Task.Run(() => service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia))));

        Assert.Equal(1, results.Count(result => result.Ok));
        Assert.Equal(ApplicationFailureCode.Conflict, results.Single(result => !result.Ok).Error!.Code);
        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(center.CenterId));
        Assert.Equal([1, 2], (await service.GetCenterServiceTypeAsync(QueryOf(op, center.CenterId))).Value!.History.Select(row => row.Number).Order());
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @CenterId AND accion_codigo = N'CENTER_SERVICE_TYPE_CHANGE'", new { center.CenterId }));
    }

    [Fact]
    public async Task Un_centro_anterior_al_tipo_es_una_Residencia_sin_constancia_y_sin_personas()
    {
        var op = await CreateOperatorAsync();
        var oldCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var state = (await BuildPlatform(op).GetCenterServiceTypeAsync(QueryOf(op, oldCenter.CenterId.Value))).Value!;

        Assert.Equal(CenterServiceType.Residencia, state.Current);
        Assert.Empty(state.History);
        Assert.False(state.HasPeople);
        Assert.True(state.CanBeCorrected);
    }

    [Fact]
    public async Task Plataforma_corrige_un_error_de_alta_en_un_centro_sin_personas_y_quedan_la_constancia_y_la_auditoria()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);

        var first = await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia));
        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(center.CenterId));

        // Puede volver a corregirse mientras siga sin personas: cada corrección es una fila más.
        var second = await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.Residencia));
        Assert.True(second.Ok, second.Error?.Message);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));

        var state = (await service.GetCenterServiceTypeAsync(QueryOf(op, center.CenterId))).Value!;
        Assert.Equal(CenterServiceType.Residencia, state.Current);
        Assert.Equal([3, 2, 1], state.History.Select(row => row.Number));
        Assert.Equal(["ERROR_DE_ALTA", "ERROR_DE_ALTA", "ALTA"], state.History.Select(row => row.Reason));
        Assert.Equal([CenterServiceType.Residencia, CenterServiceType.CentroDeDia, CenterServiceType.Residencia], state.History.Select(row => row.Type));

        // La auditoría lleva solo identificadores: la cuenta, el perfil, el centro y el recurso; ni el tipo ni el motivo.
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var audit = (await connection.QueryAsync<(Guid Account, string Profile, string Resource)>(
            "SELECT cuenta_id, perfil_activo, tipo_recurso FROM dbo.eventos_auditoria WHERE centro_id = @centerId AND accion_codigo = N'CENTER_SERVICE_TYPE_CHANGE'",
            new { center.CenterId })).ToList();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, row => Assert.Equal((op.AccountId.Value, "PLATAFORMA", "CENTER_SERVICE_TYPE"), (row.Account, row.Profile, row.Resource)));
    }

    [Fact]
    public async Task Un_centro_con_personas_ya_no_se_corrige_y_Plataforma_solo_sabe_que_las_tiene()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        await AddResidentAsync(center.CenterId, center.AdminAccountId);

        var state = (await service.GetCenterServiceTypeAsync(QueryOf(op, center.CenterId))).Value!;
        Assert.True(state.HasPeople);
        Assert.False(state.CanBeCorrected);

        var result = await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia));
        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, result.Error!.Code);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.tipos_servicio_centro WHERE centro_id = @centerId", new { center.CenterId }));
    }

    [Fact]
    public async Task Un_familiar_tambien_cuenta_como_persona_aunque_el_centro_no_tenga_residentes()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, creado_por_cuenta_id, creado_en)
                VALUES (NEWID(), @CenterId, N'Familiar ficticio de tipos', N'600000000', @AdminAccountId, SYSUTCDATETIME())
                """, new { center.CenterId, center.AdminAccountId });
        }

        Assert.True((await service.GetCenterServiceTypeAsync(QueryOf(op, center.CenterId))).Value!.HasPeople);
        var result = await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia));
        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, result.Error!.Code);
    }

    [Fact]
    public async Task El_mismo_tipo_el_centro_reservado_y_un_centro_inexistente_se_rechazan_sin_cambiar_nada()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.CentroDeDia);

        var same = await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia));
        Assert.Equal(ApplicationFailureCode.Conflict, same.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, null))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await service.ChangeCenterServiceTypeAsync(ChangeOf(op, PlatformCenter.Id, CenterServiceType.CentroDeDia))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await service.ChangeCenterServiceTypeAsync(ChangeOf(op, Guid.NewGuid(), CenterServiceType.CentroDeDia))).Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, (await service.GetCenterServiceTypeAsync(QueryOf(op, PlatformCenter.Id))).Error!.Code);

        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(center.CenterId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.tipos_servicio_centro WHERE centro_id = @CenterId", new { center.CenterId }));
        Assert.Equal("RESIDENCIA", await TypeOfAsync(PlatformCenter.Id));
    }

    [Fact]
    public async Task Quien_no_es_Plataforma_no_corrige_el_tipo_ni_llamando_a_mano_al_procedimiento_como_el_usuario_de_la_aplicacion()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        var adminScopeId = await ScalarAsync<Guid>(
            "SELECT id FROM dbo.ambitos_perfil WHERE centro_id = @CenterId AND perfil_codigo = 'ADMINISTRACION'", new { center.CenterId });
        var parameters = new { centerId = center.CenterId, type = "CENTRO_DE_DIA", accountId = center.AdminAccountId };

        // La Administración del propio centro, firmando con su cuenta.
        using (var asAdmin = await OpenAsAppUserAsync(new TenantScope(center.AdminSubject, adminScopeId)))
        {
            Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => asAdmin.ExecuteAsync(Correct, parameters))).Message);
            Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => asAdmin.ExecuteAsync("EXEC dbo.centro_tiene_personas @centerId", parameters))).Message);
            // Tampoco cambia la columna por la vía ordinaria: la aplicación tiene DENY de columna.
            Assert.Equal(230, (await Assert.ThrowsAsync<SqlException>(() =>
                asAdmin.ExecuteAsync("UPDATE dbo.centros SET tipo_de_servicio = N'CENTRO_DE_DIA' WHERE id = @centerId", parameters))).Number);
        }

        // Sin ámbito ninguno.
        using (var withoutScope = await OpenAsAppUserAsync(null))
        {
            Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => withoutScope.ExecuteAsync(Correct, parameters))).Message);
        }

        // Plataforma, pero firmando con la cuenta de otra persona.
        using (var asOperator = await OpenAsAppUserAsync(ScopeOf(op)))
        {
            Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => asOperator.ExecuteAsync(Correct, parameters))).Message);
        }

        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
    }

    [Fact]
    public async Task Plataforma_si_corrige_llamando_al_procedimiento_como_el_usuario_de_la_aplicacion_pero_no_puede_dejar_a_mano_una_fila_de_correccion()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        var parameters = new { centerId = center.CenterId, type = "CENTRO_DE_DIA", accountId = op.AccountId.Value };

        using var asOperator = await OpenAsAppUserAsync(ScopeOf(op));
        Assert.False(await asOperator.ExecuteScalarAsync<bool>("EXEC dbo.centro_tiene_personas @centerId", parameters));

        // La aplicación inserta el alta, pero una corrección a mano (sin cambiar la columna) la para el disparador: solo el procedimiento puede.
        var stray = await Assert.ThrowsAsync<SqlException>(() => asOperator.ExecuteAsync("""
            INSERT INTO dbo.tipos_servicio_centro (id, centro_id, numero, tipo_codigo, motivo_codigo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @centerId, 2, N'CENTRO_DE_DIA', N'ERROR_DE_ALTA', @accountId, N'PLATAFORMA', SYSUTCDATETIME())
            """, parameters));
        Assert.Equal("CENTER_SERVICE_TYPE_SEQUENCE_INVALID", stray.Message);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));

        await asOperator.ExecuteAsync(Correct, parameters);
        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(center.CenterId));
    }

    [Fact]
    public async Task La_constancia_no_se_modifica_ni_se_borra_y_el_tipo_no_se_cambia_a_mano_ni_siendo_dueno_sin_constancia_o_con_personas()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        using var owner = await TestDatabase.ConnectionFactory.OpenAsync();
        var parameters = new { centerId = center.CenterId, accountId = op.AccountId.Value };

        Assert.Equal("CENTER_SERVICE_TYPE_HISTORY_IMMUTABLE", (await Assert.ThrowsAsync<SqlException>(() =>
            owner.ExecuteAsync("UPDATE dbo.tipos_servicio_centro SET tipo_codigo = N'CENTRO_DE_DIA' WHERE centro_id = @centerId", parameters))).Message);
        Assert.Equal("CENTER_SERVICE_TYPE_HISTORY_IMMUTABLE", (await Assert.ThrowsAsync<SqlException>(() =>
            owner.ExecuteAsync("DELETE FROM dbo.tipos_servicio_centro WHERE centro_id = @centerId", parameters))).Message);

        // Defensa en profundidad: ni siendo dueño se cambia la columna sin una fila de constancia con ese tipo como la última...
        Assert.Equal("CENTER_SERVICE_TYPE_HISTORY_MISSING", (await Assert.ThrowsAsync<SqlException>(() =>
            owner.ExecuteAsync("UPDATE dbo.centros SET tipo_de_servicio = N'CENTRO_DE_DIA' WHERE id = @centerId", parameters))).Message);

        // ...ni con la constancia puesta si el centro ya tiene personas.
        await AddResidentAsync(center.CenterId, center.AdminAccountId);
        await owner.ExecuteAsync("""
            INSERT INTO dbo.tipos_servicio_centro (id, centro_id, numero, tipo_codigo, motivo_codigo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @centerId, 2, N'CENTRO_DE_DIA', N'ERROR_DE_ALTA', @accountId, N'PLATAFORMA', SYSUTCDATETIME())
            """, parameters);
        Assert.Equal("CENTER_SERVICE_TYPE_NOT_EMPTY", (await Assert.ThrowsAsync<SqlException>(() =>
            owner.ExecuteAsync("UPDATE dbo.centros SET tipo_de_servicio = N'CENTRO_DE_DIA' WHERE id = @centerId", parameters))).Message);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
    }

    [Fact]
    public async Task Un_alta_de_residente_a_medias_se_espera_y_el_centro_ya_no_esta_vacio()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);

        using var inFlight = await TestDatabase.ConnectionFactory.OpenAsync();
        using var transaction = inFlight.BeginTransaction();
        await inFlight.ExecuteAsync(InsertResident, new { centerId = center.CenterId, accountId = center.AdminAccountId }, transaction);

        // La corrección empieza con el alta del residente sin confirmar: no la ve (no está confirmada) pero tampoco decide sin ella; espera.
        var change = Task.Run(() => service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia)));
        await Task.Delay(1500);
        Assert.False(change.IsCompleted, "La corrección debe esperar al alta de residente que está a medias");
        transaction.Commit();

        var result = await change.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, result.Error!.Code);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
    }

    [Fact]
    public async Task Con_una_correccion_a_medias_un_alta_de_residente_espera_a_que_termine_y_nunca_se_cuela_en_medio()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);

        // La corrección se hace dentro de una transacción que no se confirma todavía: el bloqueo de la fila del centro dura hasta el final.
        using var correcting = await FactoryFor(op).OpenAsync();
        using var transaction = correcting.BeginTransaction();
        await correcting.ExecuteAsync(Correct, new { centerId = center.CenterId, type = "CENTRO_DE_DIA", accountId = op.AccountId.Value }, transaction);

        var addResident = Task.Run(() => AddResidentAsync(center.CenterId, center.AdminAccountId));
        await Task.Delay(1500);
        Assert.False(addResident.IsCompleted, "El alta del residente debe esperar a que termine la corrección del tipo");
        transaction.Commit();

        await addResident.WaitAsync(TimeSpan.FromSeconds(30));
        // El centro estaba vacío cuando se cambió; el residente entra después, ya en un Centro de Día.
        Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(center.CenterId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.residentes WHERE centro_id = @CenterId", new { center.CenterId }));
    }

    [Fact]
    public void La_correccion_del_tipo_es_una_accion_de_estructura_que_ve_Administracion()
    {
        // ADM-28 / AUD-01: la pantalla promete «queda en la auditoría del centro»; la lista cerrada de acciones que ve Administración tiene que incluirla (y AuditActionDisplayTests exige su etiqueta).
        Assert.Contains(("CENTER_SERVICE_TYPE_CHANGE", AuditCategory.Estructura), AdministrativeAudit.Actions);
    }

    [Fact]
    public async Task Con_personas_el_usuario_de_la_aplicacion_ve_el_centro_no_vacio_aunque_la_seguridad_por_filas_oculte_a_las_personas()
    {
        // La trampa principal del diseño: con los permisos de la aplicación, Plataforma no ve a ninguna persona del centro (seguridad por filas) y un centro con personas parecería vacío.
        // El procedimiento lee con los permisos del dueño; si no lo hiciera, esta prueba dejaría corregir el tipo.
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        await AddResidentAsync(center.CenterId, center.AdminAccountId);
        var parameters = new { centerId = center.CenterId, type = "CENTRO_DE_DIA", accountId = op.AccountId.Value };

        using var asOperator = await OpenAsAppUserAsync(ScopeOf(op));
        Assert.Equal(0, await asOperator.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.residentes WHERE centro_id = @centerId", parameters));
        Assert.True(await asOperator.ExecuteScalarAsync<bool>("EXEC dbo.centro_tiene_personas @centerId", parameters));
        Assert.Equal("CENTER_SERVICE_TYPE_NOT_EMPTY", (await Assert.ThrowsAsync<SqlException>(() => asOperator.ExecuteAsync(Correct, parameters))).Message);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
    }

    [Theory]
    [InlineData("baja")]
    [InlineData("anonimizado")]
    public async Task Un_residente_dado_de_baja_o_un_familiar_anonimizado_tambien_cuentan_como_persona(string kind)
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            if (kind == "baja")
            {
                await connection.ExecuteAsync("""
                    INSERT INTO dbo.residentes (id, centro_id, nombre_visible, fecha_nacimiento, fecha_ingreso, sexo_documentado_codigo, estado, creado_en, creado_por_cuenta_id, creado_por_perfil,
                                                motivo_inactivacion, inactivado_en, inactivado_por_cuenta_id, inactivado_por_perfil)
                    VALUES (NEWID(), @CenterId, N'Persona ficticia de baja', '1940-01-01', '2026-01-01', 'female', 'INACTIVE', SYSUTCDATETIME(), @AdminAccountId, 'ADMINISTRACION',
                            N'Baja de prueba', SYSUTCDATETIME(), @AdminAccountId, 'ADMINISTRACION')
                    """, new { center.CenterId, center.AdminAccountId });
            }
            else
            {
                await connection.ExecuteAsync("""
                    INSERT INTO dbo.familiares (id, centro_id, nombre_visible, telefono, creado_por_cuenta_id, creado_en, anonimizado_en, anonimizado_por_cuenta_id, anonimizado_motivo_codigo)
                    VALUES (NEWID(), @CenterId, N'Persona anonimizada', N'-', @AdminAccountId, SYSUTCDATETIME(), SYSUTCDATETIME(), @AdminAccountId, 'INICIATIVA_DEL_CENTRO')
                    """, new { center.CenterId, center.AdminAccountId });
            }
        }

        Assert.True((await service.GetCenterServiceTypeAsync(QueryOf(op, center.CenterId))).Value!.HasPeople);
        var result = await service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia));
        Assert.Equal(ApplicationFailureCode.Conflict, result.Error!.Code);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
    }

    [Fact]
    public async Task Un_centro_anterior_al_script_se_corrige_con_la_fila_1_de_correccion_y_se_puede_volver_atras()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var oldCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var centerId = oldCenter.CenterId.Value;

        Assert.True((await service.ChangeCenterServiceTypeAsync(ChangeOf(op, centerId, CenterServiceType.CentroDeDia))).Ok);
        Assert.True((await service.ChangeCenterServiceTypeAsync(ChangeOf(op, centerId, CenterServiceType.Residencia))).Ok);

        var state = (await service.GetCenterServiceTypeAsync(QueryOf(op, centerId))).Value!;
        Assert.Equal(CenterServiceType.Residencia, state.Current);
        Assert.Equal([2, 1], state.History.Select(row => row.Number));
        Assert.Equal([CenterServiceType.Residencia, CenterServiceType.CentroDeDia], state.History.Select(row => row.Type));
        Assert.All(state.History, row => Assert.Equal("ERROR_DE_ALTA", row.Reason));
    }

    [Fact]
    public async Task La_constancia_rechaza_con_su_motivo_un_salto_de_numero_el_mismo_tipo_un_alta_que_no_es_la_primera_y_el_centro_reservado()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        using var owner = await TestDatabase.ConnectionFactory.OpenAsync();

        async Task<SqlException> InsertAsync(Guid centerId, int number, string type, string reason) => await Assert.ThrowsAsync<SqlException>(() => owner.ExecuteAsync("""
            INSERT INTO dbo.tipos_servicio_centro (id, centro_id, numero, tipo_codigo, motivo_codigo, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @centerId, @number, @type, @reason, @accountId, N'PLATAFORMA', SYSUTCDATETIME())
            """, new { centerId, number, type, reason, accountId = op.AccountId.Value }));

        // Salto de número (no hay fila 2) y mismo tipo que la fila anterior.
        Assert.Equal("CENTER_SERVICE_TYPE_SEQUENCE_INVALID", (await InsertAsync(center.CenterId, 3, "CENTRO_DE_DIA", "ERROR_DE_ALTA")).Message);
        Assert.Equal("CENTER_SERVICE_TYPE_SEQUENCE_INVALID", (await InsertAsync(center.CenterId, 2, "RESIDENCIA", "ERROR_DE_ALTA")).Message);
        // El alta solo puede ser la fila 1 (restricción de la tabla) y el centro reservado no es un servicio.
        Assert.Equal(547, (await InsertAsync(center.CenterId, 2, "CENTRO_DE_DIA", "ALTA")).Number);
        Assert.Equal("CENTER_SERVICE_TYPE_CENTER_INVALID", (await InsertAsync(PlatformCenter.Id, 1, "RESIDENCIA", "ALTA")).Message);
        Assert.Equal(547, (await InsertAsync(center.CenterId, 2, "OTRO", "ERROR_DE_ALTA")).Number);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.tipos_servicio_centro WHERE centro_id = @CenterId", new { center.CenterId }));
    }

    [Theory]
    [InlineData("CENTRO_DE_DIA ")]
    [InlineData("centro_de_dia")]
    [InlineData("")]
    public async Task El_procedimiento_no_acepta_un_tipo_que_no_sea_uno_de_los_dos_valores_ni_guarda_un_valor_con_espacio_final(string type)
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        using var asOperator = await OpenAsAppUserAsync(ScopeOf(op));

        var parameters = new { centerId = center.CenterId, type, accountId = op.AccountId.Value };
        if (type.Trim() == "CENTRO_DE_DIA")
        {
            // Con un espacio final, SQL Server lo compara como igual: pasa, pero se guarda siempre el valor canónico (sin el espacio).
            await asOperator.ExecuteAsync(Correct, parameters);
            Assert.Equal("CENTRO_DE_DIA", await TypeOfAsync(center.CenterId));
            Assert.Equal(13, await ScalarAsync<int>("SELECT DATALENGTH(tipo_de_servicio) / 2 FROM dbo.centros WHERE id = @CenterId", new { center.CenterId }));
        }
        else
        {
            Assert.Equal("CENTER_SERVICE_TYPE_INVALID", (await Assert.ThrowsAsync<SqlException>(() => asOperator.ExecuteAsync(Correct, parameters))).Message);
            Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
        }
    }

    [Fact]
    public async Task Con_la_correccion_parada_antes_de_cambiar_la_columna_un_alta_de_residente_no_se_cuela()
    {
        // La ventana que protege el bloqueo de la fila del centro: ya se ha comprobado que no hay personas y aún no se ha cambiado el tipo. Para parar el procedimiento justo ahí, otra conexión
        // retiene la fila de constancia que el procedimiento lee un poco después (la numeración). Un alta de residente que empiece entonces tiene que esperar: sin el bloqueo de la fila del centro
        // (con un bloqueo de lectura, que no impide la comprobación de la clave foránea) terminaría antes de que el tipo cambie.
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);

        using var holder = await TestDatabase.ConnectionFactory.OpenAsync();
        using var holding = holder.BeginTransaction();
        await holder.ExecuteAsync("SELECT TOP (1) id FROM dbo.tipos_servicio_centro WITH (XLOCK, ROWLOCK) WHERE centro_id = @CenterId", new { center.CenterId }, holding);

        var change = Task.Run(() => service.ChangeCenterServiceTypeAsync(ChangeOf(op, center.CenterId, CenterServiceType.CentroDeDia)));
        await Task.Delay(1500);
        Assert.False(change.IsCompleted, "La corrección debería estar parada leyendo la constancia");

        var addResident = Task.Run(() => AddResidentAsync(center.CenterId, center.AdminAccountId));
        await Task.Delay(1500);
        Assert.False(addResident.IsCompleted, "El alta del residente no puede terminar mientras la corrección tiene la fila del centro");

        holding.Commit();
        // Al soltar, cada uno sigue por su lado: según quién gane puede haber un interbloqueo (error 1205) y uno de los dos se repite; nunca otro error.
        foreach (var task in new[] { change, addResident })
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (SqlException error) when (error.Number == 1205)
            {
            }
        }
    }

    [Fact]
    public async Task Quien_no_es_Plataforma_no_corrige_ni_tras_revocar_su_ambito_ni_estando_suspendida_su_cuenta()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.Residencia);
        var parameters = new { centerId = center.CenterId, type = "CENTRO_DE_DIA", accountId = op.AccountId.Value };

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @accountId", parameters);
        }

        try
        {
            using var suspended = await OpenAsAppUserAsync(ScopeOf(op));
            Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => suspended.ExecuteAsync(Correct, parameters))).Message);
            Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => suspended.ExecuteAsync("EXEC dbo.centro_tiene_personas @centerId", parameters))).Message);
        }
        finally
        {
            using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'ACTIVE' WHERE id = @accountId", parameters);
        }

        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @account WHERE id = @scope", new { scope = op.ProfileScopeId, account = op.AccountId.Value });
        }

        using var revoked = await OpenAsAppUserAsync(ScopeOf(op));
        Assert.Equal("CENTER_SERVICE_TYPE_FORBIDDEN", (await Assert.ThrowsAsync<SqlException>(() => revoked.ExecuteAsync(Correct, parameters))).Message);
        Assert.Equal("RESIDENCIA", await TypeOfAsync(center.CenterId));
    }

    [Fact]
    public async Task Plataforma_ve_el_tipo_en_la_lista_de_centros_y_en_el_ambito_del_centro_pero_el_ambito_de_Plataforma_no_lleva_tipo()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op);
        var center = await CreateCenterAsync(op, service, CenterServiceType.CentroDeDia);

        var listed = (await service.ListCentersAsync(new PlatformQuery(op.ProfileScopeId, CenterId.From(PlatformCenter.Id)))).Value!;
        Assert.Equal(CenterServiceType.CentroDeDia, listed.Single(c => c.CenterId.Value == center.CenterId).ServiceType);
        Assert.DoesNotContain(listed, c => c.CenterId.Value == PlatformCenter.Id);

        var detail = (await service.FindCenterAsync(QueryOf(op, center.CenterId))).Value!;
        Assert.Equal(CenterServiceType.CentroDeDia, detail.ServiceType);

        var provider = new SqlProfileScopeDirectoryProvider(FactoryFor(op));
        Assert.Null(Assert.Single(await provider.ListActiveAsync(op.ExternalSubject)).ServiceType);
        Assert.Equal(CenterServiceType.CentroDeDia, Assert.Single(await provider.ListActiveAsync(center.AdminSubject)).ServiceType);
    }
}
