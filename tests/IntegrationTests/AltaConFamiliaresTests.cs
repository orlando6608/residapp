using Dapper;
using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>El alta registra a los familiares de contacto prioritario, referentes y tutores legales (script 0043, CJ 2026-10-07),
/// en la misma transacción, con Administración y con Enfermería con permiso.</summary>
public class AltaConFamiliaresTests
{
    private static CreateResident Build(string externalSubject) => new(
        new SqlAuthorizationEvidenceProvider(TestDatabase.ConnectionFactory), new FixedAltaSessionIdentityProvider(externalSubject),
        new SqlResidentRepository(TestDatabase.ConnectionFactory));

    private static CreateResidentCommand Command(SeededProfile seed, IReadOnlyList<NewResidentFamilyInput>? family, Guid? operationId = null) =>
        new(seed.ProfileScopeId, seed.CenterId, seed.UnitId, "Residente Alta Familiares", new DateOnly(1941, 6, 6), DocumentedSexCode.Mujer,
            null, null, null, null, null, operationId ?? Guid.NewGuid(), family);

    private static readonly IReadOnlyList<NewResidentFamilyInput> Family =
    [
        new("Ana Ruiz", "Hija", "600123456", "ana@example.org", true, false, true),
        new("Luis Gil", "Sobrino", "600765432", null, false, true, false),
        new(null, null, null, null, false, false, false),
    ];

    private static async Task<T> QueryAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Sin resultado.");
    }

    [Fact]
    public async Task Alta_ConFamiliares_LosVinculaConSusMarcas_DesignaElContactoPrioritario_YAudita()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);
        var operationId = Guid.NewGuid();

        var result = await service.ExecuteAsync(Command(admin, Family, operationId));
        var resent = await service.ExecuteAsync(Command(admin, Family, operationId));

        Assert.True(result.Ok);
        Assert.Equal(result.Value!.ResidentId, resent.Value!.ResidentId);
        var residentId = result.Value.ResidentId.Value;
        Assert.Equal(2, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId }));
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_familiares WHERE residente_id = @residentId AND es_referente = 1 AND es_tutor_legal = 0", new { residentId }));
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_familiares WHERE residente_id = @residentId AND es_tutor_legal = 1 AND es_referente = 0", new { residentId }));
        Assert.Equal("Ana Ruiz", await QueryAsync<string>("""
            SELECT f.nombre_visible FROM dbo.residentes_contacto_urgente d
              JOIN dbo.residentes_familiares l ON l.id = d.vinculo_id JOIN dbo.familiares f ON f.id = l.familiar_id
             WHERE d.residente_id = @residentId
            """, new { residentId }));
        Assert.Equal(2, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE residente_id = @residentId AND accion_codigo = 'FAMILY_MEMBER_CREATE'", new { residentId }));
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.familiares_autorizaciones_cambios c JOIN dbo.residentes_familiares l ON l.id = c.vinculo_id WHERE l.residente_id = @residentId",
            new { residentId }));
    }

    [Fact]
    public async Task Alta_ConUnaFilaAMediasONingunFamiliar_SeRechazaOSeDaDeAltaSinFamiliares()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var service = Build(admin.ExternalSubject);

        var partial = await service.ExecuteAsync(Command(admin, [new("Ana Ruiz", null, null, null, false, false, false)]));
        var none = await service.ExecuteAsync(Command(admin, null));

        Assert.Equal(ApplicationFailureCode.InvalidInput, partial.Error!.Code);
        Assert.True(none.Ok);
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId = none.Value!.ResidentId.Value }));
    }

    [Fact]
    public async Task Alta_PorEnfermeriaConPermiso_TambienRegistraLosFamiliares_YElContactoQuedaDesignadoPorEnfermeria()
    {
        var nurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria, [ResidentBaselinePermission.ResidentIdentityCreate.ToCode()]);

        var result = await Build(nurse.ExternalSubject).ExecuteAsync(Command(nurse, Family));

        Assert.True(result.Ok);
        var residentId = result.Value!.ResidentId.Value;
        Assert.Equal(2, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.residentes_familiares WHERE residente_id = @residentId", new { residentId }));
        Assert.Equal("ENFERMERIA", await QueryAsync<string>(
            "SELECT designado_por_perfil FROM dbo.residentes_contacto_urgente WHERE residente_id = @residentId", new { residentId }));
    }

    [Fact]
    public async Task Alta_ConDosContactosPrioritarios_NoDaDeAltaNiGuardaNada()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var before = await QueryAsync<int>("SELECT COUNT(*) FROM dbo.residentes WHERE centro_id = @centerId", new { centerId = admin.CenterId.Value });

        var result = await Build(admin.ExternalSubject).ExecuteAsync(Command(admin, [
            new("Ana Ruiz", "Hija", "600123456", null, false, false, true), new("Luis Gil", "Hijo", "600765432", null, false, false, true)]));

        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
        Assert.Equal(before, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.residentes WHERE centro_id = @centerId", new { centerId = admin.CenterId.Value }));
    }
}

file sealed class FixedAltaSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
