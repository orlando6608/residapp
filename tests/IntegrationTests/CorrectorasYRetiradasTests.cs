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
using ResidApp.Domain.Families;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>Portal Familiar, bloque 2 (script 0053): lo que se hace con un comunicado ya publicado. Una publicación correctora vinculada que la
/// familia ve junto al original, y la retirada excepcional que oculta el texto sin borrarlo. Las hacen Administración y Dirección Clínica.</summary>
public class CorrectorasYRetiradasTests
{
    private static FamilyCommunicationPublicationApplicationService Staff(string subject)
    {
        var publication = new SqlFamilyCommunicationPublication(TestDatabase.ConnectionFactory);
        return new FamilyCommunicationPublicationApplicationService(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedEnmiendaSessionIdentityProvider(subject),
            publication, publication, publication);
    }

    private static Task<ApplicationResult<bool>> CorrectAsync(SeededProfile who, Guid communicationId, string? text, FamilyCommunicationType? type = FamilyCommunicationType.Relevante) =>
        Staff(who.ExternalSubject).PublishCorrectiveAsync(new PublishFamilyCommunicationCorrectiveCommand(who.ProfileScopeId, who.CenterId, communicationId, type, text));

    private static Task<ApplicationResult<bool>> WithdrawAsync(SeededProfile who, Guid communicationId, string? reason) =>
        Staff(who.ExternalSubject).WithdrawAsync(new WithdrawFamilyCommunicationCommand(who.ProfileScopeId, who.CenterId, communicationId, reason));

