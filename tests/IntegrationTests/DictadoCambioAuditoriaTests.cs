using Dapper;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>El dictado de «Registrar cambio» (ADR 0009 y 0011) con SQL real y un proveedor falso: el ámbito y el residente asignado se comprueban
/// contra la base de datos y el uso queda en dbo.eventos_auditoria antes de que nada salga hacia el proveedor.</summary>
public class DictadoCambioAuditoriaTests
{
    private static SuggestDailyChangeFromVoice Build(string externalSubject, FakeVoiceProvider provider) => new(
        new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new SqlAssignedResidentDirectory(TestDatabase.ConnectionFactory),
        new FixedSession(externalSubject), new SqlVoiceDictationAudit(TestDatabase.ConnectionFactory), provider, provider);

    private static SuggestDailyChangeFromVoiceCommand Command(SeededProfile auxiliar, ResidentId resident, Guid operationId) =>
        new(auxiliar.ProfileScopeId, auxiliar.CenterId, resident, new MemoryStream([1, 2, 3]), 3, "audio/webm", operationId);

    private static async Task<(SeededProfile Admin, SeededProfile Auxiliar, ResidentId Resident)> SeedAssignedAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var auxiliar = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var resident = await new SqlResidentRepository(TestDatabase.ConnectionFactory).CreateWithInitialLocationAsync(new CreateResidentInput(
            admin.AccountId, SystemProfile.Administracion, admin.CenterId, admin.UnitId,
            "Residente Dictado Prueba", new DateOnly(1950, 5, 5), DocumentedSexCode.Mujer, null, null, null, null, null, Guid.NewGuid()));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
            VALUES (@Id, @ProfileScopeId, @CenterId, @ResidentId, SYSUTCDATETIME(), @AccountId)
            """, new
        {
            Id = Guid.NewGuid(), ProfileScopeId = auxiliar.ProfileScopeId, CenterId = admin.CenterId.Value,
            ResidentId = resident.ResidentId.Value, AccountId = admin.AccountId.Value,
        });
        return (admin, auxiliar, resident.ResidentId);
    }

    private static async Task<List<AuditRow>> AuditRowsAsync(AccountId account)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<AuditRow>("""
            SELECT perfil_activo AS Profile, unidad_id AS UnitId, residente_id AS ResidentId, tipo_recurso AS ResourceType, recurso_id AS ResourceId,
                   accion_codigo AS Action, proposito_codigo AS Purpose
            FROM dbo.eventos_auditoria WITH (NOLOCK)
            WHERE cuenta_id = @Account AND accion_codigo = 'VOICE_DICTATION_USED'
            """, new { Account = account.Value })).ToList();
    }

    private sealed record AuditRow(string Profile, Guid UnitId, Guid ResidentId, string ResourceType, Guid ResourceId, string Action, string? Purpose);

    [Fact]
    public async Task Sugerir_ConUnResidenteAsignado_AuditaUnaVez_YLlamaAlProveedorDespues()
    {
        var (admin, auxiliar, resident) = await SeedAssignedAsync();
        var provider = new FakeVoiceProvider
        {
            Transcript = "tiene una herida en el talón",
            Output = new VoiceModelOutput([new VoiceModelArea(DailyChangeAreaCode.LesionesPiel.ToCode(), "herida en el talón", [DailyChangeAreaOptionCode.Herida.ToCode()])]),
        };
        var operationId = Guid.NewGuid();

        var result = await Build(auxiliar.ExternalSubject, provider).ExecuteAsync(Command(auxiliar, resident, operationId));

        Assert.True(result.Ok);
        Assert.Equal(["transcribir", "estructurar"], provider.Calls);
        Assert.Equal([DailyChangeAreaOptionCode.Herida], Assert.Single(result.Value!.Areas).Options);
        var row = Assert.Single(await AuditRowsAsync(auxiliar.AccountId));
        Assert.Equal(("AUXILIAR", admin.UnitId.Value, resident.Value), (row.Profile, row.UnitId, row.ResidentId));
        Assert.Equal(("VOICE_DICTATION", operationId, "VOICE_DICTATION_USED"), (row.ResourceType, row.ResourceId, row.Action));
        Assert.Null(row.Purpose);
    }

    [Fact]
    public async Task Sugerir_SiElProveedorFalla_ElUsoQuedaAuditado_ConResultadoNoDisponible()
    {
        var (_, auxiliar, resident) = await SeedAssignedAsync();
        var provider = new FakeVoiceProvider { Fails = true };

        var result = await Build(auxiliar.ExternalSubject, provider).ExecuteAsync(Command(auxiliar, resident, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.Unavailable, result.Error!.Code);
        Assert.Single(await AuditRowsAsync(auxiliar.AccountId));
    }

    [Fact]
    public async Task Sugerir_ConUnResidenteNoAsignado_EsAccesoDenegado_SinAuditarNiLlamarAlProveedor()
    {
        var (admin, _, resident) = await SeedAssignedAsync();
        var otro = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Auxiliar, admin.CenterId, admin.UnitId);
        var provider = new FakeVoiceProvider();

        var result = await Build(otro.ExternalSubject, provider).ExecuteAsync(Command(otro, resident, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Empty(provider.Calls);
        Assert.Empty(await AuditRowsAsync(otro.AccountId));
    }

    [Fact]
    public async Task Sugerir_ConOtroPerfil_EsAccesoDenegado_SinAuditar()
    {
        var (admin, _, resident) = await SeedAssignedAsync();
        var enfermeria = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        var provider = new FakeVoiceProvider();

        var result = await Build(enfermeria.ExternalSubject, provider).ExecuteAsync(Command(enfermeria, resident, Guid.NewGuid()));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Empty(provider.Calls);
        Assert.Empty(await AuditRowsAsync(enfermeria.AccountId));
    }
}

file sealed class FixedSession(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
