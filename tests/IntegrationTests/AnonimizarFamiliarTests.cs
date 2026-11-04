using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Families;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Anonimizar a un familiar que pide que se borren sus datos (script 0087; Orlando, 2026-11-03; docs/privacidad/expurgo-y-supresion.md): sobrescribe nombre, teléfono y
/// correo, revoca sus autorizaciones y cierra su acceso al portal, solo con la Administración principal y sin tocar la cuenta si tiene perfiles en otros centros.</summary>
public class AnonimizarFamiliarTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private sealed record Setup(SeededProfile Admin, ResidentId ResidentId, Guid LinkId, string Subject);

    private static async Task MakePrincipalAsync(SeededProfile admin)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO dbo.administraciones_principales_cambios (id, centro_id, ambito_perfil_id, numero, principal, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, @scope, 1, 1, @account, 'ADMINISTRACION', SYSUTCDATETIME())
            """, new { center = admin.CenterId.Value, scope = admin.ProfileScopeId, account = admin.AccountId.Value });
    }

    /// <summary>Un residente con una familiar con correo confirmado, cuenta de portal y autorización activa. Por defecto, quien actúa es la Administración principal.</summary>
    private static async Task<Setup> SetupAsync(bool principal = true, bool tutorLegal = false)
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        if (principal)
        {
            await MakePrincipalAsync(admin);
        }

        var residentId = await CreateResidentAsync(admin, "Residente Anonimizar");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Lucía Pérez", "Hija", "600 123 456", "lucia@example.test",
            Referente: true, TutorLegal: tutorLegal, ConfirmarCorreo: true))).Value;
        var subject = $"portal-{Guid.NewGuid():N}"[..20];
        Assert.True((await service.GrantFamilyPortalAccessAsync(
            new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, subject))).Ok);
        Assert.True((await service.ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, linkId, FamilyAuthorizationChange.Abrir, null, null, 0))).Ok);
        Assert.True((await service.ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, linkId, FamilyAuthorizationChange.Activar, null, null, 1))).Ok);
        return new Setup(admin, residentId, linkId, subject);
    }

    private static async Task<AdministrativeResidentDetail> DetailAsync(Setup setup) =>
        (await Build(setup.Admin.ExternalSubject).FindResidentAsync(
            new FindAdministrativeResidentQuery(setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId))).Value!;

    private static async Task<FamilyAnonymizationPreview> PreviewAsync(Setup setup, SeededProfile? actor = null)
    {
        var who = actor ?? setup.Admin;
        return (await Build(who.ExternalSubject).PreviewFamilyAnonymizationAsync(
            new PreviewFamilyAnonymizationQuery(who.ProfileScopeId, who.CenterId, setup.ResidentId, setup.LinkId))).Value!;
    }

    private static async Task<ApplicationResult<bool>> AnonymizeAsync(
        Setup setup, string? version = null, FamilyAnonymizationReason? reason = FamilyAnonymizationReason.PeticionDelInteresado, bool confirmed = true,
        SeededProfile? actor = null)
    {
        var who = actor ?? setup.Admin;
        return await Build(who.ExternalSubject).AnonymizeFamilyMemberAsync(new AnonymizeFamilyMemberCommand(
            who.ProfileScopeId, who.CenterId, setup.ResidentId, setup.LinkId, reason, confirmed, version ?? (await PreviewAsync(setup, actor)).Version));
    }

    private static async Task<T> QueryAsync<T>(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static async Task ExecuteAsync(string sql, object? parameters = null)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    [Fact]
    public async Task Anonimiza_BorraNombreTelefonoYCorreo_RevocaElAcceso_SuspendeLaCuenta_YDejaUnaAuditoriaSinDatos()
    {
        var setup = await SetupAsync();
        var accountId = await QueryAsync<Guid>("SELECT account.id FROM dbo.cuentas account WHERE account.sujeto_externo = @Subject", new { setup.Subject });
        // Credenciales y una passkey de la cuenta, y una invitación sin usar: todo debe desaparecer o quedar anulado.
        await ExecuteAsync("INSERT INTO dbo.credenciales (cuenta_id, hash_contrasena, actualizada_en) VALUES (@Id, 'hash-de-prueba', SYSUTCDATETIME())", new { Id = accountId });
        await ExecuteAsync("""
            INSERT INTO dbo.passkeys (credencial_id, cuenta_id, clave_publica, nombre, creada_en, contador_firmas, verificada, copia_posible, copiada)
            VALUES (@Credential, @Id, 0x01, N'Llave de prueba', SYSUTCDATETIME(), 0, 1, 0, 0)
            """, new { Credential = Guid.NewGuid().ToByteArray(), Id = accountId });

        var result = await AnonymizeAsync(setup);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(FamilyAnonymization.Name, await QueryAsync<string>("SELECT nombre_visible FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        Assert.Equal(FamilyAnonymization.Phone, await QueryAsync<string>("SELECT telefono FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        Assert.Null(await QueryAsync<string?>("SELECT correo FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        Assert.Null(await QueryAsync<DateTime?>("SELECT correo_confirmado_en FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        Assert.Equal("PETICION_DEL_INTERESADO", await QueryAsync<string>("SELECT anonimizado_motivo_codigo FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        Assert.Equal(setup.Admin.AccountId.Value, await QueryAsync<Guid>("SELECT anonimizado_por_cuenta_id FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));

        // El vínculo se conserva y la autorización queda revocada con un texto fijo, no con lo que escriba nadie.
        var member = Assert.Single((await DetailAsync(setup)).Family);
        Assert.True(member.IsAnonymized);
        Assert.Equal("Hija", member.Relationship);
        Assert.Equal(FamilyAuthorizationStatus.Revocada, member.CurrentAuthorization!.Status);
        Assert.Equal(FamilyAnonymization.AuthorizationRevocationReason, member.CurrentAuthorization.Reason);

        // La cuenta del portal: suspendida y sin nombre, sin credenciales ni llaves, y el perfil de familiar de este centro revocado.
        Assert.Equal("SUSPENDED", await QueryAsync<string>("SELECT estado FROM dbo.cuentas WHERE id = @Id", new { Id = accountId }));
        Assert.Null(await QueryAsync<string?>("SELECT nombre_visible FROM dbo.cuentas WHERE id = @Id", new { Id = accountId }));
        Assert.Equal(0, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.credenciales WHERE cuenta_id = @Id", new { Id = accountId }));
        Assert.Equal(0, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.passkeys WHERE cuenta_id = @Id", new { Id = accountId }));
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @Id AND perfil_codigo = 'FAMILIAR' AND estado = 'ACTIVE'", new { Id = accountId }));

        // Una sola fila de auditoría, sin residente ni unidad y sin ningún dato de la persona.
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE centro_id = @C AND accion_codigo = 'FAMILY_MEMBER_ANONYMIZE' AND residente_id IS NULL AND unidad_id IS NULL",
            new { C = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task SinSerPrincipal_ConMotivoOConfirmacionQueFaltan_OConUnaVersionVieja_NoSeAnonimizaNada()
    {
        var setup = await SetupAsync();
        var notPrincipal = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Administracion, setup.Admin.CenterId, setup.Admin.UnitId);

        var byNonPrincipal = await AnonymizeAsync(setup, actor: notPrincipal);
        var withoutReason = await AnonymizeAsync(setup, reason: null);
        var withoutConfirmation = await AnonymizeAsync(setup, confirmed: false);
        var staleVersion = await AnonymizeAsync(setup, version: "VERSION-VIEJA");

        // Otra Administración ve la explicación, no puede ejecutar nada (y la que abre la pantalla sin ser principal lo sabe).
        Assert.False((await PreviewAsync(setup, notPrincipal)).IsPrincipal);
        Assert.True((await PreviewAsync(setup)).IsPrincipal);
        Assert.Equal(ApplicationFailureCode.AccessDenied, byNonPrincipal.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutReason.Error!.Code);
        Assert.Equal(ApplicationFailureCode.InvalidInput, withoutConfirmation.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, staleVersion.Error!.Code);
        Assert.Equal("Lucía Pérez", await QueryAsync<string>("SELECT nombre_visible FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE centro_id = @C AND accion_codigo = 'FAMILY_MEMBER_ANONYMIZE'", new { C = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task ElContactoUrgenteOElTutorLegal_NoSeAnonimizan_HastaCambiarlos()
    {
        var setup = await SetupAsync(tutorLegal: true);
        var service = Build(setup.Admin.ExternalSubject);
        Assert.True((await service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, [setup.LinkId], 0))).Ok);

        var preview = await PreviewAsync(setup);
        var blocked = await AnonymizeAsync(setup);

        Assert.True(preview.IsBlocked);
        Assert.Equal(["Residente Anonimizar"], preview.UrgentContactOf);
        Assert.Equal(["Residente Anonimizar"], preview.LegalGuardianOf);
        Assert.Equal(ApplicationFailureCode.Conflict, blocked.Error!.Code);
        Assert.Equal("Lucía Pérez", await QueryAsync<string>("SELECT nombre_visible FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));

        // Quitado como contacto urgente y como tutor legal, se puede.
        Assert.True((await service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, [], 1, "Sin familia conocida."))).Ok);
        var member = Assert.Single((await DetailAsync(setup)).Family);
        Assert.True((await service.UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, member.DisplayName, member.Relationship, member.Phone, member.Email,
            new FamilyMemberData(member.DisplayName, member.Relationship, member.Phone, member.Email, member.IsReferent, member.IsLegalGuardian).Version,
            Referente: member.IsReferent, TutorLegal: false))).Ok);
        Assert.False((await PreviewAsync(setup)).IsBlocked);
        Assert.True((await AnonymizeAsync(setup)).Ok);
    }

    [Fact]
    public async Task UnFamiliarAnonimizado_NoSeEdita_NoSeAnonimizaDeNuevo_NoSeVinculaNiRecibeAccesoNiAutorizacion()
    {
        var setup = await SetupAsync();
        var otherResident = await CreateResidentAsync(setup.Admin, "Otro Residente Anonimizar");
        var service = Build(setup.Admin.ExternalSubject);
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = setup.LinkId });
        Assert.True((await AnonymizeAsync(setup)).Ok);

        var realVersion = (await PreviewAsync(setup)).Version;
        var again = await AnonymizeAsync(setup, version: realVersion);
        var edit = await service.UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, "Otro Nombre", "Hija", "611 222 333", null, realVersion));
        var removeConfirmation = await service.RemoveFamilyEmailConfirmationAsync(new RemoveFamilyEmailConfirmationCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, realVersion));
        var link = await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident, Guid.NewGuid(), familyId, "Hija"));
        var access = await service.GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, $"portal-{Guid.NewGuid():N}"[..20]));
        var authorization = await service.ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Abrir, null, null, 3));
        var emergency = await service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, [setup.LinkId], 0));

        Assert.Equal(ApplicationFailureCode.Conflict, again.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, edit.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, removeConfirmation.Error!.Code);
        Assert.Contains("ya está anonimizado", again.Error.Message);
        Assert.Contains("ya está anonimizado", edit.Error.Message);
        Assert.False(link.Ok);
        Assert.Equal(ApplicationFailureCode.Conflict, access.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, authorization.Error!.Code);
        Assert.Equal(ApplicationFailureCode.Conflict, emergency.Error!.Code);
        // Tampoco aparece entre los familiares que se pueden vincular a otro residente.
        var linkable = await service.ListLinkableFamilyAsync(new LinkableFamilyQuery(setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident, "Persona"));
        Assert.Empty(linkable.Value!);
    }

    [Fact]
    public async Task ElFamiliarVinculadoAVariosResidentes_SeAnonimizaEnTodos_SinTocarAOtrosFamiliares()
    {
        var setup = await SetupAsync();
        var otherResident = await CreateResidentAsync(setup.Admin, "Segundo Residente Anonimizar");
        var service = Build(setup.Admin.ExternalSubject);
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = setup.LinkId });
        var secondLink = (await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident, Guid.NewGuid(), familyId, "Hija"))).Value;
        Assert.True((await service.ChangeFamilyAuthorizationAsync(new ChangeFamilyAuthorizationCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident, secondLink, FamilyAuthorizationChange.Abrir, null, null, 0))).Ok);
        var bystander = (await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident, Guid.NewGuid(), "Marta Gómez", "Nieta", "611 000 111", null))).Value;

        var preview = await PreviewAsync(setup);
        Assert.Equal(2, preview.ResidentCount);
        Assert.True((await AnonymizeAsync(setup)).Ok);

        var second = (await service.FindResidentAsync(new FindAdministrativeResidentQuery(setup.Admin.ProfileScopeId, setup.Admin.CenterId, otherResident))).Value!;
        Assert.True(second.Family.Single(f => f.LinkId == secondLink).IsAnonymized);
        Assert.Equal(FamilyAuthorizationStatus.Revocada, second.Family.Single(f => f.LinkId == secondLink).CurrentAuthorization!.Status);
        var other = second.Family.Single(f => f.LinkId == bystander);
        Assert.False(other.IsAnonymized);
        Assert.Equal("Marta Gómez", other.DisplayName);
    }

    [Fact]
    public async Task SiLaCuentaTienePerfilesEnOtroCentro_SoloSeQuitaElAccesoDeEsteCentro_SinTocarLaCuenta()
    {
        var setup = await SetupAsync();
        var accountId = await QueryAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @Subject", new { setup.Subject });
        await ExecuteAsync("UPDATE dbo.cuentas SET nombre_visible = N'Lucía en dos centros' WHERE id = @Id", new { Id = accountId });
        await ExecuteAsync("INSERT INTO dbo.credenciales (cuenta_id, hash_contrasena, actualizada_en) VALUES (@Id, 'hash-de-prueba', SYSUTCDATETIME())", new { Id = accountId });
        // Un perfil activo de la misma cuenta en otro centro.
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await ExecuteAsync("""
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (NEWID(), @Account, @Center, 'FAMILIAR', 'ACTIVE', SYSUTCDATETIME(), @Granter)
            """, new { Account = accountId, Center = otherCenter.CenterId.Value, Granter = otherCenter.AccountId.Value });

        var preview = await PreviewAsync(setup);
        var result = await AnonymizeAsync(setup);

        Assert.True(preview.AccountInOtherCenters);
        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("ACTIVE", await QueryAsync<string>("SELECT estado FROM dbo.cuentas WHERE id = @Id", new { Id = accountId }));
        Assert.Equal("Lucía en dos centros", await QueryAsync<string>("SELECT nombre_visible FROM dbo.cuentas WHERE id = @Id", new { Id = accountId }));
        Assert.Equal(1, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.credenciales WHERE cuenta_id = @Id", new { Id = accountId }));
        Assert.Equal(0, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @Id AND centro_id = @Center AND perfil_codigo = 'FAMILIAR' AND estado = 'ACTIVE'",
            new { Id = accountId, Center = setup.Admin.CenterId.Value }));
        Assert.Equal(1, await QueryAsync<int>(
            "SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @Id AND centro_id = @Center AND perfil_codigo = 'FAMILIAR' AND estado = 'ACTIVE'",
            new { Id = accountId, Center = otherCenter.CenterId.Value }));
    }

    [Fact]
    public async Task LaBaseDeDatos_ExigeElEstadoAnonimizado_YNoDejaCambiarloDespues()
    {
        var setup = await SetupAsync();
        Assert.True((await AnonymizeAsync(setup)).Ok);
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = setup.LinkId });

        async Task<string> FailsAsync(string sql)
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(sql, new { Id = familyId }));
            return error.Message;
        }

        // Quitarle la marca, ponerle otro nombre o un correo, o darle una cuenta que no tenía: nada de eso lo admite la base.
        Assert.Contains("FAMILY_MEMBER_ANONYMIZED_IMMUTABLE", await FailsAsync("UPDATE dbo.familiares SET anonimizado_en = NULL, anonimizado_por_cuenta_id = NULL, anonimizado_motivo_codigo = NULL WHERE id = @Id"));
        Assert.Contains("CK_fam_anonimizado", await FailsAsync("UPDATE dbo.familiares SET nombre_visible = N'Lucía Pérez' WHERE id = @Id"));
        Assert.Contains("CK_fam_anonimizado", await FailsAsync("UPDATE dbo.familiares SET correo = N'lucia@example.test' WHERE id = @Id"));
        Assert.Contains("FAMILY_MEMBER_ANONYMIZED_IMMUTABLE", await FailsAsync("UPDATE dbo.familiares SET anonimizado_motivo_codigo = 'INICIATIVA_DEL_CENTRO' WHERE id = @Id"));
        Assert.Contains("FAMILY_MEMBER_DELETE_FORBIDDEN", await FailsAsync("DELETE FROM dbo.familiares WHERE id = @Id"));
    }

    [Theory]
    [InlineData(FamilyAnonymizationReason.PeticionDelInteresado, "PETICION_DEL_INTERESADO")]
    [InlineData(FamilyAnonymizationReason.PeticionDeRepresentante, "PETICION_DE_REPRESENTANTE")]
    [InlineData(FamilyAnonymizationReason.PeticionDeAllegadosOHerederos, "PETICION_DE_ALLEGADOS_O_HEREDEROS")]
    [InlineData(FamilyAnonymizationReason.IniciativaDelCentro, "INICIATIVA_DEL_CENTRO")]
    public async Task LosCuatroMotivosDeLaListaCerrada_SeGuardanConSuCodigo_YLaBaseNoAdmiteOtro(FamilyAnonymizationReason reason, string code)
    {
        var setup = await SetupAsync();

        var result = await AnonymizeAsync(setup, reason: reason);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(code, await QueryAsync<string>("SELECT anonimizado_motivo_codigo FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = setup.LinkId });
        var other = await SetupAsync();
        var otherFamilyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = other.LinkId });
        // Un motivo que no está en la lista, aunque el resto del estado sea el anonimizado, no lo admite la base de datos.
        var invalid = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("""
            UPDATE dbo.familiares
               SET nombre_visible = N'Persona anonimizada', telefono = N'-', correo = NULL, correo_confirmado_en = NULL, correo_confirmado_por_cuenta_id = NULL,
                   anonimizado_en = SYSUTCDATETIME(), anonimizado_por_cuenta_id = @Account, anonimizado_motivo_codigo = 'OTRO_MOTIVO'
             WHERE id = @Id
            """, new { Id = otherFamilyId, Account = other.Admin.AccountId.Value }));
        Assert.Contains("CK_fam_anonimizado", invalid.Message);
        Assert.NotEqual(familyId, otherFamilyId);
    }

    [Fact]
    public async Task UnFamiliarSinCuenta_Anonimizado_NoRecibeAccesoAlPortal_NiLaBaseDeDatosLeDejaTenerUnaCuenta()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await MakePrincipalAsync(admin);
        var residentId = await CreateResidentAsync(admin, "Residente Sin Cuenta Anonimizar");
        var service = Build(admin.ExternalSubject);
        var linkId = (await service.AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Marta Sin Cuenta", "Nieta", "611 555 666", null))).Value;
        var setup = new Setup(admin, residentId, linkId, string.Empty);
        Assert.True((await AnonymizeAsync(setup)).Ok);

        var access = await service.GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, linkId, $"portal-{Guid.NewGuid():N}"[..20]));
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = linkId });
        var accountId = await QueryAsync<Guid>("SELECT account.id FROM dbo.cuentas account WHERE account.id = @Granter", new { Granter = admin.AccountId.Value });
        var direct = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE dbo.familiares SET cuenta_id = @Account WHERE id = @Id", new { Id = familyId, Account = accountId }));

        Assert.Equal(ApplicationFailureCode.Conflict, access.Error!.Code);
        Assert.Contains("FAMILY_MEMBER_ANONYMIZED_IMMUTABLE", direct.Message);
        Assert.Null(await QueryAsync<Guid?>("SELECT cuenta_id FROM dbo.familiares WHERE id = @Id", new { Id = familyId }));
    }

    [Fact]
    public async Task Anonimizar_AnulaLaInvitacionViva_ConfirmaQueSeQuitaElCorreoYLaConfirmacion_YLaCuentaNoSeReactivaNiSeRenombra()
    {
        var setup = await SetupAsync();
        var accountId = await QueryAsync<Guid>("SELECT id FROM dbo.cuentas WHERE sujeto_externo = @Subject", new { setup.Subject });
        await ExecuteAsync("""
            INSERT INTO dbo.invitaciones_acceso (id, cuenta_id, centro_id, tipo, resumen_token, emitida_en, emitida_por_cuenta_id, perfil_emisor, caduca_en)
            VALUES (NEWID(), @Account, @Center, 'RECUPERACION', CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONVERT(VARCHAR(36), NEWID())), 2), SYSUTCDATETIME(), @Granter, 'ADMINISTRACION', DATEADD(HOUR, 72, SYSUTCDATETIME()))
            """, new { Account = accountId, Center = setup.Admin.CenterId.Value, Granter = setup.Admin.AccountId.Value });
        Assert.Equal(1, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.invitaciones_acceso WHERE cuenta_id = @Id AND anulada_en IS NULL AND usada_en IS NULL", new { Id = accountId }));

        Assert.True((await AnonymizeAsync(setup)).Ok);

        Assert.Equal(0, await QueryAsync<int>("SELECT COUNT(*) FROM dbo.invitaciones_acceso WHERE cuenta_id = @Id AND anulada_en IS NULL AND usada_en IS NULL", new { Id = accountId }));
        Assert.Null(await QueryAsync<Guid?>("SELECT correo_confirmado_por_cuenta_id FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
        var service = Build(setup.Admin.ExternalSubject);
        var reactivate = await service.ChangeAccountStatusAsync(new ChangeAccountStatusCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, AccountId.From(accountId), AccountStatus.Active));
        var rename = await service.RenameAccountAsync(new RenameProfessionalAccountCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, AccountId.From(accountId), "Lucía Otra Vez"));
        Assert.False(reactivate.Ok);
        Assert.False(rename.Ok);
        Assert.Equal("SUSPENDED", await QueryAsync<string>("SELECT estado FROM dbo.cuentas WHERE id = @Id", new { Id = accountId }));
        Assert.Null(await QueryAsync<string?>("SELECT nombre_visible FROM dbo.cuentas WHERE id = @Id", new { Id = accountId }));
    }

    [Fact]
    public async Task ElContactoUrgenteOElTutorDeUnResidenteDeBaja_NoImpideAnonimizar_PeroElDeUnoActivoSi()
    {
        var setup = await SetupAsync(tutorLegal: true);
        var service = Build(setup.Admin.ExternalSubject);
        var secondResident = await CreateResidentAsync(setup.Admin, "Residente De Baja Anonimizar");
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = setup.LinkId });
        var secondLink = (await service.LinkFamilyMemberAsync(new LinkFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, secondResident, Guid.NewGuid(), familyId, "Hija"))).Value;
        Assert.True((await service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, secondResident, [secondLink], 0))).Ok);
        // El primer residente: tutor legal de la familiar (SetupAsync). Se le quita; el segundo (de baja) tiene a la familiar como único contacto urgente.
        var member = Assert.Single((await DetailAsync(setup)).Family);
        Assert.True((await service.UpdateFamilyMemberAsync(new UpdateFamilyMemberCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, member.DisplayName, member.Relationship, member.Phone, member.Email,
            new FamilyMemberData(member.DisplayName, member.Relationship, member.Phone, member.Email, member.IsReferent, member.IsLegalGuardian).Version,
            Referente: member.IsReferent, TutorLegal: false))).Ok);
        Assert.Contains("Residente De Baja Anonimizar", (await PreviewAsync(setup)).UrgentContactOf);
        var discharge = await EstadoResidenteTests.Build(setup.Admin.ExternalSubject).DischargeAsync(new DischargeResidentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, secondResident, Guid.NewGuid(), ResidApp.Domain.Residents.ResidentDischargeReason.Fallecimiento, null));
        Assert.True(discharge.Ok, discharge.Error?.Message);

        var preview = await PreviewAsync(setup);
        var result = await AnonymizeAsync(setup);

        Assert.False(preview.IsBlocked);
        Assert.Equal(2, preview.ResidentCount);
        Assert.True(result.Ok, result.Error?.Message);
    }

    [Fact]
    public async Task DesignarContactoUrgente_YAnonimizarAlMismoTiempo_NuncaDejanAUnAnonimizadoComoContactoUrgente()
    {
        for (var round = 0; round < 4; round++)
        {
            var setup = await SetupAsync();
            var service = Build(setup.Admin.ExternalSubject);

            var designate = service.DesignateEmergencyContactAsync(new DesignateEmergencyContactCommand(
                setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, [setup.LinkId], 0));
            var anonymize = AnonymizeAsync(setup);
            await Task.WhenAll(designate, anonymize);

            // Si ganó la anonimización, la designación falla; si ganó la designación, la anonimización se rechaza por ser contacto urgente. Nunca las dos.
            Assert.False((await designate).Ok && (await anonymize).Ok, $"Ronda {round}: las dos tuvieron éxito");
            var anonymized = await QueryAsync<int>("SELECT COUNT(*) FROM dbo.familiares WHERE centro_id = @C AND anonimizado_en IS NOT NULL", new { C = setup.Admin.CenterId.Value });
            var urgent = (await DetailAsync(setup)).CurrentEmergencyContacts.Contains(setup.LinkId);
            Assert.False(anonymized == 1 && urgent, $"Ronda {round}: un anonimizado quedó como contacto urgente");
        }
    }

    [Fact]
    public async Task Anonimizar_ConUnPerfilQueNoEsAdministracion_SeDeniega()
    {
        var setup = await SetupAsync();
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, setup.Admin.CenterId, setup.Admin.UnitId);

        var result = await Build(nurse.ExternalSubject).AnonymizeFamilyMemberAsync(new AnonymizeFamilyMemberCommand(
            nurse.ProfileScopeId, nurse.CenterId, setup.ResidentId, setup.LinkId, FamilyAnonymizationReason.PeticionDelInteresado, true, (await PreviewAsync(setup)).Version));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Equal("Lucía Pérez", await QueryAsync<string>("SELECT nombre_visible FROM dbo.familiares WHERE centro_id = @C", new { C = setup.Admin.CenterId.Value }));
    }

    [Fact]
    public async Task LaBaseDeDatos_NoAdmiteMarcarComoAnonimizadoAUnFamiliarConSusDatosPuestos()
    {
        var setup = await SetupAsync();
        var familyId = await QueryAsync<Guid>("SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @Link", new { Link = setup.LinkId });

        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("""
            UPDATE dbo.familiares
               SET anonimizado_en = SYSUTCDATETIME(), anonimizado_por_cuenta_id = @Account, anonimizado_motivo_codigo = 'PETICION_DEL_INTERESADO'
             WHERE id = @Id
            """, new { Id = familyId, Account = setup.Admin.AccountId.Value }));

        Assert.Contains("CK_fam_anonimizado", error.Message);
    }
}
