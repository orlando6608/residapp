using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Administración, bloque 1 (historia 1, ADM-02/ADM-03, script 0021): lista de residentes, ficha administrativa
/// con historial de ubicación y corrección de identidad. Cada prueba crea su propio centro.</summary>
public class AdministracionResidentesTests
{
    internal static AdministracionApplicationService Build(string externalSubject)
    {
        var session = new FixedAdministracionSessionIdentityProvider(externalSubject);
        return new AdministracionApplicationService(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory),
            new SqlAdministracionResidentDirectory(TestDatabase.ConnectionFactory), session,
            new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory),
            new SqlResidentIdentityRepository(TestDatabase.ConnectionFactory),
            new SqlResidentFamilyRepository(TestDatabase.ConnectionFactory),
            new SqlProfessionalAccountDirectory(TestDatabase.ConnectionFactory),
            new SqlProfessionalAccountRepository(TestDatabase.ConnectionFactory));
    }

    internal static async Task<ResidentId> CreateResidentAsync(SeededProfile admin, string name, UnitId? unitId = null)
    {
        var created = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, unitId ?? admin.UnitId, name, new DateOnly(1940, 5, 20),
            DocumentedSexCode.NoConsta, null, null, null, null, null, Guid.NewGuid()));
        return created.ResidentId;
    }

    internal static async Task<UnitId> AddUnitAsync(CenterId centerId)
    {
        var unitId = Guid.NewGuid();
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @code, @code, 'ACTIVE', SYSUTCDATETIME())",
            new { unitId, centerId = centerId.Value, code = $"TEST-UNIT-{unitId:N}"[..30] });
        return UnitId.From(unitId);
    }

    private static CorrectResidentIdentityCommand Correct(
        SeededProfile seed, ResidentId residentId, int expected, string? name = "María García López", string? reason = "Error al transcribir el DNI.",
        DocumentedSexCode sex = DocumentedSexCode.Mujer) =>
        new(seed.ProfileScopeId, seed.CenterId, residentId, name, new DateOnly(1940, 5, 21), sex, reason, expected);

    [Fact]
    public async Task Lista_SoloDaLosResidentesDelAmbito_YLaFichaIncluyeSuUbicacion()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var mine = await CreateResidentAsync(admin, "Residente Administración Uno");
        var otherUnit = await AddUnitAsync(admin.CenterId);
        var outsider = await CreateResidentAsync(admin, "Residente Otra Unidad", otherUnit);
        var service = Build(admin.ExternalSubject);
        var query = new AdministracionQuery(admin.ProfileScopeId, admin.CenterId);

        var list = await service.ListResidentsAsync(query);
        var detail = await service.FindResidentAsync(new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, mine));
        var foreign = await service.FindResidentAsync(new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, outsider));

        var only = Assert.Single(list.Value!);
        Assert.Equal(mine, only.ResidentId);
        Assert.Equal(DocumentedSexCode.NoConsta, only.DocumentedSex);
        var location = Assert.Single(detail.Value!.Locations);
        Assert.Null(location.Until);
        Assert.Equal(SystemProfile.Administracion, location.RecordedBy);
        Assert.Empty(detail.Value.Corrections);
        Assert.Equal(ApplicationFailureCode.AccessDenied, foreign.Error!.Code);
    }

    [Fact]
    public async Task Correccion_GuardaLaVersionAnterior_LaAuditoria_YRechazaElDobleEnvio()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "maria garcia");
        var service = Build(admin.ExternalSubject);

        var first = await service.CorrectIdentityAsync(Correct(admin, residentId, expected: 0));
        var again = await service.CorrectIdentityAsync(Correct(admin, residentId, expected: 0));
        var unchanged = await service.CorrectIdentityAsync(Correct(admin, residentId, expected: 1));
        var withoutReason = await service.CorrectIdentityAsync(Correct(admin, residentId, expected: 1, name: "María García", reason: " "));
        var detail = (await service.FindResidentAsync(new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, residentId))).Value!;

        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(1, first.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unchanged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error!.Code);
        Assert.Equal(("María García López", new DateOnly(1940, 5, 21), DocumentedSexCode.Mujer),
            (detail.Resident.DisplayName, detail.Resident.BirthDate, detail.Resident.DocumentedSex));
        var correction = Assert.Single(detail.Corrections);
        Assert.Equal(new ResidentIdentity("maria garcia", new DateOnly(1940, 5, 20), DocumentedSexCode.NoConsta), correction.Before);
        Assert.Equal("Error al transcribir el DNI.", correction.Reason);
        Assert.Equal(1, await CountAuditAsync(residentId));
    }

    [Fact]
    public async Task Correccion_SoloLaHaceAdministracion_YNoFueraDeSuAmbito()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Ámbito");
        var otherAdmin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var results = new List<ApplicationResult<int>>();
        foreach (var profile in new[] { SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Auxiliar, SystemProfile.DireccionClinica })
        {
            var other = await SeedFixture.AddProfileToCenterAsync(profile, admin.CenterId, admin.UnitId);
            results.Add(await Build(other.ExternalSubject).CorrectIdentityAsync(Correct(other, residentId, expected: 0)));
        }
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);

        results.Add(await Build(otherAdmin.ExternalSubject).CorrectIdentityAsync(Correct(otherAdmin, residentId, expected: 0)));
        var nurseList = await Build(nurse.ExternalSubject).ListResidentsAsync(new AdministracionQuery(nurse.ProfileScopeId, nurse.CenterId));

        Assert.All(results, r => Assert.Equal(ApplicationFailureCode.AccessDenied, r.Error!.Code));
        Assert.Equal(ApplicationFailureCode.AccessDenied, nurseList.Error!.Code);
        Assert.Equal(0, await CountAuditAsync(residentId));
    }

    [Fact]
    public async Task BaseDeDatos_NoDejaCambiarLaIdentidadSinCorreccion_NiTocarLasCorrecciones()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Protegido");
        Assert.True((await Build(admin.ExternalSubject).CorrectIdentityAsync(Correct(admin, residentId, expected: 0))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var direct = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.residentes SET nombre_visible = N'Otro nombre' WHERE id = @Id", new { Id = residentId.Value }));
        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.residentes_identidad_correcciones SET motivo = N'x' WHERE residente_id = @Id", new { Id = residentId.Value }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.residentes_identidad_correcciones WHERE residente_id = @Id", new { Id = residentId.Value }));

        Assert.Contains("RESIDENT_IDENTITY_CHANGE_REQUIRES_CORRECTION", direct.Message);
        Assert.Contains("RESIDENT_IDENTITY_CORRECTION_IMMUTABLE", update.Message);
        Assert.Contains("RESIDENT_IDENTITY_CORRECTION_IMMUTABLE", delete.Message);
    }

    [Fact]
    public void TiposDeAdministracion_NoTienenNingunDatoClinico()
    {
        var properties = new[]
        {
            typeof(AdministrativeResidentSummary), typeof(ResidentLocationInterval), typeof(ResidentIdentityCorrectionEntry),
            typeof(AdministrativeResidentDetail),
        }.SelectMany(t => t.GetProperties()).Select(p => p.Name + ":" + p.PropertyType.Name).ToList();

        Assert.DoesNotContain(properties, p => p.Contains("Baseline", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Basal", StringComparison.OrdinalIgnoreCase) || p.Contains("Barthel", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Event", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> CountAuditAsync(ResidentId residentId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE recurso_id = @Id AND accion_codigo = 'RESIDENT_IDENTITY_CORRECT'",
            new { Id = residentId.Value });
    }
}

file sealed class FixedAdministracionSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
