using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Audit;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, auditoría administrativa (historia 9, ADM-28 / AUD-01 a AUD-03): solo las acciones administrativas del
/// centro del ámbito, con los nombres resueltos y sin ranking. Cada prueba crea su propio centro.</summary>
public class AdministracionAuditoriaTests
{
    private static ListAdministrativeAuditQuery Query(
        SeededProfile admin, DateOnly? from = null, DateOnly? to = null, string? action = null, AccountId? account = null) =>
        new(admin.ProfileScopeId, admin.CenterId, from ?? DateOnly.FromDateTime(DateTime.Today).AddDays(-1),
            to ?? DateOnly.FromDateTime(DateTime.Today).AddDays(1), action, account);

    private static async Task<AuditPage> ListAsync(SeededProfile admin, ListAdministrativeAuditQuery? query = null) =>
        (await BuildEstructura(admin.ExternalSubject).ListAuditAsync(query ?? Query(admin))).Value!;

    private static async Task InsertAuditAsync(
        SeededProfile actor, string profile, string action, string resourceType, Guid resourceId, Guid? unitId = null, Guid? residentId = null,
        string? purpose = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.eventos_auditoria
                (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, proposito_codigo, ocurrido_en)
            VALUES (NEWID(), @actor, @profile, @center, @unitId, @residentId, @resourceType, @resourceId, @action, @purpose, SYSUTCDATETIME())
            """, new
        {
            actor = actor.AccountId.Value, profile, center = actor.CenterId.Value, unitId, residentId, resourceType, resourceId, action, purpose,
        });
    }

    private static async Task<AccountId> CreateAccountAsync(SeededProfile admin, string displayName)
    {
        var result = await Build(admin.ExternalSubject).CreateAccountAsync(new CreateProfessionalAccountCommand(
            admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), $"test-aud-{Guid.NewGuid():N}"[..28], displayName,
            SystemProfile.Enfermeria, [admin.UnitId.Value]));
        Assert.True(result.Ok, result.Error?.Message);
        return result.Value;
    }

    [Fact]
    public async Task VeLosEventosAdministrativos_ConLosNombresResueltos_YNingunoClinico()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var resident = await CreateResidentAsync(admin, "Residente Auditoría");
        var created = await CreateAccountAsync(admin, "Ana Ruiz");
        var unit = await BuildEstructura(admin.ExternalSubject).CreateUnitAsync(new CreateUnitCommand(
            admin.ProfileScopeId, admin.CenterId, Guid.NewGuid(), $"aud-{Guid.NewGuid():N}"[..16], "Unidad de auditoría"));
        Assert.True(unit.Ok, unit.Error?.Message);
        await InsertAuditAsync(admin, "ENFERMERIA", "CLINICAL_EVENT_REGISTER", "CLINICAL_EVENT", Guid.NewGuid(), admin.UnitId.Value, resident.Value);
        await InsertAuditAsync(admin, "MEDICINA", "REFERENCE_RANGES_UPDATE", "REFERENCE_RANGES", admin.CenterId.Value);
        await InsertAuditAsync(admin, "DIRECCION_CLINICA", "CLINICAL_DETAIL_READ", "BASELINE", Guid.NewGuid(), admin.UnitId.Value, resident.Value,
            "SUPERVISION_CLINICA");

        var page = await ListAsync(admin);

        Assert.All(page.Entries, entry => Assert.True(AdministrativeAudit.IsAdministrative(entry.Action), entry.Action));
        Assert.DoesNotContain(page.Entries, e => e.Action is "CLINICAL_EVENT_REGISTER" or "REFERENCE_RANGES_UPDATE" or "CLINICAL_DETAIL_READ");
        var accountCreate = Assert.Single(page.Entries, e => e.Action == "ACCOUNT_CREATE");
        Assert.Equal(admin.ExternalSubject, accountCreate.ActorName);
        Assert.Equal(SystemProfile.Administracion, accountCreate.ActorProfile);
        Assert.Equal("Ana Ruiz", accountCreate.AffectedName);
        Assert.Contains(page.Entries, e => e.Action == "PROFILE_SCOPE_GRANT" && e.AffectedName == "Ana Ruiz");
        Assert.Contains(page.Entries, e => e.Action == "PROFILE_UNIT_GRANT" && e.AffectedName == "Ana Ruiz" && e.UnitName is not null);
        Assert.Contains(page.Entries, e => e.Action == "UNIT_CREATE" && e.UnitName == "Unidad de auditoría");
        Assert.Contains(page.Entries, e => e.Action == "RESIDENT_CREATE" && e.ResidentName == "Residente Auditoría");
        Assert.Equal(page.Entries.OrderByDescending(e => e.OccurredAt), page.Entries);
        Assert.False(page.Truncated);
        Assert.NotEqual(Guid.Empty, created.Value);
    }

    [Fact]
    public async Task NoVeLosEventosDeOtroCentro_NiLosDeUnaUnidadFueraDeSuAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await CreateAccountAsync(other, "Cuenta de otro centro");
        var ungranted = await AddUnitAsync(admin.CenterId);
        await InsertAuditAsync(admin, "ADMINISTRACION", "UNIT_RENAME", "UNIT", ungranted.Value, ungranted.Value);
        await InsertAuditAsync(admin, "ADMINISTRACION", "UNIT_RENAME", "UNIT", admin.UnitId.Value, admin.UnitId.Value);

        var mine = await ListAsync(admin);
        var theirs = await ListAsync(other);

        Assert.DoesNotContain(mine.Entries, e => e.AffectedName == "Cuenta de otro centro");
        Assert.Contains(theirs.Entries, e => e.AffectedName == "Cuenta de otro centro");
        var renames = mine.Entries.Where(e => e.Action == "UNIT_RENAME").ToList();
        Assert.Single(renames);
        Assert.Equal(await UnitNameAsync(admin.UnitId), renames[0].UnitName);
    }

    private static async Task<string> UnitNameAsync(UnitId unitId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT nombre_visible FROM dbo.unidades WHERE id = @id", new { id = unitId.Value });
    }

    [Fact]
    public async Task Filtros_PorAccion_PorCuentaAfectada_YPorPeriodo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var ana = await CreateAccountAsync(admin, "Ana Ruiz");
        var luis = await CreateAccountAsync(admin, "Luis Pérez");

        var byAction = await ListAsync(admin, Query(admin, action: "ACCOUNT_CREATE"));
        var byAccount = await ListAsync(admin, Query(admin, account: ana));
        var longAgo = DateOnly.FromDateTime(DateTime.Today).AddDays(-60);
        var oldPeriod = await ListAsync(admin, Query(admin, longAgo, longAgo.AddDays(5)));

        Assert.Equal(2, byAction.Entries.Count);
        Assert.All(byAction.Entries, e => Assert.Equal("ACCOUNT_CREATE", e.Action));
        Assert.NotEmpty(byAccount.Entries);
        Assert.All(byAccount.Entries, e => Assert.Equal("Ana Ruiz", e.AffectedName));
        Assert.DoesNotContain(byAccount.Entries, e => e.AffectedName == "Luis Pérez");
        Assert.Empty(oldPeriod.Entries);
        Assert.NotEqual(ana, luis);
    }

    [Fact]
    public async Task PeriodoImposible_OAccionNoAdministrativa_SonEntradaInvalida()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = BuildEstructura(admin.ExternalSubject);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var reversed = await service.ListAuditAsync(Query(admin, today, today.AddDays(-1)));
        var tooLong = await service.ListAuditAsync(Query(admin, today.AddDays(-400), today));
        var clinical = await service.ListAuditAsync(Query(admin, action: "CLINICAL_DETAIL_READ"));
        var unknown = await service.ListAuditAsync(Query(admin, action: "no-existe"));

        foreach (var result in new[] { reversed, tooLong, clinical, unknown })
        {
            Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
        }
    }

    [Fact]
    public async Task ConMasDeQuinientosEventos_TruncaAlosMasRecientes_YLoAvisa()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.eventos_auditoria
                    (id, cuenta_id, perfil_activo, centro_id, unidad_id, residente_id, tipo_recurso, recurso_id, accion_codigo, ocurrido_en)
                SELECT TOP (501) NEWID(), @actor, 'ADMINISTRACION', @center, NULL, NULL, 'ACCOUNT', @actor, 'ACCOUNT_RENAME', SYSUTCDATETIME()
                  FROM sys.all_columns
                """, new { actor = admin.AccountId.Value, center = admin.CenterId.Value });
        }

        var page = await ListAsync(admin);

        Assert.Equal(AuditPage.MaxEntries, page.Entries.Count);
        Assert.True(page.Truncated);
    }

    [Fact]
    public async Task OtrosPerfiles_DanAccesoDenegado()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);

        var result = await BuildEstructura(nurse.ExternalSubject).ListAuditAsync(Query(nurse));
        var foreign = await BuildEstructura(nurse.ExternalSubject).ListAuditAsync(Query(admin));

        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
    }

    [Fact]
    public void Aud02_LosTiposDeLaLectura_NoTienenTextoLibreNiContadoresNiFiltroPorActor()
    {
        static string[] Names<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order().ToArray();

        Assert.Equal(["Action", "ActorName", "ActorProfile", "AffectedName", "OccurredAt", "ResidentName", "UnitName"], Names<AuditEntry>());
        Assert.Equal(["Entries", "Truncated"], Names<AuditPage>());
        Assert.Equal(["Action", "AffectedAccountId", "FromUtc", "ToExclusiveUtc"], Names<AdministrativeAuditQuery>());
    }

    [Fact]
    public async Task LaTablaDeAuditoria_SigueSiendoDeSoloInsercion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await CreateAccountAsync(admin, "Ana Ruiz");
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_auditoria SET accion_codigo = 'ACCOUNT_RENAME' WHERE centro_id = @center", new { center = admin.CenterId.Value }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.eventos_auditoria WHERE centro_id = @center", new { center = admin.CenterId.Value }));

        Assert.Contains("AUDIT_EVENT_IMMUTABLE", update.Message);
        Assert.Contains("AUDIT_EVENT_IMMUTABLE", delete.Message);
    }
}
