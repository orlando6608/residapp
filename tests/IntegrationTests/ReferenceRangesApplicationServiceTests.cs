using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Pantalla de rangos de referencia de constantes: solo Medicina o Dirección Clínica con el permiso
/// REFERENCE_RANGES_MANAGE, historial inmutable y concurrencia optimista por versión.</summary>
public class ReferenceRangesApplicationServiceTests
{
    private const string Permission = "REFERENCE_RANGES_MANAGE";

    private static ReferenceRangesApplicationService BuildService(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory),
        new FixedSessionIdentityProvider(externalSubject),
        new SqlReferenceRangeRepository(TestDatabase.ConnectionFactory));

    private static SaveReferenceRangesCommand Save(SeededProfile seed, int version, params ReferenceRangeCommandItem[] items) =>
        new(seed.ProfileScopeId, seed.CenterId, version, items);

    [Fact]
    public async Task GuardarYLeer_AplicaLosCambiosYRegistraElHistorial()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, [Permission]);
        var service = BuildService(direccion.ExternalSubject);

        var empty = (await service.ReadAsync(new ReadReferenceRangesCommand(direccion.ProfileScopeId, direccion.CenterId))).Value!;
        Assert.Empty(empty.Ranges);
        Assert.Equal(0, empty.Version);

        var first = await service.SaveAsync(Save(direccion, 0,
            new ReferenceRangeCommandItem(VitalSignCode.Temperatura, 35m, 38m), new ReferenceRangeCommandItem(VitalSignCode.SaturacionO2, 92m, null), new ReferenceRangeCommandItem(VitalSignCode.Glucemia, null, null)));
        Assert.Equal(2, first.Value);

        // Se ajusta la temperatura, se quita la saturación y la glucemia sigue sin rango.
        var second = await service.SaveAsync(Save(direccion, 2,
            new ReferenceRangeCommandItem(VitalSignCode.Temperatura, 35.5m, 37.8m), new ReferenceRangeCommandItem(VitalSignCode.SaturacionO2, null, null)));
        Assert.Equal(4, second.Value);

        var view = (await service.ReadAsync(new ReadReferenceRangesCommand(direccion.ProfileScopeId, direccion.CenterId))).Value!;
        Assert.Equal([new VitalSignRange(VitalSignCode.Temperatura, 35.5m, 37.8m)], view.Ranges);
        Assert.Equal(4, view.Version);
        Assert.All(view.History, h => Assert.True(h.ChangedByCurrentAccount));
        Assert.All(view.History, h => Assert.Equal(SystemProfile.DireccionClinica, h.ChangedByProfile));
        var removed = view.History.Single(h => h.Code == VitalSignCode.SaturacionO2 && h.NewMin is null && h.NewMax is null);
        Assert.Equal(92m, removed.PreviousMin);
        var adjusted = view.History.Single(h => h.Code == VitalSignCode.Temperatura && h.PreviousMin == 35m);
        Assert.Equal(38m, adjusted.PreviousMax);
        Assert.Equal(35.5m, adjusted.NewMin);
        Assert.Equal(37.8m, adjusted.NewMax);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var audits = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE centro_id = @CenterId AND accion_codigo = 'REFERENCE_RANGES_UPDATE' AND perfil_activo = 'DIRECCION_CLINICA'",
            new { CenterId = direccion.CenterId.Value });
        Assert.Equal(2, audits);
    }

    [Fact]
    public async Task Medicina_ConPermiso_PuedeFijarRangos()
    {
        var medicina = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina, [Permission]);

        var result = await BuildService(medicina.ExternalSubject).SaveAsync(Save(medicina, 0, new ReferenceRangeCommandItem(VitalSignCode.FrecuenciaCardiaca, 50m, 100m)));

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task VersionAntigua_EsConflicto_YNoEscribeNada()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, [Permission]);
        var service = BuildService(direccion.ExternalSubject);
        Assert.True((await service.SaveAsync(Save(direccion, 0, new ReferenceRangeCommandItem(VitalSignCode.Temperatura, 35m, 38m)))).Ok);

        var stale = await service.SaveAsync(Save(direccion, 0, new ReferenceRangeCommandItem(VitalSignCode.Temperatura, 36m, 37m)));

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        var view = (await service.ReadAsync(new ReadReferenceRangesCommand(direccion.ProfileScopeId, direccion.CenterId))).Value!;
        Assert.Equal([new VitalSignRange(VitalSignCode.Temperatura, 35m, 38m)], view.Ranges);
        Assert.Equal(1, view.Version);
    }

    [Fact]
    public async Task SinPermisoOPerfilNoClinico_AccesoDenegado()
    {
        var direccionSinPermiso = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica);
        var enfermeria = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var administracion = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        foreach (var seed in new[] { direccionSinPermiso, enfermeria, administracion })
        {
            var service = BuildService(seed.ExternalSubject);
            var read = await service.ReadAsync(new ReadReferenceRangesCommand(seed.ProfileScopeId, seed.CenterId));
            var save = await service.SaveAsync(Save(seed, 0, new ReferenceRangeCommandItem(VitalSignCode.Temperatura, 35m, 38m)));
            Assert.Equal(ApplicationFailureCode.AccessDenied, read.Error!.Code);
            Assert.Equal(ApplicationFailureCode.AccessDenied, save.Error!.Code);
        }
    }

    [Theory]
    [InlineData(38, 35)]
    [InlineData(35, 35)]
    [InlineData(0, 38)]
    public async Task RangoIncoherente_EsEntradaInvalida(int min, int max)
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, [Permission]);

        var result = await BuildService(direccion.ExternalSubject).SaveAsync(Save(direccion, 0, new ReferenceRangeCommandItem(VitalSignCode.Temperatura, min, max)));

        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
    }

    [Theory]
    [InlineData(SystemProfile.Enfermeria)]
    [InlineData(SystemProfile.Administracion)]
    [InlineData(SystemProfile.Auxiliar)]
    public async Task LaBaseDeDatos_SoloConcedeElPermisoAPerfilesClinicos(SystemProfile profile)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => SeedFixture.CreateProfileAsync(profile, [Permission]));

        Assert.Contains("REFERENCE_RANGES_PERMISSION_PROFILE_INVALID", ex.Message);
    }

    [Fact]
    public async Task ElHistorial_NoAdmiteModificaciones()
    {
        var direccion = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, [Permission]);
        await BuildService(direccion.ExternalSubject).SaveAsync(Save(direccion, 0, new ReferenceRangeCommandItem(VitalSignCode.Glucemia, 70m, 180m)));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        foreach (var sql in new[]
        {
            "UPDATE dbo.rangos_referencia_constantes_historial SET minimo_nuevo = 1 WHERE centro_id = @CenterId",
            "DELETE FROM dbo.rangos_referencia_constantes_historial WHERE centro_id = @CenterId",
        })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(sql, new { CenterId = direccion.CenterId.Value }));
            Assert.Contains("REFERENCE_RANGE_HISTORY_IMMUTABLE", ex.Message);
        }
    }
}

file sealed class FixedSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
