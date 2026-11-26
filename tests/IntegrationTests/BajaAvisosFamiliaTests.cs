using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Families;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>La baja de avisos por familiar (script 0123): cómo se anota, quién puede, qué la conserva y qué la borra. Las condiciones están en
/// docs/privacidad/codigo-acceso-familiar-por-correo.md, apartado «PR 3: baja de avisos». Que cada aviso la respete está en las pruebas de cada aviso.</summary>
public class BajaAvisosFamiliaTests
{
    private const string Address = "familia.baja.ficticia@example.test";

    private sealed record Family(SeededProfile Admin, ResidentId ResidentId, Guid LinkId, AdministracionApplicationService Service);

    private sealed record NoticesRow(DateTime? At, Guid? By);

    private static async Task<Family> FamilyAsync(string name, string? email = Address)
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, name);
        var service = Build(admin.ExternalSubject);
        var added = await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Lucía Pérez", "Hija", "600 123 456", email, ConfirmarCorreo: email is not null));
        Assert.True(added.Ok, added.Error?.Message);
        return new Family(admin, residentId, added.Value, service);
    }

    private static async Task<ResidentFamilyMember> MemberAsync(SeededProfile admin, ResidentId residentId) =>
        Assert.Single((await Build(admin.ExternalSubject).FindResidentAsync(
            new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, residentId))).Value!.Family);

    private static Task<ApplicationResult<bool>> SetAsync(Family family, bool optOut, DateTimeOffset? seen = null, bool requested = false, SeededProfile? actor = null)
    {
        var who = actor ?? family.Admin;
        return Build(who.ExternalSubject).SetFamilyNoticesOptOutAsync(new SetFamilyNoticesOptOutCommand(
            who.ProfileScopeId, who.CenterId, family.ResidentId, family.LinkId, optOut, seen, requested));
    }

    private static async Task<Guid> FamilyIdAsync(Guid linkId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId", new { linkId });
    }

    private static async Task<NoticesRow> RowAsync(Guid linkId)
    {
        var familyId = await FamilyIdAsync(linkId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QuerySingleAsync<NoticesRow>(
            "SELECT avisos_baja_en AS At, avisos_baja_por_cuenta_id AS [By] FROM dbo.familiares WHERE id = @familyId", new { familyId });
    }

    private static async Task<int> AuditAsync(ResidentId residentId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND accion_codigo = @Action",
            new { Id = residentId.Value, Action = action });
    }

    [Theory]
    [InlineData(Address)]
    [InlineData(null)] // la baja es de la persona: no depende de que tenga correo
    public async Task DarDeBaja_AnotaDesdeCuandoYQuien_ConSuAuditoria_SinTocarNadaMas(string? email)
    {
        var family = await FamilyAsync("Residente Baja Avisos Alta", email);
        var before = await MemberAsync(family.Admin, family.ResidentId);

        var result = await SetAsync(family, optOut: true);

        Assert.True(result.Ok, result.Error?.Message);
        var after = await MemberAsync(family.Admin, family.ResidentId);
        Assert.NotNull(after.NoticesOptOutAt);
        Assert.Equal(family.Admin.AccountId.Value, (await RowAsync(family.LinkId)).By);
        Assert.Equal((before.DisplayName, before.Phone, before.Email, before.EmailConfirmedAt), (after.DisplayName, after.Phone, after.Email, after.EmailConfirmedAt));
        Assert.Equal(1, await AuditAsync(family.ResidentId, "FAMILY_NOTICES_OPT_OUT"));
        Assert.Equal(0, await AuditAsync(family.ResidentId, "FAMILY_MEMBER_UPDATE"));
    }

    [Fact]
    public async Task VolverAActivar_ExigeQueLaPersonaLoHayaPedido_YQuitaLaBaja()
    {
        var family = await FamilyAsync("Residente Baja Avisos Reactivar");
        Assert.True((await SetAsync(family, optOut: true)).Ok);
        var seen = (await MemberAsync(family.Admin, family.ResidentId)).NoticesOptOutAt;

        var unrequested = await SetAsync(family, optOut: false, seen);
        Assert.Equal(ApplicationFailureCode.InvalidInput, unrequested.Error!.Code);
        Assert.Equal(ApplicationResultRunner.FamilyNoticesRequestRequiredMessage, unrequested.Error.Message);
        Assert.NotNull((await RowAsync(family.LinkId)).At);

        var requested = await SetAsync(family, optOut: false, seen, requested: true);

        Assert.True(requested.Ok, requested.Error?.Message);
        var row = await RowAsync(family.LinkId);
        Assert.Equal((null, null), (row.At, row.By));
        Assert.Equal(1, await AuditAsync(family.ResidentId, "FAMILY_NOTICES_OPT_IN"));
    }

    [Fact]
    public async Task RepetirOUsarUnaPantallaVieja_EsConflicto_SinSegundaAuditoria_NiLevantarUnaBajaPosterior()
    {
        var family = await FamilyAsync("Residente Baja Avisos Conflicto");

        var notOptedOut = await SetAsync(family, optOut: false, requested: true);
        Assert.True((await SetAsync(family, optOut: true)).Ok);
        var first = (await MemberAsync(family.Admin, family.ResidentId)).NoticesOptOutAt;
        var again = await SetAsync(family, optOut: true);
        var againSeeingIt = await SetAsync(family, optOut: true, first);

        Assert.Equal(ApplicationFailureCode.Conflict, notOptedOut.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, againSeeingIt.Error!.Code);
        Assert.Equal(1, await AuditAsync(family.ResidentId, "FAMILY_NOTICES_OPT_OUT"));

        // Entre medias se reactiva y la persona vuelve a pedir la baja: una pantalla que aún enseña la primera no levanta la segunda.
        Assert.True((await SetAsync(family, optOut: false, first, requested: true)).Ok);
        await Task.Delay(20);
        Assert.True((await SetAsync(family, optOut: true)).Ok);
        var stale = await SetAsync(family, optOut: false, first, requested: true);

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.NotNull((await RowAsync(family.LinkId)).At);
        Assert.Equal(1, await AuditAsync(family.ResidentId, "FAMILY_NOTICES_OPT_IN"));
    }

    [Fact]
    public async Task LaBaja_EsDeLaFicha_ValeParaTodosSusResidentes_YSeConservaAlCambiarElCorreoConfirmarlo_YDesvincular()
    {
        var family = await FamilyAsync("Residente Baja Avisos Persona A");
        var other = await FamilyAsync("Residente Baja Avisos Ajeno");
        var second = await CreateResidentAsync(family.Admin, "Residente Baja Avisos Persona B");
        var secondLink = await family.Service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            family.Admin.ProfileScopeId, family.Admin.CenterId, second, Guid.NewGuid(), await FamilyIdAsync(family.LinkId), "Hija"));
        Assert.True(secondLink.Ok, secondLink.Error?.Message);

        Assert.True((await SetAsync(family, optOut: true)).Ok);
        var since = (await MemberAsync(family.Admin, second)).NoticesOptOutAt;

        Assert.NotNull(since);
        Assert.Null((await MemberAsync(other.Admin, other.ResidentId)).NoticesOptOutAt);

        // Cambiar la dirección, borrarla, volver a ponerla confirmada y un cambio directo en la base de datos: la baja sigue.
        async Task SaveAsync(string? email, bool confirm)
        {
            var member = await MemberAsync(family.Admin, second);
            var version = new FamilyMemberData(member.DisplayName, member.Relationship, member.Phone, member.Email, member.IsReferent, member.IsLegalGuardian).Version;
            var saved = await family.Service.UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
                family.Admin.ProfileScopeId, family.Admin.CenterId, second, secondLink.Value, member.DisplayName, member.Relationship, member.Phone, email, version,
                member.IsReferent, member.IsLegalGuardian, confirm));
            Assert.True(saved.Ok, saved.Error?.Message);
        }

        await SaveAsync("otra.ficticia@example.test", confirm: false);
        await SaveAsync(null, confirm: false);
        await SaveAsync("tercera.ficticia@example.test", confirm: true);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = N'cuarta.ficticia@example.test' WHERE id = @id", new { id = await FamilyIdAsync(family.LinkId) });
        }

        Assert.True((await family.Service.UnlinkFamilyMemberAsync(new UnlinkFamilyMemberCommand(
            family.Admin.ProfileScopeId, family.Admin.CenterId, family.ResidentId, family.LinkId, "Prueba."))).Ok);

        Assert.Equal(since, (await MemberAsync(family.Admin, second)).NoticesOptOutAt);
    }

    [Fact]
    public async Task SoloAdministracionDelCentro_PuedeDarDeBajaOReactivar()
    {
        var family = await FamilyAsync("Residente Baja Avisos Permisos");
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, family.Admin.CenterId, family.Admin.UnitId);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var byNurse = await SetAsync(family, optOut: true, actor: nurse);
        var byOtherCenter = await SetAsync(family, optOut: true, actor: otherCenter);

        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOtherCenter.Error!.Code);
        Assert.Null((await RowAsync(family.LinkId)).At);
    }

    [Fact]
    public async Task LaBaseDeDatos_NoAdmiteMediaBaja()
    {
        var family = await FamilyAsync("Residente Baja Avisos Restriccion");
        var familyId = await FamilyIdAsync(family.LinkId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var withoutAccount = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.familiares SET avisos_baja_en = SYSUTCDATETIME() WHERE id = @familyId", new { familyId }));
        var withoutDate = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.familiares SET avisos_baja_por_cuenta_id = @by WHERE id = @familyId", new { familyId, by = family.Admin.AccountId.Value }));

        Assert.Contains("CK_fam_avisos_baja", withoutAccount.Message);
        Assert.Contains("CK_fam_avisos_baja", withoutDate.Message);
    }
}
