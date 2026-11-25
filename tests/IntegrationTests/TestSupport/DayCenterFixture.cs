using Dapper;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Platform;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>
/// Un centro creado por el camino real (el alta de Plataforma) del tipo que se pida, con su unidad, su Administración y un residente activo sin habitación. Lo usan las pruebas de la asistencia del
/// Centro de Día. La conexión lleva el ámbito de Plataforma en la sesión de SQL, como en producción (el alta fija el horario inicial con un procedimiento que lo comprueba, script 0114).
/// </summary>
internal static class DayCenterFixture
{
    internal sealed record Day(Guid CenterId, Guid AccountId, Guid ResidentId, Guid UnitId, string AdminSubject);

    private sealed class FixedTenant(TenantScope? scope) : ITenantContext
    {
        public Task<TenantScope?> GetAsync(CancellationToken ct = default) => Task.FromResult(scope);
    }

    private sealed class FixedIdentity(string subject) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) => Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(subject));
    }

    public static async Task<Day> CreateAsync(CenterServiceType type)
    {
        var accountId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();
        var subject = $"test-asist-{Guid.NewGuid():N}"[..28];
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, nombre_visible, estado, creado_en) VALUES (@accountId, @subject, N'Operador de asistencia', 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@scopeId, @accountId, @centerId, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @accountId)
                """, new { accountId, subject, scopeId, centerId = PlatformCenter.Id });
        }

        var factory = new SqlConnectionFactory(TestDatabase.ConnectionString, new FixedTenant(new TenantScope(subject, scopeId)));
        var service = new PlatformApplicationService(
            new SqlProfileScopeDirectoryProvider(factory), new FixedIdentity(subject), new SqlPlatformCenterDirectory(factory), new SqlPlatformCenterRepository(factory),
            new SqlAccessInvitationRepository(factory), new SqlCenterModuleRepository(factory), TimeProvider.System, new SqlCenterTimeZones(factory), new SqlCenterTimeZones(factory),
            new SqlCenterServiceTypes(factory));
        var centerId = Guid.NewGuid();
        var adminSubject = $"test-adm-{Guid.NewGuid():N}"[..28];
        string Code() => $"asi-{Guid.NewGuid():N}"[..20];
        var created = await service.CreateCenterAsync(new CreateCenterCommand(
            scopeId, CenterId.From(PlatformCenter.Id), centerId, Code(), "Centro de asistencia (ficticio)", Code(), "Sala 1", adminSubject, "Admin Asistencia", TipoDeServicio: type));
        Assert.True(created.Ok, created.Error?.Message);

        using var db = await TestDatabase.ConnectionFactory.OpenAsync();
        var admin = await db.ExecuteScalarAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @adminSubject", new { adminSubject });
        var unit = await db.ExecuteScalarAsync<Guid>("SELECT TOP (1) id FROM dbo.unidades WHERE centro_id = @centerId", new { centerId });
        var resident = await AddResidentAsync(admin, centerId, unit, "Persona de asistencia (ficticia)");
        return new Day(centerId, admin, resident, unit, adminSubject);
    }

    /// <summary>Otro residente activo, sin habitación, en la unidad indicada.</summary>
    public static async Task<Guid> AddResidentAsync(Guid adminAccountId, Guid centerId, Guid unitId, string name) =>
        (await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            AccountId.From(adminAccountId), SystemProfile.Administracion, CenterId.From(centerId), UnitId.From(unitId), name,
            new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid(), LocationPending: true))).ResidentId.Value;
}
