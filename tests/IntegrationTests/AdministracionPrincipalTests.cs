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

namespace ResidApp.IntegrationTests;

/// <summary>Administración «principal» del centro (script 0047; CJ, 2026-10-07): solo ella ve todas las unidades y se añade las que no tiene;
/// marca a otras; el soporte de la plataforma puede hacer lo mismo sobre cualquier Administración. Cada prueba crea su propio centro.</summary>
public class AdministracionPrincipalTests
{
    private static AdministrationScopeApplicationService Build(string externalSubject)
    {
        var scope = new SqlAdministrationScope(TestDatabase.ConnectionFactory);
        return new AdministrationScopeApplicationService(
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedPrincipalSessionIdentityProvider(externalSubject)),
            scope, scope);
    }

    private static async Task MakePrincipalAsync(SeededProfile admin)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.administraciones_principales_cambios (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, @scope, 1, 1, @account, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { center = admin.CenterId.Value, scope = admin.ProfileScopeId, account = admin.AccountId.Value });
    }

    private static async Task<int> CountAsync(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    [Fact]
    public async Task SoloLaPrincipalVeLasUnidadesDelCentro_YSeAnadeLasQueNoTiene_ConAuditoria()
    {
        var principal = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, principal.CenterId, principal.UnitId);
        await MakePrincipalAsync(principal);
        var second = await AdministracionResidentesTests.AddUnitAsync(principal.CenterId);
        var foreignUnit = await AdministracionResidentesTests.AddUnitAsync((await SeedFixture.CreateProfileAsync(SystemProfile.Administracion)).CenterId);
        var service = Build(principal.ExternalSubject);

        var asOther = await Build(other.ExternalSubject).ReadAsync(other.ProfileScopeId, other.CenterId);
        var before = await service.ReadAsync(principal.ProfileScopeId, principal.CenterId);
        var denied = await Build(other.ExternalSubject).AddUnitAsync(new AddUnitToOwnScopeCommand(other.ProfileScopeId, other.CenterId, second));
        var added = await service.AddUnitAsync(new AddUnitToOwnScopeCommand(principal.ProfileScopeId, principal.CenterId, second));
        var again = await service.AddUnitAsync(new AddUnitToOwnScopeCommand(principal.ProfileScopeId, principal.CenterId, second));
        var foreign = await service.AddUnitAsync(new AddUnitToOwnScopeCommand(principal.ProfileScopeId, principal.CenterId, foreignUnit));
        var after = await service.ReadAsync(principal.ProfileScopeId, principal.CenterId);

        Assert.True(asOther.Ok);
        Assert.False(asOther.Value!.IsPrincipal);
        Assert.Empty(asOther.Value.Units);
        Assert.True(before.Value!.IsPrincipal);
        Assert.Equal(2, before.Value.Units.Count);
        Assert.Equal([true, false], before.Value.Units.OrderByDescending(u => u.InScope).Select(u => u.InScope));
        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error!.Code);
        Assert.True(added.Ok, added.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, foreign.Error!.Code);
        Assert.All(after.Value!.Units, u => Assert.True(u.InScope));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'ADMIN_SCOPE_UNIT_ADD' AND unidad_id = @u",
            new { a = principal.AccountId.Value, u = second.Value }));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.ambitos_perfil_unidad WHERE ambito_perfil_id = @s AND unidad_id = @u AND concedido_por_cuenta_id = @a AND revocado_en IS NULL",
            new { s = principal.ProfileScopeId, u = second.Value, a = principal.AccountId.Value }));
    }

    [Fact]
    public async Task LaPrincipalMarcaYDesmarcaAOtras_SinDejarElCentroSinNinguna_Y_SoloSiEsPrincipal()
    {
        var first = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var second = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, first.CenterId, first.UnitId);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, first.CenterId, first.UnitId);
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await MakePrincipalAsync(first);
        var service = Build(first.ExternalSubject);

        var byNonPrincipal = await Build(second.ExternalSubject).SetPrincipalAsync(new SetAdministrationPrincipalCommand(second.ProfileScopeId, second.CenterId, second.ProfileScopeId, true));
        var marked = await service.SetPrincipalAsync(new SetAdministrationPrincipalCommand(first.ProfileScopeId, first.CenterId, second.ProfileScopeId, true));
        var markedAgain = await service.SetPrincipalAsync(new SetAdministrationPrincipalCommand(first.ProfileScopeId, first.CenterId, second.ProfileScopeId, true));
        var notAdministration = await service.SetPrincipalAsync(new SetAdministrationPrincipalCommand(first.ProfileScopeId, first.CenterId, nurse.ProfileScopeId, true));
        var otherCenter = await service.SetPrincipalAsync(new SetAdministrationPrincipalCommand(first.ProfileScopeId, first.CenterId, outsider.ProfileScopeId, true));
        var firstStepsDown = await service.SetPrincipalAsync(new SetAdministrationPrincipalCommand(first.ProfileScopeId, first.CenterId, first.ProfileScopeId, false));
        var lastOne = await Build(second.ExternalSubject).SetPrincipalAsync(new SetAdministrationPrincipalCommand(second.ProfileScopeId, second.CenterId, second.ProfileScopeId, false));
        var firstIsNoLongerPrincipal = await service.AddUnitAsync(new AddUnitToOwnScopeCommand(first.ProfileScopeId, first.CenterId, await AdministracionResidentesTests.AddUnitAsync(first.CenterId)));

        Assert.Equal(ApplicationFailureCode.AccessDenied, byNonPrincipal.Error!.Code);
        Assert.True(marked.Ok, marked.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, markedAgain.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, notAdministration.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, otherCenter.Error!.Code);
        Assert.True(firstStepsDown.Ok, firstStepsDown.Error?.Message);
        Assert.Equal(ApplicationFailureCode.InvalidInput, lastOne.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, firstIsNoLongerPrincipal.Error!.Code);
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @c AND accion_codigo = 'ADMIN_PRINCIPAL_SET'", new { c = first.CenterId.Value }));
    }

    [Fact]
    public async Task ElHistorialDeMarcas_EsInmutableYSoloAdmiteAdministracionConNumeracionSinHuecos()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        await MakePrincipalAsync(admin);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Task Insert(Guid scope, int number, bool principal) => connection.ExecuteAsync("""
            INSERT INTO dbo.administraciones_principales_cambios (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, @scope, @number, @principal, @account, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { center = admin.CenterId.Value, scope, number, principal, account = admin.AccountId.Value });

        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.administraciones_principales_cambios SET principal = 0 WHERE ambito_perfil_id = @s", new { s = admin.ProfileScopeId }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.administraciones_principales_cambios WHERE ambito_perfil_id = @s", new { s = admin.ProfileScopeId }));
        var wrongProfile = await Assert.ThrowsAsync<SqlException>(() => Insert(nurse.ProfileScopeId, 1, true));
        var gap = await Assert.ThrowsAsync<SqlException>(() => Insert(admin.ProfileScopeId, 3, false));
        var sameState = await Assert.ThrowsAsync<SqlException>(() => Insert(admin.ProfileScopeId, 2, true));
        var startsUnmarked = await Assert.ThrowsAsync<SqlException>(() => Insert(
            SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, admin.CenterId, admin.UnitId).Result.ProfileScopeId, 1, false));

        Assert.Contains("ADMIN_PRINCIPAL_CHANGE_IMMUTABLE", update.Message);
        Assert.Contains("ADMIN_PRINCIPAL_CHANGE_IMMUTABLE", delete.Message);
        Assert.Contains("ADMIN_PRINCIPAL_PROFILE_INVALID", wrongProfile.Message);
        Assert.Contains("ADMIN_PRINCIPAL_SEQUENCE_INVALID", gap.Message);
        Assert.Contains("ADMIN_PRINCIPAL_SEQUENCE_INVALID", sameState.Message);
        Assert.Contains("ADMIN_PRINCIPAL_SEQUENCE_INVALID", startsUnmarked.Message);
    }

    // ---- Soporte de la plataforma ----

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
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedPrincipalSessionIdentityProvider(subject),
        new SqlPlatformCenterDirectory(TestDatabase.ConnectionFactory), new SqlPlatformCenterRepository(TestDatabase.ConnectionFactory));

    [Fact]
    public async Task ElSoporte_CreaElCentroConSuPrimeraAdministracionPrincipal_LaMarcaYAnadeUnidades()
    {
        var op = await CreateOperatorAsync();
        var platform = BuildPlatform(op.ExternalSubject);
        var centerId = Guid.NewGuid();
        var adminSubject = $"test-adm-{Guid.NewGuid():N}"[..28];
        var created = await platform.CreateCenterAsync(new CreateCenterCommand(
            op.ProfileScopeId, op.CenterId, centerId, $"cen-{Guid.NewGuid():N}"[..20], "Residencia Principal (ficticia)", $"u-{Guid.NewGuid():N}"[..14],
            "Primera unidad", adminSubject, "Admin Principal"));
        Assert.True(created.Ok, created.Error?.Message);
        var extraUnit = await AdministracionResidentesTests.AddUnitAsync(CenterId.From(centerId));
        var query = new PlatformCenterQuery(op.ProfileScopeId, op.CenterId, CenterId.From(centerId));

        var detail = (await platform.FindCenterAsync(query)).Value!;
        var adminScope = Assert.Single(detail.Administrations);
        Assert.True(adminScope.IsPrincipal);
        Assert.Equal([true, false], adminScope.Units.OrderByDescending(u => u.InScope).Select(u => u.InScope));

        var added = await platform.AddUnitToAdministrationAsync(new AddUnitToAdministrationCommand(
            op.ProfileScopeId, op.CenterId, CenterId.From(centerId), adminScope.ProfileScopeId, extraUnit));
        var addedAgain = await platform.AddUnitToAdministrationAsync(new AddUnitToAdministrationCommand(
            op.ProfileScopeId, op.CenterId, CenterId.From(centerId), adminScope.ProfileScopeId, extraUnit));
        var unmarked = await platform.SetAdministrationPrincipalAsync(new SetPlatformAdministrationPrincipalCommand(
            op.ProfileScopeId, op.CenterId, CenterId.From(centerId), adminScope.ProfileScopeId, false));
        var unmarkedAgain = await platform.SetAdministrationPrincipalAsync(new SetPlatformAdministrationPrincipalCommand(
            op.ProfileScopeId, op.CenterId, CenterId.From(centerId), adminScope.ProfileScopeId, false));
        var marked = await platform.SetAdministrationPrincipalAsync(new SetPlatformAdministrationPrincipalCommand(
            op.ProfileScopeId, op.CenterId, CenterId.From(centerId), adminScope.ProfileScopeId, true));
        var otherCenterScope = (await SeedFixture.CreateProfileAsync(SystemProfile.Administracion)).ProfileScopeId;
        var foreign = await platform.SetAdministrationPrincipalAsync(new SetPlatformAdministrationPrincipalCommand(
            op.ProfileScopeId, op.CenterId, CenterId.From(centerId), otherCenterScope, true));
        var unknownCenter = await platform.FindCenterAsync(query with { CentroDestinoId = CenterId.From(Guid.NewGuid()) });

        Assert.True(added.Ok, added.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, addedAgain.Error!.Code);
        Assert.True(unmarked.Ok, unmarked.Error?.Message);
        Assert.Equal(ApplicationFailureCode.Conflict, unmarkedAgain.Error!.Code);
        Assert.True(marked.Ok, marked.Error?.Message);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, unknownCenter.Error!.Code);
        Assert.Equal(3, await CountAsync(
            "SELECT COUNT(*) FROM dbo.administraciones_principales_cambios WHERE centro_id = @c AND cambiado_por_perfil = 'PLATAFORMA'", new { c = centerId }));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @c AND perfil_activo = 'PLATAFORMA' AND accion_codigo = 'ADMIN_SCOPE_UNIT_ADD'", new { c = centerId }));
    }

    [Fact]
    public async Task ElSoporteEsElUnicoPerfilQueOperaLaPlataforma()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await MakePrincipalAsync(admin);
        var denied = await BuildPlatform(admin.ExternalSubject).FindCenterAsync(new PlatformCenterQuery(admin.ProfileScopeId, admin.CenterId, admin.CenterId));
        var deniedMark = await BuildPlatform(admin.ExternalSubject).SetAdministrationPrincipalAsync(new SetPlatformAdministrationPrincipalCommand(
            admin.ProfileScopeId, admin.CenterId, admin.CenterId, admin.ProfileScopeId, false));

        Assert.Equal(ApplicationFailureCode.AccessDenied, denied.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, deniedMark.Error!.Code);
        Assert.True((await Build(admin.ExternalSubject).ReadAsync(admin.ProfileScopeId, admin.CenterId)).Value!.IsPrincipal);
    }
}

file sealed class FixedPrincipalSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