    /// <summary>Un comunicado ya publicado (su hora llegó ayer) y otro por publicar (dentro de un día).</summary>
    private static Task<Guid> PublishedAsync(Setup setup, string text, FamilyCommunicationType type = FamilyCommunicationType.Ordinaria) =>
        AddCommunicationAsync(setup, text, type, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));

    private static Task<Guid> UnpublishedAsync(Setup setup, string text) =>
        AddCommunicationAsync(setup, text, FamilyCommunicationType.Ordinaria, DateTime.UtcNow, DateTime.UtcNow.AddDays(1));

    /// <summary>Una correctora insertada con la fecha de publicación que se quiera (para probar su ventana).</summary>
    private static async Task AddCorrectiveRowAsync(Setup setup, Guid communicationId, FamilyCommunicationType type, string text, DateTime publishedAt)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares_correctoras
                (id, comunicacion_id, centro_id, residente_id, tipo_codigo, texto, publicada_por_cuenta_id, publicada_en)
            VALUES (NEWID(), @communicationId, @centerId, @residentId, @type, @text, @accountId, @publishedAt)
            """, new
        {
            communicationId, centerId = setup.Admin.CenterId.Value, residentId = setup.ResidentId.Value, type = type.ToCode(), text,
            accountId = setup.Admin.AccountId.Value, publishedAt,
        });
    }

    [Fact]
    public async Task Administracion_PublicaUnaCorrectora_LaFamiliaLaVeJuntoAlOriginal_YElOriginalNoCambia()
    {
        var setup = await SetupAsync("Residente Correctora");
        var original = await PublishedAsync(setup, "Ha tenido fiebre.");
        var before = await ListAsync(setup);

        var corrected = await CorrectAsync(setup.Admin, original, "  Corrección: fue febrícula, no fiebre.  ");
        var after = await ListAsync(setup);
        var portal = Portal(setup.Subject);
        var query = await QueryAsync(setup.Subject);
        var originalDetail = (await portal.ReadCommunicationAsync(query, setup.LinkId, original)).Value!;
        var correctiveId = Assert.Single(after, c => c.Kind == FamilyPortalCommunicationKind.Corrective).Id;
        var correctiveDetail = (await portal.ReadCommunicationAsync(query, setup.LinkId, correctiveId)).Value!;
        var staff = (await Staff(setup.Admin.ExternalSubject).ListAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!.Single();

        Assert.True(corrected.Ok, corrected.Error?.Message);
        var untouched = Assert.Single(before);
        Assert.Equal(FamilyPortalCommunicationKind.Original, untouched.Kind);
        Assert.Equal(2, after.Count);
        var corrective = after[0];
        Assert.Equal(FamilyPortalCommunicationKind.Corrective, corrective.Kind);
        Assert.Equal("Corrección: fue febrícula, no fiebre.", corrective.Text);
        Assert.Equal(FamilyCommunicationType.Relevante, corrective.Type);
        Assert.Equal(original, corrective.Corrects!.OriginalId);
        Assert.True(corrective.Corrects.OriginalVisible);
        Assert.Equal("Ha tenido fiebre.", after[1].Text);
        Assert.Equal(FamilyPortalCommunicationKind.Original, after[1].Kind);
        Assert.Equal(corrective.Id, Assert.Single(originalDetail.Corrections).Id);
        Assert.Empty(correctiveDetail.Corrections);
        Assert.Equal(original, correctiveDetail.Communication.Corrects!.OriginalId);
        Assert.Equal("Ha tenido fiebre.", staff.Communication.Text);
        Assert.Equal("Corrección: fue febrícula, no fiebre.", Assert.Single(staff.Communication.Correctives).Text);
        Assert.True(staff.CanAmend);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'FAMILY_COMMUNICATION_CORRECTIVE' AND recurso_id = @c AND perfil_activo = 'ADMINISTRACION'",
            new { a = setup.Admin.AccountId.Value, c = original }));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE accion_codigo = 'FAMILY_PORTAL_READ' AND recurso_id = @c", new { c = correctiveId }));
        Assert.Equal("Ha tenido fiebre.", await TextAsync(original));
    }

    [Fact]
    public async Task DireccionClinica_TambienPuedePublicarUnaCorrectora_YRetirar_ConSuPerfilEnLaAuditoria()
    {
        var setup = await SetupAsync("Residente Dirección Enmienda");
        var direction = await SeedFixture.AddProfileToCenterAsync(SystemProfile.DireccionClinica, setup.Admin.CenterId, setup.Admin.UnitId);
        var first = await PublishedAsync(setup, "Primero.");
        var second = await PublishedAsync(setup, "Segundo.");

        var corrected = await CorrectAsync(direction, first, "Corrección de Dirección.", FamilyCommunicationType.Ordinaria);
        var withdrawn = await WithdrawAsync(direction, second, "Se publicó sobre el residente equivocado.");

        Assert.True(corrected.Ok, corrected.Error?.Message);
        Assert.True(withdrawn.Ok, withdrawn.Error?.Message);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'FAMILY_COMMUNICATION_CORRECTIVE' AND perfil_activo = 'DIRECCION_CLINICA'",
            new { a = direction.AccountId.Value }));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WHERE cuenta_id = @a AND accion_codigo = 'FAMILY_COMMUNICATION_WITHDRAW' AND recurso_id = @c AND perfil_activo = 'DIRECCION_CLINICA'",
            new { a = direction.AccountId.Value, c = second }));
    }

    [Fact]
    public async Task NoSePuedeCorregirNiRetirar_SinPublicar_NiPorQuienNoEsDeAdministracionODireccion_NiFueraDelCentro_NiConDatosInvalidos()
    {
        var setup = await SetupAsync("Residente Reglas Enmienda");
        var outsider = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var published = await PublishedAsync(setup, "Publicado.");
        var unpublished = await UnpublishedAsync(setup, "Todavía no.");

        var correctUnpublished = await CorrectAsync(setup.Admin, unpublished, "Corrección.");
        var withdrawUnpublished = await WithdrawAsync(setup.Admin, unpublished, "Motivo.");
        var byNurse = await CorrectAsync(setup.Nurse, published, "Corrección.");
        var withdrawByNurse = await WithdrawAsync(setup.Nurse, published, "Motivo.");
        var byOutsider = await CorrectAsync(outsider, published, "Corrección.");
        var withdrawByOutsider = await WithdrawAsync(outsider, published, "Motivo.");
        var noText = await CorrectAsync(setup.Admin, published, "   ");
        var noType = await CorrectAsync(setup.Admin, published, "Corrección.", type: null);
        var noReason = await WithdrawAsync(setup.Admin, published, "   ");
        var longReason = await WithdrawAsync(setup.Admin, published, new string('x', 501));
        var unknown = await CorrectAsync(setup.Admin, Guid.NewGuid(), "Corrección.");

        Assert.Equal(ApplicationFailureCode.Conflict, correctUnpublished.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, withdrawUnpublished.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, withdrawByNurse.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, withdrawByOutsider.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noText.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noType.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, noReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, longReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.AccessDenied, unknown.Error!.Code);
        Assert.Equal(0, await CountAsync(
            "SELECT (SELECT COUNT(*) FROM dbo.comunicaciones_familiares_correctoras WHERE centro_id = @c) + (SELECT COUNT(*) FROM dbo.comunicaciones_familiares_retiradas WHERE centro_id = @c)",
            new { c = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task Retirada_OcultaElTextoYLasCorrectoras_ConservaElOriginal_YNoSePuedeRepetirNiCorregirDespues()
    {
        var setup = await SetupAsync("Residente Retirada");
        var original = await PublishedAsync(setup, "Texto equivocado.");
        Assert.True((await CorrectAsync(setup.Admin, original, "Otra versión.")).Ok);
        var portal = Portal(setup.Subject);
        var query = await QueryAsync(setup.Subject);
        var correctiveId = (await portal.ReadResidentAsync(query, setup.LinkId)).Value!.Communications.Single(c => c.Kind == FamilyPortalCommunicationKind.Corrective).Id;

        var withdrawn = await WithdrawAsync(setup.Admin, original, "  Era de otro residente.  ");
        var list = await ListAsync(setup);
        var withdrawnDetail = (await portal.ReadCommunicationAsync(query, setup.LinkId, original)).Value!;
        var correctiveAfter = await portal.ReadCommunicationAsync(query, setup.LinkId, correctiveId);
        var again = await WithdrawAsync(setup.Admin, original, "Otra vez.");
        var correctAfter = await CorrectAsync(setup.Admin, original, "Tarde.");
        var staff = (await Staff(setup.Admin.ExternalSubject).ListAsync(setup.Admin.ProfileScopeId, setup.Admin.CenterId)).Value!.Single();

        Assert.True(withdrawn.Ok, withdrawn.Error?.Message);
        var item = Assert.Single(list);
        Assert.Equal(original, item.Id);
        Assert.Equal(FamilyPortalCommunicationKind.Withdrawn, item.Kind);
        Assert.Equal(string.Empty, item.Text);
        Assert.Null(item.Corrects);
        Assert.Equal(FamilyPortalCommunicationKind.Withdrawn, withdrawnDetail.Communication.Kind);
        Assert.Equal(string.Empty, withdrawnDetail.Communication.Text);
        Assert.Empty(withdrawnDetail.Corrections);
        Assert.False(correctiveAfter.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, correctAfter.Error!.Code);
        Assert.Equal("Texto equivocado.", await TextAsync(original));
        Assert.Equal("Era de otro residente.", staff.Communication.Withdrawal!.Reason);
        Assert.False(staff.CanAmend);
        Assert.Equal("Otra versión.", Assert.Single(staff.Communication.Correctives).Text);
    }

    [Fact]
    public async Task LaCorrectora_TieneSuPropiaVentana_YSeVeAunqueElOriginalYaNoSeVea()
    {
        var setup = await SetupAsync("Residente Ventana Correctora");
        // Un ordinario de hace 40 días ya no se ve (30); su correctora relevante de hace 20 días sí (6 meses), y una ordinaria de hace 31 días no.
        var old = await AddCommunicationAsync(setup, "Antiguo.", FamilyCommunicationType.Ordinaria, DateTime.UtcNow.AddDays(-41), DateTime.UtcNow.AddDays(-40));
        await AddCorrectiveRowAsync(setup, old, FamilyCommunicationType.Relevante, "Corrección relevante reciente.", DateTime.UtcNow.AddDays(-20));
        await AddCorrectiveRowAsync(setup, old, FamilyCommunicationType.Ordinaria, "Corrección ordinaria antigua.", DateTime.UtcNow.AddDays(-31));

        var list = await ListAsync(setup);

        var corrective = Assert.Single(list);
        Assert.Equal("Corrección relevante reciente.", corrective.Text);
        Assert.False(corrective.Corrects!.OriginalVisible);
        Assert.Equal(old, corrective.Corrects.OriginalId);
    }

    [Fact]
    public async Task OtraFamiliaDelMismoCentro_NoVeLasCorrectorasNiLasRetiradasDeOtroResidente()
    {
        var one = await SetupAsync("Residente Uno Enmienda");
        // Otro residente del mismo centro, con su propia familia y su propio acceso al portal.
        var otherResident = await AdministracionResidentesTests.CreateResidentAsync(one.Admin, "Residente Dos Enmienda");
        var otherLink = await AddFamilyAsync(one.Admin, otherResident, "Otra Persona");
        var otherSubject = $"portal-{Guid.NewGuid():N}"[..20];
        Assert.True((await AdministracionResidentesTests.Build(one.Admin.ExternalSubject).GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(one.Admin.ProfileScopeId, one.Admin.CenterId, otherResident, otherLink, otherSubject))).Ok);
        await ChangeAsync(one.Admin, otherResident, otherLink, FamilyAuthorizationChange.Abrir, 0);
        await ChangeAsync(one.Admin, otherResident, otherLink, FamilyAuthorizationChange.Activar, 1);
        var published = await PublishedAsync(one, "Del uno.");
        var withdrawn = await PublishedAsync(one, "Retirado del uno.");
        Assert.True((await CorrectAsync(one.Admin, published, "Corrección del uno.")).Ok);
        Assert.True((await WithdrawAsync(one.Admin, withdrawn, "Motivo.")).Ok);

        var otherQuery = await QueryAsync(otherSubject);
        var otherHome = await Portal(otherSubject).ReadResidentAsync(otherQuery, otherLink);
        var foreign = await Portal(otherSubject).ReadResidentAsync(otherQuery, one.LinkId);
        var foreignDetail = await Portal(otherSubject).ReadCommunicationAsync(otherQuery, otherLink, published);

        Assert.Empty(otherHome.Value!.Communications);
        Assert.False(foreign.Ok);
        Assert.False(foreignDetail.Ok);
        Assert.Equal(3, (await ListAsync(one)).Count);
    }


    [Fact]
    public async Task LaBaseDeDatos_ImpideCambiarOBorrarLasCorrectorasYLasRetiradas_YCorregirUnRetirado()
    {
        var setup = await SetupAsync("Residente Inmutable Enmienda");
        var communication = await PublishedAsync(setup, "Original.");
        Assert.True((await CorrectAsync(setup.Admin, communication, "Corrección.")).Ok);
        Assert.True((await WithdrawAsync(setup.Admin, communication, "Motivo.")).Ok);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var updateCorrective = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.comunicaciones_familiares_correctoras SET texto = N'Cambiado' WHERE comunicacion_id = @communication", new { communication }));
        var deleteCorrective = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.comunicaciones_familiares_correctoras WHERE comunicacion_id = @communication", new { communication }));
        var updateWithdrawal = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.comunicaciones_familiares_retiradas SET motivo = N'Cambiado' WHERE comunicacion_id = @communication", new { communication }));
        var deleteWithdrawal = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.comunicaciones_familiares_retiradas WHERE comunicacion_id = @communication", new { communication }));
        var correctAWithdrawn = await Assert.ThrowsAsync<SqlException>(() => AddCorrectiveRowAsync(
            setup, communication, FamilyCommunicationType.Ordinaria, "Tarde.", DateTime.UtcNow));
        var other = await PublishedAsync(setup, "Otro.");
        var foreignCorrective = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares_correctoras (id, comunicacion_id, centro_id, residente_id, tipo_codigo, texto, publicada_por_cuenta_id, publicada_en)
            VALUES (NEWID(), @other, @center, NEWID(), 'ORDINARIA', N'Texto', @account, SYSUTCDATETIME())
            """, new { other, center = setup.Admin.CenterId.Value, account = setup.Admin.AccountId.Value }));
        var foreignWithdrawal = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync("""
            INSERT INTO dbo.comunicaciones_familiares_retiradas (id, comunicacion_id, centro_id, residente_id, motivo, retirada_por_cuenta_id, retirada_en)
            VALUES (NEWID(), @other, @center, NEWID(), N'Motivo', @account, SYSUTCDATETIME())
            """, new { other, center = setup.Admin.CenterId.Value, account = setup.Admin.AccountId.Value }));

        Assert.Contains("FAMILY_COMMUNICATION_CORRECTIVE_IMMUTABLE", updateCorrective.Message);
        Assert.Contains("FAMILY_COMMUNICATION_CORRECTIVE_IMMUTABLE", deleteCorrective.Message);
        Assert.Contains("FAMILY_COMMUNICATION_WITHDRAWAL_IMMUTABLE", updateWithdrawal.Message);
        Assert.Contains("FAMILY_COMMUNICATION_WITHDRAWAL_IMMUTABLE", deleteWithdrawal.Message);
        Assert.Contains("FAMILY_COMMUNICATION_WITHDRAWN", correctAWithdrawn.Message);
        Assert.Contains("FAMILY_COMMUNICATION_CORRECTIVE_RESIDENT_INVALID", foreignCorrective.Message);
        Assert.Contains("FAMILY_COMMUNICATION_WITHDRAWAL_RESIDENT_INVALID", foreignWithdrawal.Message);
    }

    private static async Task<string> TextAsync(Guid communicationId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT texto FROM dbo.comunicaciones_familiares WHERE id = @communicationId", new { communicationId }) ?? string.Empty;
    }
}

file sealed class FixedEnmiendaSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
