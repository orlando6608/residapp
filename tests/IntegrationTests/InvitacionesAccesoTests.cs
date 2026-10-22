using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Platform;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>dbo.invitaciones_acceso (0063, ADR 0007 I2) contra SQL real: emitir anula la anterior, el tipo lo decide la contraseña,
/// se usa una sola vez aunque lleguen dos peticiones a la vez, las reglas de quién emite para quién, la auditoría, las guardas del
/// script y el cierre de todas las sesiones al recuperar.</summary>
public class InvitacionesAccesoTests
{
    private static readonly SqlAccessInvitationRepository Repository = new(TestDatabase.ConnectionFactory);

    private static AccountAdministrationAccess Access(SeededProfile admin) => new(admin.ProfileScopeId, admin.AccountId, admin.CenterId);

    private static DateTime Now => DateTime.UtcNow;

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static Task<int> AuditAsync(AccountId accountId, string action, string profile = "ADMINISTRACION") =>
        ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK)
             WHERE recurso_id = @AccountId AND accion_codigo = @Action AND perfil_activo = @Profile
            """, new { AccountId = accountId.Value, Action = action, Profile = profile });

    private static async Task<(SeededProfile Admin, SeededProfile Nurse)> SeedAsync()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        return (admin, await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId));
    }

    [Fact]
    public async Task Emitir_AnulaLaAnterior_YSoloSeUsaUnaVez_AunqueSeaAlaVez()
    {
        var (admin, nurse) = await SeedAsync();
        var first = AccessInvitation.NewToken();
        var second = AccessInvitation.NewToken();

        var issued = await Repository.IssueAsync(Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(first), Now);
        await Repository.IssueAsync(Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(second), Now);
        var old = await Repository.FindLiveAsync(AccessInvitation.Hash(first), Now);
        var live = await Repository.FindLiveAsync(AccessInvitation.Hash(second), Now);
        var consumed = await Task.WhenAll(Repository.ConsumeAsync(live!.Id, Now), Repository.ConsumeAsync(live.Id, Now));

        Assert.Equal((AccessInvitationKind.Alta, nurse.ExternalSubject), (issued.Kind, issued.Subject));
        Assert.InRange(issued.ExpiresAt - Now, TimeSpan.FromHours(71.9), TimeSpan.FromHours(72));
        Assert.Null(old);
        Assert.Equal(nurse.AccountId, live.AccountId);
        Assert.Single(consumed, c => c);
        Assert.Null(await Repository.FindLiveAsync(AccessInvitation.Hash(second), Now));
        Assert.Equal(2, await AuditAsync(nurse.AccountId, "ACCOUNT_INVITATION_ISSUE"));
        Assert.Equal(64, (await ScalarAsync<string>(
            "SELECT TOP (1) resumen_token FROM dbo.invitaciones_acceso WHERE cuenta_id = @Id", new { Id = nurse.AccountId.Value })).Length);
    }

    [Fact]
    public async Task ConContrasena_EsDeRecuperacion_YCaducaALas72Horas()
    {
        var (admin, nurse) = await SeedAsync();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO dbo.credenciales (cuenta_id, hash_contrasena, actualizada_en) VALUES (@Id, N'hash', SYSUTCDATETIME())",
                new { Id = nurse.AccountId.Value });
        }
        var token = AccessInvitation.NewToken();

        var issued = await Repository.IssueAsync(Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(token), Now);
        var status = await Repository.FindStatusAsync(Access(admin), nurse.ExternalSubject, Now);
        var expired = await Repository.FindLiveAsync(AccessInvitation.Hash(token), Now.AddHours(73));
        var lateUse = await Repository.ConsumeAsync((await Repository.FindLiveAsync(AccessInvitation.Hash(token), Now))!.Id, Now.AddHours(73));

        Assert.Equal(AccessInvitationKind.Recuperacion, issued.Kind);
        Assert.True(status.HasPassword);
        Assert.NotNull(status.LiveExpiresAt);
        Assert.Null(expired);
        Assert.False(lateUse);
        Assert.Equal(1, await AuditAsync(nurse.AccountId, "ACCOUNT_RECOVERY_ISSUE"));
    }

    [Fact]
    public async Task Anular_LaDejaSinValor_YSinInvitacionEsUnConflicto()
    {
        var (admin, nurse) = await SeedAsync();
        var token = AccessInvitation.NewToken();
        await Repository.IssueAsync(Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(token), Now);

        await Repository.CancelAsync(Access(admin), nurse.ExternalSubject, Now);
        var again = await Assert.ThrowsAsync<DomainValidationException>(() => Repository.CancelAsync(Access(admin), nurse.ExternalSubject, Now));

        Assert.Null(await Repository.FindLiveAsync(AccessInvitation.Hash(token), Now));
        Assert.Equal("ACCOUNT_CHANGE_CONFLICT", again.Code);
        Assert.Equal(1, await AuditAsync(nurse.AccountId, "ACCOUNT_INVITATION_CANCEL"));
    }

    [Fact]
    public async Task SoloParaCuentasDelCentro_NoLaPropia_NiConPerfilesEnOtrosCentros_NiSuspendidas()
    {
        var (admin, nurse) = await SeedAsync();
        var foreign = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        var shared = await SeedFixture.CreateProfileAsync(SystemProfile.Enfermeria);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @AccountId, @CenterId, 'AUXILIAR', 'ACTIVE', SYSUTCDATETIME(), @AccountId);
                UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @NurseId;
                """, new { AccountId = shared.AccountId.Value, CenterId = admin.CenterId.Value, NurseId = nurse.AccountId.Value });
        }
        Task Issue(string subject) => Repository.IssueAsync(Access(admin), subject, AccessInvitation.Hash(AccessInvitation.NewToken()), Now);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Issue(foreign.ExternalSubject));
        Assert.Equal("ACCOUNT_SELF_CHANGE", (await Assert.ThrowsAsync<DomainValidationException>(() => Issue(admin.ExternalSubject))).Code);
        Assert.Equal("ACCOUNT_OTHER_CENTERS", (await Assert.ThrowsAsync<DomainValidationException>(() => Issue(shared.ExternalSubject))).Code);
        Assert.Equal("ACCOUNT_CHANGE_CONFLICT", (await Assert.ThrowsAsync<DomainValidationException>(() => Issue(nurse.ExternalSubject))).Code);
        // Quien no es Administración del centro no emite.
        var other = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Repository.IssueAsync(
            new AccountAdministrationAccess(other.ProfileScopeId, other.AccountId, admin.CenterId), nurse.ExternalSubject, "x", Now));
    }

    [Fact]
    public async Task CuentaSuspendidaDespuesDeEmitir_LaInvitacionNoSirve()
    {
        var (admin, nurse) = await SeedAsync();
        var token = AccessInvitation.NewToken();
        await Repository.IssueAsync(Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(token), Now);
        var id = (await Repository.FindLiveAsync(AccessInvitation.Hash(token), Now))!.Id;

        await ScalarAsync<int>("UPDATE dbo.cuentas SET estado = 'SUSPENDED' WHERE id = @Id; SELECT 1", new { Id = nurse.AccountId.Value });

        Assert.Null(await Repository.FindLiveAsync(AccessInvitation.Hash(token), Now));
        Assert.False(await Repository.ConsumeAsync(id, Now));
    }

    [Fact]
    public async Task Plataforma_EmiteParaUnaAdministracionDelCentro_ConSuAuditoria()
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var operatorId = Guid.NewGuid();
        var operatorScope = Guid.NewGuid();
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@Id, @Subject, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@Scope, @Id, @Center, 'PLATAFORMA', 'ACTIVE', SYSUTCDATETIME(), @Id);
                """, new { Id = operatorId, Subject = $"test-plat-{Guid.NewGuid():N}"[..28], Scope = operatorScope, Center = PlatformCenter.Id });
        }
        var access = new PlatformAccess(operatorScope, AccountId.From(operatorId));

        var issued = await Repository.IssueForAdministratorAsync(access, admin.CenterId, admin.ProfileScopeId, AccessInvitation.Hash(AccessInvitation.NewToken()), Now);
        var notAdministration = await SeedFixture.AddProfileToCenterAsync(SystemProfile.Enfermeria, admin.CenterId, admin.UnitId);

        Assert.Equal((AccessInvitationKind.Alta, admin.ExternalSubject), (issued.Kind, issued.Subject));
        Assert.Equal(1, await AuditAsync(admin.AccountId, "ACCOUNT_INVITATION_ISSUE", "PLATAFORMA"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Repository.IssueForAdministratorAsync(
            access, admin.CenterId, notAdministration.ProfileScopeId, AccessInvitation.Hash(AccessInvitation.NewToken()), Now));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Repository.IssueForAdministratorAsync(
            new PlatformAccess(admin.ProfileScopeId, admin.AccountId), admin.CenterId, admin.ProfileScopeId, "x", Now));
    }

    [Fact]
    public async Task LaBase_NoDejaReabrirNiBorrarUnaInvitacion()
    {
        var (admin, nurse) = await SeedAsync();
        var token = AccessInvitation.NewToken();
        await Repository.IssueAsync(Access(admin), nurse.ExternalSubject, AccessInvitation.Hash(token), Now);
        var id = (await Repository.FindLiveAsync(AccessInvitation.Hash(token), Now))!.Id;
        await Repository.ConsumeAsync(id, Now);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();

        var reopen = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.invitaciones_acceso SET usada_en = NULL WHERE id = @id", new { id }));
        var both = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.invitaciones_acceso SET anulada_en = SYSUTCDATETIME() WHERE id = @id", new { id }));
        var delete = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "DELETE FROM dbo.invitaciones_acceso WHERE id = @id", new { id }));

        Assert.Contains("ACCESS_INVITATION_UPDATE_INVALID", reopen.Message);
        Assert.Contains("CK_invitaciones_acceso_cierre", both.Message);
        Assert.Contains("ACCESS_INVITATION_DELETE_FORBIDDEN", delete.Message);
    }

    [Fact]
    public async Task CerrarTodas_CierraLasSesionesAbiertasDeLaCuenta_ConSuMotivo()
    {
        var (_, nurse) = await SeedAsync();
        var store = new SqlSessionStore(TestDatabase.ConnectionFactory);
        var (one, two) = (Guid.NewGuid(), Guid.NewGuid());
        await store.OpenAsync(one, nurse.AccountId, Now, Now.AddHours(12));
        await store.OpenAsync(two, nurse.AccountId, Now, Now.AddHours(12));

        await store.CloseAllAsync(nurse.AccountId, SessionCloseReason.Recuperacion, Now.AddMinutes(1));

        Assert.Equal(2, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.sesiones WHERE cuenta_id = @Id AND motivo_cierre = 'RECUPERACION'", new { Id = nurse.AccountId.Value }));
    }
}
