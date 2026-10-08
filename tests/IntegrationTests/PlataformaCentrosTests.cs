using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Perfil de plataforma (script 0026): alta de un centro con su primera unidad y su primer administrador, y sus límites.
/// Cada prueba crea su propio operador; los centros que se crean no se borran (nada se borra), pero llevan códigos únicos.</summary>
public class PlataformaCentrosTests
{
    private sealed record Operator(string ExternalSubject, AccountId AccountId, Guid ProfileScopeId)
    {
        public CenterId CenterId => CenterId.From(PlatformCenter.Id);
    }

    private static async Task<Operator> CreateOperatorAsync()
    {
        var accountId = Guid.NewGuid();
        var profileScopeId = Guid.NewGuid();
        var subject = $"test-plat-{Guid.NewGuid():N}"[..28];
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de pruebas', 'ACTIVE', @now);
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@profileScopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', @now, @accountId);
            """, new { accountId, subject, now, profileScopeId, centerId = PlatformCenter.Id });
        return new Operator(subject, AccountId.From(accountId), profileScopeId);
    }

    private static PlatformApplicationService BuildPlatform(string subject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedPlatformSessionIdentityProvider(subject),
        new SqlPlatformCenterDirectory(TestDatabase.ConnectionFactory), new SqlPlatformCenterRepository(TestDatabase.ConnectionFactory));

    private static string NewCode() => $"cen-{Guid.NewGuid():N}"[..20];

    private static string NewSubject() => $"test-adm-{Guid.NewGuid():N}"[..28];

    private static CreateCenterCommand Create(
        Operator op, string? centerCode = null, string? adminSubject = null, Guid? operationId = null, string centerName = "Residencia Nueva (ficticia)") =>
        new(op.ProfileScopeId, op.CenterId, operationId ?? Guid.NewGuid(), centerCode ?? NewCode(), centerName, NewCode(),
            "Primera unidad", adminSubject ?? NewSubject(), "Admin Nuevo");

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task Alta_CreaCentroUnidadYAdministrador_ConAuditoria_YElAdministradorTrabajaDeExtremoAExtremo()
    {
        var op = await CreateOperatorAsync();
        var centerId = Guid.NewGuid();
        var subject = NewSubject();
        var code = NewCode();

        var created = await BuildPlatform(op.ExternalSubject).CreateCenterAsync(Create(op, code, subject, centerId));

        Assert.True(created.Ok, created.Error?.Message);
        Assert.Equal(centerId, created.Value.Value);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.centros WHERE id = @centerId AND codigo = @code AND estado = 'ACTIVE'", new { centerId, code = code.ToUpperInvariant() }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.unidades WHERE centro_id = @centerId AND estado = 'ACTIVE'", new { centerId }));
        foreach (var action in new[] { "CENTER_CREATE", "UNIT_CREATE", "ACCOUNT_CREATE", "PROFILE_SCOPE_GRANT", "PROFILE_UNIT_GRANT" })
        {
            Assert.Equal(1, await CountAsync(
                "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE centro_id = @centerId AND accion_codigo = @action AND perfil_activo = 'PLATAFORMA' AND cuenta_id = @actor",
                new { centerId, action, actor = op.AccountId.Value }));
        }

        // El administrador nuevo entra con su ámbito, ve su unidad, crea otra y da de alta un residente.
        var scopes = await new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory).ListActiveAsync(subject);
        var scope = Assert.Single(scopes);
        Assert.Equal(SystemProfile.Administracion, scope.Profile);
        Assert.Equal(centerId, scope.CenterId.Value);
        var admin = BuildEstructura(subject);
        var query = new AdministracionQuery(scope.ProfileScopeId, scope.CenterId);
        var units = (await admin.ListStructureUnitsAsync(query)).Value!;
        var first = Assert.Single(units);
        var second = await admin.CreateUnitAsync(new CreateUnitCommand(scope.ProfileScopeId, scope.CenterId, Guid.NewGuid(), NewCode(), "Segunda unidad"));
        Assert.True(second.Ok, second.Error?.Message);
        var seeded = new SeededProfile(subject, scope.AccountId, scope.CenterId, first.UnitId, scope.ProfileScopeId);
        await CreateResidentAsync(seeded, "Residente del centro nuevo");
        var residents = await Build(subject).ListResidentsAsync(query);
        Assert.Single(residents.Value!);

        var listed = (await BuildPlatform(op.ExternalSubject).ListCentersAsync(new PlatformQuery(op.ProfileScopeId, op.CenterId))).Value!;
        var summary = Assert.Single(listed, c => c.CenterId.Value == centerId);
        Assert.Equal(2, summary.UnitCount);
        Assert.DoesNotContain(listed, c => c.CenterId.Value == PlatformCenter.Id);
    }

    [Fact]
    public async Task Reenvio_NoDuplica_YConOtrosDatosEsConflicto()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op.ExternalSubject);
        var centerId = Guid.NewGuid();
        var command = Create(op, operationId: centerId);

        var first = await service.CreateCenterAsync(command);
        var again = await service.CreateCenterAsync(command);
        var reused = await service.CreateCenterAsync(command with { NombreCentro = "Otro nombre" });
        var otherAdmin = await service.CreateCenterAsync(command with { IdentificadorAdministrador = NewSubject() });

        Assert.True(first.Ok, first.Error?.Message);
        Assert.True(again.Ok, again.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, otherAdmin.Error!.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.centros WHERE id = @centerId", new { centerId }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.unidades WHERE centro_id = @centerId", new { centerId }));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE centro_id = @centerId AND accion_codigo = 'CENTER_CREATE'", new { centerId }));
        Assert.Equal(ApplicationFailureCode.Conflict, reused.Error!.Code);
    }

    [Fact]
    public async Task CodigoOIdentificadorRepetidos_SeRechazan_SinDejarNadaAMedias()
    {
        var op = await CreateOperatorAsync();
        var service = BuildPlatform(op.ExternalSubject);
        var code = NewCode();
        var subject = NewSubject();
        Assert.True((await service.CreateCenterAsync(Create(op, code, subject))).Ok);

        var sameCode = Create(op, code.ToUpperInvariant());
        var sameSubject = Create(op, adminSubject: subject.ToUpperInvariant());
        var codeResult = await service.CreateCenterAsync(sameCode);
        var subjectResult = await service.CreateCenterAsync(sameSubject);

        Assert.Equal(ApplicationFailureCode.Conflict, codeResult.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, subjectResult.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.centros WHERE id IN (@a, @b)", new { a = sameCode.OperacionId, b = sameSubject.OperacionId }));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.unidades WHERE centro_id IN (@a, @b)", new { a = sameCode.OperacionId, b = sameSubject.OperacionId }));
    }

    [Theory]
    [InlineData("PLATAFORMA", "Centro", "unidad-1", "Unidad", "admin-valido", "Admin")]
    [InlineData("con espacio", "Centro", "unidad-1", "Unidad", "admin-valido", "Admin")]
    [InlineData("centro-1", " ", "unidad-1", "Unidad", "admin-valido", "Admin")]
    [InlineData("centro-1", "Centro", "u", "Unidad", "admin-valido", "Admin")]
    [InlineData("centro-1", "Centro", "unidad-1", "Unidad", "ab", "Admin")]
    [InlineData("centro-1", "Centro", "unidad-1", "Unidad", "admin-valido", " ")]
    public async Task DatosInvalidos_SeRechazan(string centerCode, string centerName, string unitCode, string unitName, string subject, string adminName)
    {
        var op = await CreateOperatorAsync();
        var command = new CreateCenterCommand(op.ProfileScopeId, op.CenterId, Guid.NewGuid(), centerCode, centerName, unitCode, unitName, subject, adminName);

        var result = await BuildPlatform(op.ExternalSubject).CreateCenterAsync(command);

        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.centros WHERE id = @id", new { id = command.OperacionId }));
    }

    [Fact]
    public async Task OtrosPerfiles_YUnOperadorSuspendido_DanAccesoDenegado()
    {
        var op = await CreateOperatorAsync();
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        CreateCenterCommand AsProfile(SeededProfile p) => Create(op) with { AmbitoPerfilId = p.ProfileScopeId, CentroId = p.CenterId };

        var byAdmin = await BuildPlatform(admin.ExternalSubject).CreateCenterAsync(AsProfile(admin));
        var byNurse = await BuildPlatform(nurse.ExternalSubject).CreateCenterAsync(AsProfile(nurse));
        var listByAdmin = await BuildPlatform(admin.ExternalSubject).ListCentersAsync(new PlatformQuery(admin.ProfileScopeId, admin.CenterId));
        var adminWithOperatorScope = await BuildPlatform(admin.ExternalSubject).CreateCenterAsync(Create(op));
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @id", new { id = op.AccountId.Value });
        }

        var suspended = await BuildPlatform(op.ExternalSubject).CreateCenterAsync(Create(op));
        var suspendedList = await BuildPlatform(op.ExternalSubject).ListCentersAsync(new PlatformQuery(op.ProfileScopeId, op.CenterId));

        foreach (var result in new[] { byAdmin.Error, byNurse.Error, listByAdmin.Error, adminWithOperatorScope.Error, suspended.Error, suspendedList.Error })
        {
            Assert.Equal(ApplicationFailureCode.AccessDenied, result!.Code);
        }
    }

    [Fact]
    public async Task ElPerfilDePlataforma_NoEntraEnNingunOtroServicio()
    {
        var op = await CreateOperatorAsync();
        var scopes = new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory);
        var session = new FixedPlatformSessionIdentityProvider(op.ExternalSubject);

        var administracion = await Build(op.ExternalSubject).ListResidentsAsync(new AdministracionQuery(op.ProfileScopeId, op.CenterId));
        var estructura = await BuildEstructura(op.ExternalSubject).ListStructureUnitsAsync(new AdministracionQuery(op.ProfileScopeId, op.CenterId));
        var usuarios = await Build(op.ExternalSubject).ListAccountsAsync(new AdministracionQuery(op.ProfileScopeId, op.CenterId));
        var enfermeria = await EnfermeriaApplicationServiceTests.BuildService(op.ExternalSubject)
            .ListScopeResidentsAsync(new ListScopeResidentsCommand(op.ProfileScopeId, op.CenterId));
        var medicina = await MedicinaApplicationServiceTests.BuildMedicina(op.ExternalSubject)
            .ListScopeResidentsAsync(new ListScopeResidentsCommand(op.ProfileScopeId, op.CenterId, SystemProfile.Medicina));
        var direccion = await new DireccionApplicationService(
                scopes, new SqlSupervisionDirectory(TestDatabase.ConnectionFactory), session, new SqlEnfermeriaResidentDirectory(TestDatabase.ConnectionFactory),
                new SqlProcessDeadlineRepository(TestDatabase.ConnectionFactory))
            .ListPendingAsync(new ListSupervisionPendingQuery(op.ProfileScopeId, op.CenterId, DateOnly.FromDateTime(DateTime.Today)));

        foreach (var error in new[] { administracion.Error, estructura.Error, usuarios.Error, enfermeria.Error, medicina.Error, direccion.Error })
        {
            Assert.NotNull(error);
            Assert.Equal(ApplicationFailureCode.AccessDenied, error.Code);
        }
    }

    [Fact]
    public async Task BaseDeDatos_ElPerfilSoloValeEnElCentroReservado_YElCentroReservadoSoloAdmiteEsePerfil()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        const string insert = """
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @accountId, @centerId, @profile, 'ACTIVE', SYSUTCDATETIME(), @accountId)
            """;

        var platformElsewhere = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            insert, new { accountId = admin.AccountId.Value, centerId = admin.CenterId.Value, profile = "PLATAFORMA" }));
        var clinicalInPlatform = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            insert, new { accountId = admin.AccountId.Value, centerId = PlatformCenter.Id, profile = "ENFERMERIA" }));

        Assert.Contains("PLATFORM_PROFILE_ONLY_IN_PLATFORM_CENTER", platformElsewhere.Message);
        Assert.Contains("PLATFORM_CENTER_ONLY_PLATFORM_PROFILE", clinicalInPlatform.Message);
    }
}

file sealed class FixedPlatformSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
