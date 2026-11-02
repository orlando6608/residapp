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

/// <summary>La casilla «Dirección confirmada con la familia» (script 0084; CJ, aviso-citas-por-correo-cj 2.1 A, 2026-11-02). Las condiciones que se comprueban (D1 a D4) están en
/// docs/privacidad/direccion-confirmada-familia.md, apartado 3. Los avisos sin la marca están en AvisoPropuestaCitaTests y AvisoCambioCitaTests.</summary>
public class DireccionConfirmadaFamiliaTests
{
    private const string Address = "lucia.ficticia@example.test";

    private static async Task<(SeededProfile Admin, ResidentId ResidentId, Guid LinkId, AdministracionApplicationService Service)> FamilyAsync(
        string name, string? email = null, bool confirm = false)
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, name);
        var service = Build(admin.ExternalSubject);
        var added = await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Lucía Pérez", "Hija", "600 123 456", email, ConfirmarCorreo: confirm));
        Assert.True(added.Ok, added.Error?.Message);
        return (admin, residentId, added.Value, service);
    }

    private static async Task<ResidentFamilyMember> MemberAsync(SeededProfile admin, ResidentId residentId) =>
        Assert.Single((await Build(admin.ExternalSubject).FindResidentAsync(
            new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, residentId))).Value!.Family);

    private static string Version(ResidentFamilyMember member) =>
        new FamilyMemberData(member.DisplayName, member.Relationship, member.Phone, member.Email, member.IsReferent, member.IsLegalGuardian).Version;

    private static UpdateFamilyMemberCommand Update(
        SeededProfile admin, ResidentId residentId, Guid linkId, ResidentFamilyMember before, string? email, string? phone = null, bool confirm = false) =>
        new(admin.ProfileScopeId, admin.CenterId, residentId, linkId, before.DisplayName, before.Relationship, phone ?? before.Phone, email, Version(before),
            before.IsReferent, before.IsLegalGuardian, confirm);

    private static async Task<int> AuditAsync(ResidentId residentId, string action)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @Id AND accion_codigo = @Action",
            new { Id = residentId.Value, Action = action });
    }

    private static async Task<Guid> FamilyIdAsync(Guid linkId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId", new { linkId });
    }

    private sealed record FamilyRow(string? Address, DateTime? ConfirmedAt, Guid? ConfirmedBy);

    private static async Task<FamilyRow> RowAsync(Guid familyId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.QuerySingleAsync<FamilyRow>(
            "SELECT correo AS Address, correo_confirmado_en AS ConfirmedAt, correo_confirmado_por_cuenta_id AS ConfirmedBy FROM dbo.familiares WHERE id = @familyId", new { familyId });
    }

    private static void AssertRow(FamilyRow row, string? address, bool confirmed)
    {
        Assert.Equal(address, row.Address);
        Assert.Equal(confirmed, row.ConfirmedAt is not null);
        Assert.Equal(confirmed, row.ConfirmedBy is not null);
    }

    [Fact]
    public async Task Alta_SinLaCasilla_QuedaSinConfirmar_YConLaCasilla_ConfirmadaConSuAuditoria()
    {
        var plain = await FamilyAsync("Residente Dirección Uno", Address);
        var confirmed = await FamilyAsync("Residente Dirección Dos", Address, confirm: true);

        var plainMember = await MemberAsync(plain.Admin, plain.ResidentId);
        var confirmedMember = await MemberAsync(confirmed.Admin, confirmed.ResidentId);

        Assert.Null(plainMember.EmailConfirmedAt);
        Assert.NotNull(confirmedMember.EmailConfirmedAt);
        Assert.Equal(0, await AuditAsync(plain.ResidentId, "FAMILY_EMAIL_CONFIRM"));
        Assert.Equal(1, await AuditAsync(confirmed.ResidentId, "FAMILY_EMAIL_CONFIRM"));
        Assert.Equal(1, await AuditAsync(confirmed.ResidentId, "FAMILY_MEMBER_CREATE"));
        // quién la puso queda en la fila, atado a la cuenta de Administración que lo hizo
        Assert.Equal(confirmed.Admin.AccountId.Value, (await RowAsync(await FamilyIdAsync(confirmed.LinkId))).ConfirmedBy);
    }

    [Fact]
    public async Task NoSePuedeConfirmarSinCorreo()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Dirección Sin Correo");
        var service = Build(admin.ExternalSubject);

        var added = await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Lucía Pérez", "Hija", "600 123 456", null, ConfirmarCorreo: true));

        Assert.Equal(ApplicationFailureCode.InvalidInput, added.Error!.Code);
        Assert.Empty((await Build(admin.ExternalSubject).FindResidentAsync(
            new FindAdministrativeResidentQuery(admin.ProfileScopeId, admin.CenterId, residentId))).Value!.Family);
    }

    [Fact]
    public async Task Editar_SoloMarcarLaCasilla_Confirma_SinTocarOtrosDatos_YRepetirloNoHaceNada()
    {
        var (admin, residentId, linkId, service) = await FamilyAsync("Residente Dirección Tres", Address);
        var before = await MemberAsync(admin, residentId);

        var confirmed = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, before, Address, confirm: true));
        var after = await MemberAsync(admin, residentId);
        var again = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, after, Address, confirm: true));

        Assert.True(confirmed.Ok, confirmed.Error?.Message);
        Assert.NotNull(after.EmailConfirmedAt);
        Assert.Equal(ApplicationFailureCode.InvalidInput, again.Error!.Code);
        Assert.Equal(1, await AuditAsync(residentId, "FAMILY_EMAIL_CONFIRM"));
        // solo se confirmó: no son «datos del familiar cambiados» ni un cambio de dirección
        Assert.Equal(0, await AuditAsync(residentId, "FAMILY_MEMBER_UPDATE"));
        Assert.Equal(0, await AuditAsync(residentId, "FAMILY_EMAIL_CHANGE"));
    }

    [Fact]
    public async Task CambiarOBorrarLaDireccion_QuitaLaConfirmacion_ConSuAuditoria_YCambiarOtroDatoLaConserva()
    {
        var (admin, residentId, linkId, service) = await FamilyAsync("Residente Dirección Cuatro", Address, confirm: true);

        // otro dato: la marca se conserva
        var phone = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, await MemberAsync(admin, residentId), Address, phone: "611 222 333"));
        Assert.True(phone.Ok, phone.Error?.Message);
        Assert.NotNull((await MemberAsync(admin, residentId)).EmailConfirmedAt);
        Assert.Equal(0, await AuditAsync(residentId, "FAMILY_EMAIL_UNCONFIRM"));

        // dirección nueva sin la casilla: la marca se pierde y hay que volver a confirmar
        var changed = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, await MemberAsync(admin, residentId), "otra.ficticia@example.test"));
        Assert.True(changed.Ok, changed.Error?.Message);
        Assert.Null((await MemberAsync(admin, residentId)).EmailConfirmedAt);
        Assert.Equal(1, await AuditAsync(residentId, "FAMILY_EMAIL_CHANGE"));
        Assert.Equal(1, await AuditAsync(residentId, "FAMILY_EMAIL_UNCONFIRM"));

        // dirección nueva leída y confirmada en el mismo guardado: queda confirmada
        var confirmedNew = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, await MemberAsync(admin, residentId), "tercera.ficticia@example.test", confirm: true));
        Assert.True(confirmedNew.Ok, confirmedNew.Error?.Message);
        Assert.NotNull((await MemberAsync(admin, residentId)).EmailConfirmedAt);
        Assert.Equal(2, await AuditAsync(residentId, "FAMILY_EMAIL_CHANGE"));
        Assert.Equal(2, await AuditAsync(residentId, "FAMILY_EMAIL_CONFIRM"));

        // borrar la dirección también la quita
        var removed = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, await MemberAsync(admin, residentId), null));
        Assert.True(removed.Ok, removed.Error?.Message);
        var member = await MemberAsync(admin, residentId);
        Assert.Equal((null, null), (member.Email, member.EmailConfirmedAt));
        Assert.Equal(2, await AuditAsync(residentId, "FAMILY_EMAIL_UNCONFIRM"));
    }

    [Fact]
    public async Task QuitarLaConfirmacion_SoloQuitaLaMarca_ConLaVersionQueVioQuienLaQuita()
    {
        var (admin, residentId, linkId, service) = await FamilyAsync("Residente Dirección Cinco", Address, confirm: true);
        var seen = await MemberAsync(admin, residentId);
        RemoveFamilyEmailConfirmationCommand Remove(string version) => new(admin.ProfileScopeId, admin.CenterId, residentId, linkId, version);

        // otra persona cambió la dirección entretanto: la versión vieja no se aplica
        Assert.True((await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, seen, "nueva.ficticia@example.test", confirm: true))).Ok);
        var stale = await service.RemoveFamilyEmailConfirmationAsync(Remove(Version(seen)));
        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.NotNull((await MemberAsync(admin, residentId)).EmailConfirmedAt);

        var current = await MemberAsync(admin, residentId);
        var removed = await service.RemoveFamilyEmailConfirmationAsync(Remove(Version(current)));
        var already = await service.RemoveFamilyEmailConfirmationAsync(Remove(Version(current)));

        Assert.True(removed.Ok, removed.Error?.Message);
        var member = await MemberAsync(admin, residentId);
        Assert.Equal(("nueva.ficticia@example.test", null), (member.Email, member.EmailConfirmedAt));
        Assert.Equal(ApplicationFailureCode.Conflict, already.Error!.Code);
        Assert.Equal(1, await AuditAsync(residentId, "FAMILY_EMAIL_UNCONFIRM"));
    }

    [Fact]
    public async Task UnaMarcaHechaSobreUnFormularioViejo_NoSeAplicaALaDireccionNueva()
    {
        var (admin, residentId, linkId, service) = await FamilyAsync("Residente Dirección Seis", Address);
        var seen = await MemberAsync(admin, residentId);

        // mientras tanto otra persona cambia la dirección
        Assert.True((await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, seen, "cambiada.ficticia@example.test"))).Ok);
        var stale = await service.UpdateFamilyMemberAsync(Update(admin, residentId, linkId, seen, Address, confirm: true));

        Assert.Equal(ApplicationFailureCode.Conflict, stale.Error!.Code);
        Assert.Null((await MemberAsync(admin, residentId)).EmailConfirmedAt);
        Assert.Equal(0, await AuditAsync(residentId, "FAMILY_EMAIL_CONFIRM"));
    }

    [Fact]
    public async Task LaMarcaEsDelFamiliar_UnaDireccionCambiadaDesdeOtroResidenteLaQuitaParaTodos()
    {
        var (admin, residentOne, linkOne, service) = await FamilyAsync("Residente Dirección Siete A", Address, confirm: true);
        var residentTwo = await CreateResidentAsync(admin, "Residente Dirección Siete B");
        var linkTwo = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentTwo, Guid.NewGuid(), await FamilyIdAsync(linkOne), "Hija"));
        Assert.True(linkTwo.Ok, linkTwo.Error?.Message);

        Assert.NotNull((await MemberAsync(admin, residentTwo)).EmailConfirmedAt);

        var viaTwo = await service.UpdateFamilyMemberAsync(Update(admin, residentTwo, linkTwo.Value, await MemberAsync(admin, residentTwo), "otra.ficticia@example.test"));

        Assert.True(viaTwo.Ok, viaTwo.Error?.Message);
        Assert.Null((await MemberAsync(admin, residentOne)).EmailConfirmedAt);
        Assert.Null((await MemberAsync(admin, residentTwo)).EmailConfirmedAt);
    }

    [Fact]
    public async Task LaBaseDeDatos_QuitaLaMarcaSolaSiCambiaLaDireccion_AunqueElCodigoNoLoHaga()
    {
        var (admin, _, linkId, _) = await FamilyAsync("Residente Dirección Ocho", Address, confirm: true);
        var familyId = await FamilyIdAsync(linkId);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        // UPDATE directo que solo cambia la dirección
        await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = N'Otra.Ficticia@example.test' WHERE id = @familyId", new { familyId });
        AssertRow(await RowAsync(familyId), "Otra.Ficticia@example.test", confirmed: false);

        // mismas letras con otra capitalización: también es otra dirección (se compara byte a byte)
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = N'otra.ficticia@example.test', correo_confirmado_en = SYSUTCDATETIME(), correo_confirmado_por_cuenta_id = @by WHERE id = @familyId",
            new { familyId, by = admin.AccountId.Value });
        Assert.NotNull((await RowAsync(familyId)).ConfirmedAt);
        await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = N'OTRA.ficticia@example.test' WHERE id = @familyId", new { familyId });
        Assert.Null((await RowAsync(familyId)).ConfirmedAt);

        // quitar la dirección también la quita
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo_confirmado_en = SYSUTCDATETIME(), correo_confirmado_por_cuenta_id = @by WHERE id = @familyId", new { familyId, by = admin.AccountId.Value });
        await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = NULL WHERE id = @familyId", new { familyId });
        AssertRow(await RowAsync(familyId), null, confirmed: false);
    }

    [Fact]
    public async Task LaBaseDeDatos_NoAdmiteUnaConfirmacionDeUnaDireccionAnteriorNiSinDireccionNiMediaPuesta()
    {
        var (admin, _, linkId, _) = await FamilyAsync("Residente Dirección Nueve", Address, confirm: true);
        var familyId = await FamilyIdAsync(linkId);
        var confirmedAt = (await RowAsync(familyId)).ConfirmedAt;
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        // cambiar la dirección repitiendo la fecha de la confirmación anterior no la mantiene: solo una posterior es una confirmación deliberada
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = N'distinta.ficticia@example.test', correo_confirmado_en = @confirmedAt WHERE id = @familyId", new { familyId, confirmedAt });
        Assert.Null((await RowAsync(familyId)).ConfirmedAt);

        // una confirmación sin dirección no existe (alta o cambio)
        await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = NULL WHERE id = @familyId", new { familyId });
        var withoutAddress = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo_confirmado_en = SYSUTCDATETIME(), correo_confirmado_por_cuenta_id = @by WHERE id = @familyId", new { familyId, by = admin.AccountId.Value }));
        Assert.Equal(50840, withoutAddress.Number);

        // la fecha y la cuenta van juntas o no van
        await connection.ExecuteAsync("UPDATE dbo.familiares SET correo = N'cuarta.ficticia@example.test' WHERE id = @familyId", new { familyId });
        var half = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo_confirmado_en = SYSUTCDATETIME() WHERE id = @familyId", new { familyId }));
        Assert.Equal(547, half.Number);
    }

    [Fact]
    public async Task UnFamiliarQueYaTeniaCorreo_NoNaceConfirmado()
    {
        // D3: ningún script ni inserción pone la marca; la columna nace nula
        var (_, _, linkId, _) = await FamilyAsync("Residente Dirección Diez", Address);

        AssertRow(await RowAsync(await FamilyIdAsync(linkId)), Address, confirmed: false);
    }
}
