using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>Traslado del residente (script 0040, CJ 2026-10-07): ubicación, eventos abiertos que pasan a la unidad de destino, borrador de
/// basal cancelado, idempotencia, ámbito y la guarda de la base de datos. Cada prueba crea su propio centro.</summary>
public class TrasladoResidenteTests
{
    private static ResidentTransferApplicationService Build(string externalSubject) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedTransferSessionIdentityProvider(externalSubject),
        new SqlResidentTransferRepository(TestDatabase.ConnectionFactory));

    private static async Task GrantUnitAsync(SeededProfile profile, UnitId unitId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @ProfileScopeId, @CenterId, @UnitId, SYSUTCDATETIME(), @AccountId)
            """, new { profile.ProfileScopeId, CenterId = profile.CenterId.Value, UnitId = unitId.Value, AccountId = profile.AccountId.Value });
    }

    private static TransferResidentCommand Transfer(
        SeededProfile admin, ResidentId residentId, UnitId expected, UnitId destination, Guid? operationId = null) =>
        new(admin.ProfileScopeId, admin.CenterId, residentId, expected, destination, null, null, operationId ?? Guid.NewGuid());

    private static async Task<T> QueryAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Sin resultado.");
    }

    /// <summary>Una Administración con dos unidades, un residente en la primera y dos profesionales de Enfermería (una en cada unidad).</summary>
    private static async Task<(SeededProfile Admin, UnitId Origin, UnitId Destination, ResidentId Resident, SeededProfile NurseOrigin, SeededProfile NurseDestination)> SeedAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var destination = await AdministracionResidentesTests.AddUnitAsync(admin.CenterId);
        await GrantUnitAsync(admin, destination);
        var resident = await AdministracionResidentesTests.CreateResidentAsync(admin, "Residente Traslado Integración");
        var nurseOrigin = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var nurseDestination = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, destination);
        return (admin, admin.UnitId, destination, resident, nurseOrigin, nurseDestination);
    }

    private static async Task<Guid> RegisterEventAsync(SeededProfile nurse, ResidentId residentId) =>
        (await EnfermeriaApplicationServiceTests.BuildService(nurse.ExternalSubject).RegisterClinicalEventAsync(new RegisterClinicalEventCommand(
            nurse.ProfileScopeId, nurse.CenterId, residentId, "Tos productiva desde la mañana.",
            DailyChangeClassification.Ordinario, null, Guid.NewGuid()))).Value!.EventId;

    private static async Task<int> PendingCountAsync(SeededProfile nurse) =>
        (await EnfermeriaApplicationServiceTests.BuildService(nurse.ExternalSubject).ListPendingChangesAsync(new ListPendingChangesCommand(
            nurse.ProfileScopeId, nurse.CenterId, DailyChangeClassification.Ordinario))).Value!.Count;

    [Fact]
    public async Task Trasladar_CambiaLaUbicacion_PasaLosEventosAbiertosALaUnidadDeDestino_YAudita()
    {
        var (admin, origin, destination, resident, nurseOrigin, nurseDestination) = await SeedAsync();
        var eventId = await RegisterEventAsync(nurseOrigin, resident);
        Assert.Equal(1, await PendingCountAsync(nurseOrigin));
        Assert.Equal(0, await PendingCountAsync(nurseDestination));
        var revisionBefore = await QueryAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId });

        var result = await Build(admin.ExternalSubject).TransferAsync(Transfer(admin, resident, origin, destination));

        Assert.True(result.Ok);
        Assert.Equal(new TransferResidentResult(1, false), result.Value);
        Assert.Equal(0, await PendingCountAsync(nurseOrigin));
        Assert.Equal(1, await PendingCountAsync(nurseDestination));
        Assert.Equal(destination.Value, await QueryAsync<Guid>("SELECT unidad_id FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
        Assert.Equal(revisionBefore + 1, await QueryAsync<int>("SELECT revision FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
        Assert.Equal(destination.Value, await QueryAsync<Guid>(
            "SELECT unidad_id FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @id AND vigente_hasta IS NULL", new { id = resident.Value }));
        Assert.Equal(2, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @id", new { id = resident.Value }));
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @id AND accion_codigo = 'RESIDENT_TRANSFER' AND unidad_id = @unit",
            new { id = resident.Value, unit = destination.Value }));
    }

    [Fact]
    public async Task Trasladar_CancelaElBorradorDeBasalAMedias()
    {
        var (admin, origin, destination, resident, nurseOrigin, _) = await SeedAsync();
        var draftId = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.basales_borrador
                    (id, residente_id, centro_id, creado_en_unidad_id, creado_por_cuenta_id, creado_por_perfil, creado_en,
                     actualizado_por_cuenta_id, actualizado_por_perfil, actualizado_en)
                VALUES (@draftId, @residentId, @centerId, @unitId, @accountId, 'ENFERMERIA', SYSUTCDATETIME(), @accountId, 'ENFERMERIA', SYSUTCDATETIME())
                """, new { draftId, residentId = resident.Value, centerId = admin.CenterId.Value, unitId = origin.Value, accountId = nurseOrigin.AccountId.Value });
        }

        var result = await Build(admin.ExternalSubject).TransferAsync(Transfer(admin, resident, origin, destination));

        Assert.Equal(new TransferResidentResult(0, true), result.Value);
        Assert.Equal("CANCELLED", await QueryAsync<string>("SELECT estado FROM dbo.basales_borrador WHERE id = @draftId", new { draftId }));
        Assert.Equal(admin.AccountId.Value, await QueryAsync<Guid>(
            "SELECT cancelado_por_cuenta_id FROM dbo.basales_borrador WHERE id = @draftId", new { draftId }));
    }

    [Fact]
    public async Task Trasladar_UnReenvioConElMismoIdentificadorNoRepiteNada_YOtroDestinoEsConflicto()
    {
        var (admin, origin, destination, resident, nurseOrigin, _) = await SeedAsync();
        await RegisterEventAsync(nurseOrigin, resident);
        var service = Build(admin.ExternalSubject);
        var operationId = Guid.NewGuid();

        var first = await service.TransferAsync(Transfer(admin, resident, origin, destination, operationId));
        var resent = await service.TransferAsync(Transfer(admin, resident, origin, destination, operationId));
        var other = await service.TransferAsync(Transfer(admin, resident, origin, origin, operationId));

        Assert.Equal(new TransferResidentResult(1, false), first.Value);
        Assert.Equal(first.Value, resent.Value);
        Assert.Equal(ApplicationFailureCode.Conflict, other.Error!.Code);
        Assert.Equal(1, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.traslados_residente WHERE residente_id = @id", new { id = resident.Value }));
    }

    [Fact]
    public async Task Trasladar_SiYaNoEstaEnLaUnidadQueSeVeia_EsConflicto_YSinCambioEsEntradaInvalida()
    {
        var (admin, origin, destination, resident, _, _) = await SeedAsync();
        var service = Build(admin.ExternalSubject);

        var unchanged = await service.TransferAsync(Transfer(admin, resident, origin, origin));
        Assert.True((await service.TransferAsync(Transfer(admin, resident, origin, destination))).Ok);
        var stale = await service.TransferAsync(Transfer(admin, resident, origin, origin));

        Assert.Equal(ApplicationFailureCode.InvalidInput, unchanged.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
    }

    [Fact]
    public async Task Trasladar_ADestinoFueraDelAmbito_YPorOtroPerfil_EsAccesoDenegado()
    {
        var (admin, origin, _, resident, nurseOrigin, _) = await SeedAsync();
        var foreign = await AdministracionResidentesTests.AddUnitAsync(admin.CenterId);

        var outOfScope = await Build(admin.ExternalSubject).TransferAsync(Transfer(admin, resident, origin, foreign));
        var byNurse = await Build(nurseOrigin.ExternalSubject).TransferAsync(new TransferResidentCommand(
            nurseOrigin.ProfileScopeId, nurseOrigin.CenterId, resident, origin, origin, null, null, Guid.NewGuid()));

        Assert.Equal(ApplicationFailureCode.AccessDenied, outOfScope.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(origin.Value, await QueryAsync<Guid>(
            "SELECT unidad_id FROM dbo.intervalos_ubicacion_residente WHERE residente_id = @id AND vigente_hasta IS NULL", new { id = resident.Value }));
    }

    [Fact]
    public async Task LaBaseDeDatos_NoDejaMoverUnEventoSinUnTrasladoDeEsaTransaccion_NiUnoCerrado()
    {
        var (_, origin, destination, resident, nurseOrigin, _) = await SeedAsync();
        var eventId = await RegisterEventAsync(nurseOrigin, resident);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var withoutTransfer = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.eventos_asistenciales SET unidad_id = @unit, revision = revision + 1 WHERE id = @eventId",
            new { unit = destination.Value, eventId }));

        Assert.Contains("CLINICAL_EVENT_TRANSITION_INVALID", withoutTransfer.Message);
        Assert.Equal(origin.Value, await connection.ExecuteScalarAsync<Guid>(
            "SELECT unidad_id FROM dbo.eventos_asistenciales WHERE id = @eventId", new { eventId }));
    }
}

file sealed class FixedTransferSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
