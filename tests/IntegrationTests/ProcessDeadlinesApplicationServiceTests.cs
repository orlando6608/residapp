using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Supervision;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Plazos de los hitos del proceso por centro (script 0046): solo Dirección Clínica con el permiso PROCESS_DEADLINES_MANAGE,
/// valores de CJ por defecto, historial inmutable y concurrencia optimista por versión.</summary>
public class ProcessDeadlinesApplicationServiceTests
{
    private static ProcessDeadlinesApplicationService BuildService(string externalSubject) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory),
        new FixedDeadlinesSessionIdentityProvider(externalSubject),
        new SqlProcessDeadlineRepository(TestDatabase.ConnectionFactory));

    private static SaveProcessDeadlinesCommand Save(SeededProfile seed, int version, params ProcessDeadlineCommandItem[] items) =>
        new(seed.ProfileScopeId, seed.CenterId, version, items);

    private static Task<SeededProfile> DireccionAsync() =>
        SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica, [ProfilePermissions.ProcessDeadlinesManage]);

    [Fact]
    public async Task SinCambios_ValenLosPlazosDeCJ_YGuardarYLeerAplicaLosCambiosYRegistraElHistorial()
    {
        var direccion = await DireccionAsync();
        var service = BuildService(direccion.ExternalSubject);
        var repository = new SqlProcessDeadlineRepository(TestDatabase.ConnectionFactory);

        var initial = (await service.ReadAsync(new ReadProcessDeadlinesCommand(direccion.ProfileScopeId, direccion.CenterId))).Value!;
        Assert.Equal(ProcessMilestoneRules.Defaults, initial.Deadlines);
        Assert.Equal(0, initial.Version);

        // La valoración médica pasa a 12 h (720 min) y el informe de derivación deja de medirse si el evento es prioritario.
        var first = await service.SaveAsync(Save(direccion, 0,
            new ProcessDeadlineCommandItem(ProcessMilestone.ValoracionMedica, 720, 30),
            new ProcessDeadlineCommandItem(ProcessMilestone.InformeDerivacion, 30, null),
            new ProcessDeadlineCommandItem(ProcessMilestone.LlamadaFamilia, 60, 30)));   // sin cambios: no cuenta
        Assert.True(first.Ok, first.Error?.Message);
        Assert.Equal(2, first.Value);

        var effective = await repository.GetEffectiveAsync(direccion.CenterId);
        Assert.Equal(new MilestoneDeadline(TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)), effective[ProcessMilestone.ValoracionMedica]);
        Assert.Equal(new MilestoneDeadline(TimeSpan.FromMinutes(30), null), effective[ProcessMilestone.InformeDerivacion]);
        Assert.Equal(ProcessMilestoneRules.Defaults[ProcessMilestone.LlamadaFamilia], effective[ProcessMilestone.LlamadaFamilia]);

        // Volver a los plazos de CJ borra el cambio del centro y deja su rastro en el historial.
        var second = await service.SaveAsync(Save(direccion, 2,
            new ProcessDeadlineCommandItem(ProcessMilestone.ValoracionMedica, 1440, 30)));
        Assert.Equal(3, second.Value);
        var view = (await service.ReadAsync(new ReadProcessDeadlinesCommand(direccion.ProfileScopeId, direccion.CenterId))).Value!;
        Assert.Equal(ProcessMilestoneRules.Defaults[ProcessMilestone.ValoracionMedica], view.Deadlines[ProcessMilestone.ValoracionMedica]);
        Assert.Equal(new MilestoneDeadline(TimeSpan.FromMinutes(30), null), view.Deadlines[ProcessMilestone.InformeDerivacion]);
        Assert.Equal(3, view.Version);
        Assert.All(view.History, h => Assert.True(h.ChangedByCurrentAccount));
        var latest = view.History.First(h => h.Milestone == ProcessMilestone.ValoracionMedica);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.plazos_hitos_centro WHERE centro_id = @c", new { c = direccion.CenterId.Value }));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE centro_id = @c AND accion_codigo = 'PROCESS_DEADLINES_UPDATE'", new { c = direccion.CenterId.Value }));
        Assert.Equal(new MilestoneDeadline(TimeSpan.FromHours(12), TimeSpan.FromMinutes(30)), latest.Previous);
    }

    [Fact]
    public async Task UnaVersionAntigua_EsConflicto_YUnPlazoImposible_EsEntradaInvalida()
    {
        var direccion = await DireccionAsync();
        var service = BuildService(direccion.ExternalSubject);
        Assert.True((await service.SaveAsync(Save(direccion, 0, new ProcessDeadlineCommandItem(ProcessMilestone.LecturaIndicacion, 120, 15)))).Ok);

        var stale = await service.SaveAsync(Save(direccion, 0, new ProcessDeadlineCommandItem(ProcessMilestone.LecturaIndicacion, 60, 15)));
        var zero = await service.SaveAsync(Save(direccion, 1, new ProcessDeadlineCommandItem(ProcessMilestone.LecturaIndicacion, 0, 15)));
        var tooLong = await service.SaveAsync(Save(direccion, 1, new ProcessDeadlineCommandItem(ProcessMilestone.LecturaIndicacion, 10081, 15)));
        var repeated = await service.SaveAsync(Save(direccion, 1,
            new ProcessDeadlineCommandItem(ProcessMilestone.LecturaIndicacion, 60, 15), new ProcessDeadlineCommandItem(ProcessMilestone.LecturaIndicacion, 90, 15)));

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, zero.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, tooLong.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, repeated.Error!.Code);
    }

    [Fact]
    public async Task SoloDireccionConElPermiso_Gestiona_YCadaCentroTieneLosSuyos()
    {
        var withPermission = await DireccionAsync();
        var withoutPermission = await SeedFixture.CreateProfileAsync(SystemProfile.DireccionClinica);
        var denied = new List<ApplicationFailureCode?>
        {
            (await BuildService(withoutPermission.ExternalSubject).ReadAsync(new ReadProcessDeadlinesCommand(withoutPermission.ProfileScopeId, withoutPermission.CenterId))).Error?.Code,
            (await BuildService(withoutPermission.ExternalSubject).SaveAsync(Save(withoutPermission, 0, new ProcessDeadlineCommandItem(ProcessMilestone.ValoracionMedica, 60, 30)))).Error?.Code,
        };
        foreach (var profile in new[] { SystemProfile.Enfermeria, SystemProfile.Medicina, SystemProfile.Auxiliar, SystemProfile.Administracion })
        {
            var other = await SeedFixture.AddProfileToCenterAsync(profile, withPermission.CenterId, withPermission.UnitId);
            denied.Add((await BuildService(other.ExternalSubject).ReadAsync(new ReadProcessDeadlinesCommand(other.ProfileScopeId, other.CenterId))).Error?.Code);
        }

        Assert.All(denied, code => Assert.Equal(ApplicationFailureCode.AccessDenied, code));
        // Lo que cambia un centro no cambia los de otro.
        Assert.True((await BuildService(withPermission.ExternalSubject).SaveAsync(
            Save(withPermission, 0, new ProcessDeadlineCommandItem(ProcessMilestone.ValoracionMedica, 600, 30)))).Ok);
        var other2 = await new SqlProcessDeadlineRepository(TestDatabase.ConnectionFactory).GetEffectiveAsync(withoutPermission.CenterId);
        Assert.Equal(ProcessMilestoneRules.Defaults, other2);
    }

    [Fact]
    public async Task ElPermiso_SoloSeConcedeADireccionClinica_YElHistorialNoSeToca()
    {
        var direccion = await DireccionAsync();
        var enfermera = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, direccion.CenterId, direccion.UnitId);
        Assert.True((await BuildService(direccion.ExternalSubject).SaveAsync(
            Save(direccion, 0, new ProcessDeadlineCommandItem(ProcessMilestone.RealizacionIndicacion, 240, 20)))).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var grant = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.permisos_perfil (id, ambito_perfil_id, centro_id, permiso_codigo, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @scope, @center, 'PROCESS_DEADLINES_MANAGE', SYSUTCDATETIME(), @account)
            """, new { scope = enfermera.ProfileScopeId, center = enfermera.CenterId.Value, account = enfermera.AccountId.Value }));
        var update = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.plazos_hitos_centro_historial SET plazo_nuevo_minutos = 1 WHERE centro_id = @c", new { c = direccion.CenterId.Value }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.plazos_hitos_centro_historial WHERE centro_id = @c", new { c = direccion.CenterId.Value }));

        Assert.Contains("PROFILE_PERMISSION_NOT_ALLOWED", grant.Message);
        Assert.Contains("PROCESS_DEADLINES_HISTORY_IMMUTABLE", update.Message);
        Assert.Contains("PROCESS_DEADLINES_HISTORY_IMMUTABLE", delete.Message);
    }
}

file sealed class FixedDeadlinesSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
