using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>dbo.vinculaciones_cuenta (0065, ADR 0007 I2b) contra SQL real: emitir, canjear (una sola vez, con segundo factor y sin ser quien
/// la emitió), confirmar o rechazar, anular, caducar, las guardas del script, y lo que pasa con una cuenta que ya trabaja en dos centros
/// (ninguna Administración la cambia; Plataforma, sí).</summary>
public class VinculacionesCuentaTests
{
    private static readonly SqlAccountLinkRepository Links = new(TestDatabase.ConnectionFactory);
    private static readonly SqlProfessionalAccountRepository Accounts = new(TestDatabase.ConnectionFactory);
    private static readonly SqlAccessInvitationRepository Invitations = new(TestDatabase.ConnectionFactory);
    private static readonly SqlPlatformCenterRepository PlatformCenters = new(TestDatabase.ConnectionFactory);
    private static readonly SqlPlatformCenterDirectory PlatformDirectory = new(TestDatabase.ConnectionFactory);

    private static AccountAdministrationAccess Access(SeededProfile admin) => new(admin.ProfileScopeId, admin.AccountId, admin.CenterId);

    private static DateTime Now => DateTime.UtcNow;

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static Task<int> ExecuteAsync(string sql, object parameters) => ScalarAsync<int>(sql + "; SELECT 1", parameters);

    /// <summary>Una cuenta de Enfermería en su propio centro, con el segundo factor ya activado (la de una persona del personal).</summary>
    private static async Task<SeededProfile> StaffWithTwoFactorAsync()
    {
        var nurse = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        await ExecuteAsync("""
            INSERT INTO dbo.credenciales (cuenta_id, hash_contrasena, sello_seguridad, clave_autenticador, segundo_factor_activo, intentos_fallidos, actualizada_en)
            VALUES (@AccountId, 'hash', 'sello', 'CLAVE', 1, 0, SYSUTCDATETIME())
            """, new { AccountId = nurse.AccountId.Value });
        return nurse;
    }

    private static Task<int> ScopesInCenterAsync(AccountId accountId, CenterId centerId, string profile = "ENFERMERIA") => ScalarAsync<int>("""
        SELECT COUNT(*) FROM dbo.ambitos_perfil WHERE cuenta_id = @AccountId AND centro_id = @CenterId AND perfil_codigo = @Profile AND estado = 'ACTIVE'
        """, new { AccountId = accountId.Value, CenterId = centerId.Value, Profile = profile });

    private static Task<int> AuditAsync(Guid resourceId, string action) => ScalarAsync<int>("""
        SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE recurso_id = @ResourceId AND accion_codigo = @Action
        """, new { ResourceId = resourceId, Action = action });

    /// <summary>Emite una vinculación de Enfermería en la unidad del administrador y devuelve su token y su id.</summary>
    private static async Task<(string Token, Guid Id)> IssueAsync(SeededProfile admin, SystemProfile profile = SystemProfile.Enfermeria)
    {
        var token = AccessInvitation.NewToken();
        await Links.IssueAsync(Access(admin), profile, [admin.UnitId], AccessInvitation.Hash(token), Now);
        return (token, (await Links.FindLiveAsync(AccessInvitation.Hash(token), Now))!.Id);
    }

    [Fact]
    public async Task EmitirCanjearYConfirmar_CreaElPerfilYSusUnidades_SoloAlConfirmar()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await StaffWithTwoFactorAsync();
        var token = AccessInvitation.NewToken();

        var issued = await Links.IssueAsync(Access(admin), SystemProfile.Enfermeria, [admin.UnitId], AccessInvitation.Hash(token), Now);
        var live = await Links.FindLiveAsync(AccessInvitation.Hash(token), Now);
        await Links.RedeemAsync(nurse.AccountId, live!.Id, Now);
        var pending = await Links.ListOpenAsync(Access(admin), Now);
        var beforeConfirming = await ScopesInCenterAsync(nurse.AccountId, admin.CenterId);
        var scopeId = await Links.ResolveAsync(Access(admin), live.Id, confirm: true, Now);

        Assert.Equal(SystemProfile.Enfermeria, issued.Profile);
        Assert.InRange(issued.ExpiresAt - Now, TimeSpan.FromHours(71.9), TimeSpan.FromHours(72));
        Assert.NotEmpty(issued.UnitNames);
        Assert.Equal(SystemProfile.Enfermeria, live.Profile);
        var open = Assert.Single(pending);
        Assert.Equal((live.Id, nurse.ExternalSubject, true), (open.Id, open.RedeemedBySubject, open.IsRedeemed));
        Assert.Equal(0, beforeConfirming);
        Assert.Equal(1, await ScopesInCenterAsync(nurse.AccountId, admin.CenterId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ambitos_perfil_unidad WHERE ambito_perfil_id = @Id AND unidad_id = @UnitId AND revocado_en IS NULL",
            new { Id = scopeId, UnitId = admin.UnitId.Value }));
        Assert.Equal(1, await AuditAsync(live.Id, "ACCOUNT_LINK_ISSUE"));
        Assert.Equal(1, await AuditAsync(nurse.AccountId.Value, "ACCOUNT_LINK_CONFIRM"));
        Assert.Equal(1, await AuditAsync(scopeId!.Value, "PROFILE_SCOPE_GRANT"));
        Assert.Empty(await Links.ListOpenAsync(Access(admin), Now));
        Assert.Null(await Links.FindLiveAsync(AccessInvitation.Hash(token), Now));
    }

    [Fact]
    public async Task Rechazar_NoConcedeNada_YNoSeResuelveDosVeces()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await StaffWithTwoFactorAsync();
        var (_, id) = await IssueAsync(admin);
        await Links.RedeemAsync(nurse.AccountId, id, Now);

        var rejected = await Links.ResolveAsync(Access(admin), id, confirm: false, Now);

        Assert.Null(rejected);
        Assert.Equal(0, await ScopesInCenterAsync(nurse.AccountId, admin.CenterId));
        Assert.Equal(1, await AuditAsync(nurse.AccountId.Value, "ACCOUNT_LINK_REJECT"));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.ResolveAsync(Access(admin), id, confirm: true, Now));
        Assert.Equal(0, await ScopesInCenterAsync(nurse.AccountId, admin.CenterId));
    }

    [Fact]
    public async Task Canjear_SoloUnaVez_AunqueLleguenDosAlaVez()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var first = await StaffWithTwoFactorAsync();
        var second = await StaffWithTwoFactorAsync();
        var (_, id) = await IssueAsync(admin);

        var outcomes = await Task.WhenAll(
            Links.RedeemAsync(first.AccountId, id, Now).ContinueWith(task => task.IsCompletedSuccessfully),
            Links.RedeemAsync(second.AccountId, id, Now).ContinueWith(task => task.IsCompletedSuccessfully));

        Assert.Single(outcomes, ok => ok);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vinculaciones_cuenta WHERE id = @Id AND canjeada_por_cuenta_id IS NOT NULL", new { Id = id }));
    }

    [Fact]
    public async Task Canjear_ExigeSegundoFactor_NoLaPropiaCuentaEmisora_NiTenerYaElPerfil()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await ExecuteAsync("""
            INSERT INTO dbo.credenciales (cuenta_id, hash_contrasena, sello_seguridad, clave_autenticador, segundo_factor_activo, intentos_fallidos, actualizada_en)
            VALUES (@AccountId, 'hash', 'sello', 'CLAVE', 1, 0, SYSUTCDATETIME())
            """, new { AccountId = admin.AccountId.Value });
        var withoutSecondFactor = await SeedFixture.CreateProfileAsync(SystemProfile.Medicina);
        var alreadyThere = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        await ExecuteAsync("""
            INSERT INTO dbo.credenciales (cuenta_id, hash_contrasena, sello_seguridad, clave_autenticador, segundo_factor_activo, intentos_fallidos, actualizada_en)
            VALUES (@AccountId, 'hash', 'sello', 'CLAVE', 1, 0, SYSUTCDATETIME())
            """, new { AccountId = alreadyThere.AccountId.Value });
        var suspended = await StaffWithTwoFactorAsync();
        await ExecuteAsync("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @AccountId", new { AccountId = suspended.AccountId.Value });
        var (_, id) = await IssueAsync(admin);

        var noSecondFactor = await Assert.ThrowsAsync<DomainValidationException>(() => Links.RedeemAsync(withoutSecondFactor.AccountId, id, Now));
        var issuer = await Assert.ThrowsAsync<DomainValidationException>(() => Links.RedeemAsync(admin.AccountId, id, Now));
        var duplicate = await Assert.ThrowsAsync<DomainValidationException>(() => Links.RedeemAsync(alreadyThere.AccountId, id, Now));
        var notActive = await Assert.ThrowsAsync<DomainValidationException>(() => Links.RedeemAsync(suspended.AccountId, id, Now));

        Assert.Equal("ACCOUNT_LINK_SECOND_FACTOR", noSecondFactor.Message);
        Assert.Equal("ACCOUNT_LINK_INVALID", issuer.Message);
        Assert.Equal("PROFILE_SCOPE_DUPLICATE", duplicate.Message);
        Assert.Equal("ACCOUNT_LINK_INVALID", notActive.Message);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vinculaciones_cuenta WHERE id = @Id AND canjeada_en IS NOT NULL", new { Id = id }));
    }

    [Fact]
    public async Task Anular_SoloLoQueNadieHaCanjeado_YSoloEnSuCentro()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await StaffWithTwoFactorAsync();
        var (cancelledToken, cancelledId) = await IssueAsync(admin);
        var (_, redeemedId) = await IssueAsync(admin);
        await Links.RedeemAsync(nurse.AccountId, redeemedId, Now);

        await Assert.ThrowsAsync<DomainValidationException>(() => Links.CancelAsync(Access(other), cancelledId, Now));
        await Links.CancelAsync(Access(admin), cancelledId, Now);

        Assert.Null(await Links.FindLiveAsync(AccessInvitation.Hash(cancelledToken), Now));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.CancelAsync(Access(admin), cancelledId, Now));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.CancelAsync(Access(admin), redeemedId, Now));
        Assert.Equal(1, await AuditAsync(cancelledId, "ACCOUNT_LINK_CANCEL"));
        // Una Administración de otro centro no resuelve (ni ve) la vinculación.
        await Assert.ThrowsAsync<AccessDeniedException>(() => Links.ResolveAsync(Access(other), redeemedId, confirm: true, Now));
        Assert.Empty(await Links.ListOpenAsync(Access(other), Now));
    }

    [Fact]
    public async Task Emitir_SoloPerfilesDePersonal_YUnidadesDelAmbitoDeQuienEmite()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var elsewhere = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var hash = AccessInvitation.Hash(AccessInvitation.NewToken());

        await Assert.ThrowsAsync<DomainValidationException>(() => Links.IssueAsync(Access(admin), SystemProfile.Familiar, [admin.UnitId], hash, Now));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.IssueAsync(Access(admin), SystemProfile.Plataforma, [admin.UnitId], hash, Now));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.IssueAsync(Access(admin), SystemProfile.Enfermeria, [elsewhere.UnitId], hash, Now));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.IssueAsync(Access(admin), SystemProfile.Enfermeria, [], hash, Now));
        var nurse = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Links.IssueAsync(
            new AccountAdministrationAccess(nurse.ProfileScopeId, nurse.AccountId, nurse.CenterId), SystemProfile.Enfermeria, [nurse.UnitId], hash, Now));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vinculaciones_cuenta WHERE centro_id = @CenterId", new { CenterId = admin.CenterId.Value }));
    }

    [Fact]
    public async Task Caducada_NoSeEnseñaNiSeCanjea()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await StaffWithTwoFactorAsync();
        var (token, id) = await IssueAsync(admin);
        var later = Now + AccessInvitation.Validity + TimeSpan.FromMinutes(1);

        Assert.Null(await Links.FindLiveAsync(AccessInvitation.Hash(token), later));
        await Assert.ThrowsAsync<DomainValidationException>(() => Links.RedeemAsync(nurse.AccountId, id, later));
        Assert.Empty(await Links.ListOpenAsync(Access(admin), later));
    }

    [Fact]
    public async Task LaBase_NoDejaCambiarLoEmitidoNiDeshacerNiBorrar()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await StaffWithTwoFactorAsync();
        var (_, id) = await IssueAsync(admin);
        await Links.RedeemAsync(nurse.AccountId, id, Now);

        var changeToken = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            "UPDATE dbo.vinculaciones_cuenta SET resumen_token = 'otro' WHERE id = @Id", new { Id = id }));
        var undoRedeem = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            "UPDATE dbo.vinculaciones_cuenta SET canjeada_en = NULL, canjeada_por_cuenta_id = NULL WHERE id = @Id", new { Id = id }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM dbo.vinculaciones_cuenta WHERE id = @Id", new { Id = id }));
        var deleteUnit = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM dbo.vinculaciones_cuenta_unidades WHERE vinculacion_id = @Id", new { Id = id }));
        var cancelRedeemed = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            "UPDATE dbo.vinculaciones_cuenta SET anulada_en = SYSUTCDATETIME() WHERE id = @Id", new { Id = id }));

        Assert.Contains("ACCOUNT_LINK_UPDATE_INVALID", changeToken.Message);
        Assert.Contains("ACCOUNT_LINK_UPDATE_INVALID", undoRedeem.Message);
        Assert.Contains("ACCOUNT_LINK_DELETE_FORBIDDEN", delete.Message);
        Assert.Contains("ACCOUNT_LINK_DELETE_FORBIDDEN", deleteUnit.Message);
        Assert.Contains("CK_vinculaciones_cuenta_anulada", cancelRedeemed.Message);
    }

    [Fact]
    public async Task CuentaEnDosCentros_NingunaAdministracionLaCambia_PlataformaSi()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var nurse = await StaffWithTwoFactorAsync();
        var (_, id) = await IssueAsync(admin);
        await Links.RedeemAsync(nurse.AccountId, id, Now);
        await Links.ResolveAsync(Access(admin), id, confirm: true, Now);
        var operatorId = Guid.NewGuid();
        var operatorScope = Guid.NewGuid();
        await ExecuteAsync("""
            INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@Id, @Subject, 'ACTIVE', SYSUTCDATETIME());
            INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
            VALUES (@Scope, @Id, @Center, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @Id)
            """, new { Id = operatorId, Subject = $"test-plat-{Guid.NewGuid():N}"[..28], Scope = operatorScope, Center = PlatformCenter.Id });
        var platform = new PlatformAccess(operatorScope, AccountId.From(operatorId));

        // Ninguna Administración: ni suspender, ni cambiar el nombre, ni entregar la invitación (la del centro de la cuenta tampoco).
        var suspend = await Assert.ThrowsAsync<DomainValidationException>(() => Accounts.ChangeStatusAsync(Access(admin), nurse.AccountId, AccountStatus.Suspended));
        var rename = await Assert.ThrowsAsync<DomainValidationException>(() => Accounts.RenameAsync(Access(admin), nurse.AccountId, "Otro nombre"));
        var invitation = await Assert.ThrowsAsync<DomainValidationException>(() => Invitations.IssueAsync(
            Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(AccessInvitation.NewToken()), Now));
        // Plataforma: la ve, le entrega la invitación y la suspende (y la reactiva), y cada centro lo ve en su auditoría.
        var listed = await PlatformDirectory.ListMultiCenterAccountsAsync(platform, Now);
        var sheet = await Invitations.IssueForMultiCenterAsync(platform, nurse.AccountId, AccessInvitation.Hash(AccessInvitation.NewToken()), Now);
        await PlatformCenters.ChangeMultiCenterAccountStatusAsync(platform, nurse.AccountId, AccountStatus.Suspended);
        var again = await Assert.ThrowsAsync<DomainValidationException>(() => PlatformCenters.ChangeMultiCenterAccountStatusAsync(platform, nurse.AccountId, AccountStatus.Suspended));
        var suspendedState = await ScalarAsync<string>("SELECT estado FROM dbo.cuentas WHERE id = @Id", new { Id = nurse.AccountId.Value });
        await PlatformCenters.ChangeMultiCenterAccountStatusAsync(platform, nurse.AccountId, AccountStatus.Active);
        var single = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);

        Assert.Equal("ACCOUNT_OTHER_CENTERS", suspend.Message);
        Assert.Equal("ACCOUNT_OTHER_CENTERS", rename.Message);
        Assert.Equal("ACCOUNT_OTHER_CENTERS", invitation.Message);
        var account = Assert.Single(listed, a => a.AccountId == nurse.AccountId);
        Assert.Equal(2, account.Scopes.Count);
        Assert.Equal(AccessInvitationKind.Recuperacion, sheet.Kind);
        Assert.Equal("ACCOUNT_CHANGE_CONFLICT", again.Message);
        Assert.Equal("SUSPENDED", suspendedState);
        Assert.Equal("ACTIVE", await ScalarAsync<string>("SELECT estado FROM dbo.cuentas WHERE id = @Id", new { Id = nurse.AccountId.Value }));
        Assert.Equal(2, await ScalarAsync<int>("""
            SELECT COUNT(DISTINCT centro_id) FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE recurso_id = @Id AND accion_codigo = 'ACCOUNT_SUSPEND' AND perfil_activo = 'PLATAFORMA'
            """, new { Id = nurse.AccountId.Value }));
        Assert.DoesNotContain(await PlatformDirectory.ListMultiCenterAccountsAsync(platform, Now), a => a.AccountId == single.AccountId);
        await Assert.ThrowsAsync<DomainValidationException>(() => PlatformCenters.ChangeMultiCenterAccountStatusAsync(platform, single.AccountId, AccountStatus.Suspended));
        await Assert.ThrowsAsync<DomainValidationException>(() => Invitations.IssueForMultiCenterAsync(
            platform, single.AccountId, AccessInvitation.Hash(AccessInvitation.NewToken()), Now));
        await Assert.ThrowsAsync<AccessDeniedException>(() => PlatformCenters.ChangeMultiCenterAccountStatusAsync(
            new PlatformAccess(admin.ProfileScopeId, admin.AccountId), nurse.AccountId, AccountStatus.Suspended));
    }
}
