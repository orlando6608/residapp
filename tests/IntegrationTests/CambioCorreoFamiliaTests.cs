using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Families;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.AdministracionResidentesTests;

namespace ResidApp.IntegrationTests;

/// <summary>Lo que cambia en Administración cuando la familia entra con un código por correo (ADR 0012, pieza 3; script 0122): correo obligatorio para dar acceso e invitar, efectos
/// de cambiarlo o borrarlo y «Última entrada». Condiciones C16, C17 y C22 a C29 de docs/privacidad/codigo-acceso-familiar-por-correo.md.</summary>
public class CambioCorreoFamiliaTests
{
    private const string Old = "anterior.ficticia@example.test";
    private const string Next = "nueva.ficticia@example.test";
    private static readonly EmailSettings Open = new(true, "https://dev.example.test", "es", true, []);

    private sealed class Sender(Func<EmailMessage, EmailSendResult>? result = null) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(result?.Invoke(message) ?? EmailSendResult.Ok("op-cambio"));
        }
    }

    private sealed record Family(SeededProfile Admin, ResidentId ResidentId, Guid LinkId, Guid FamilyId, string Subject, AccountId AccountId);

    private static AdministracionApplicationService Service(SeededProfile admin, bool emailCode, Sender? sender = null, EmailSettings? settings = null)
    {
        var factory = TestDatabase.ConnectionFactory;
        var session = new AdminSession(admin.ExternalSubject);
        return new AdministracionApplicationService(
            new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(factory), session), new SqlProfileScopeDirectoryProvider(factory),
            new SqlAdministracionResidentDirectory(factory), session, new SqlAuthorizationEvidenceProvider(factory),
            new SqlResidentIdentityRepository(factory), new SqlResidentFamilyRepository(factory), new SqlProfessionalAccountDirectory(factory),
            new SqlProfessionalAccountRepository(factory), new FamilyAccessPolicy(emailCode),
            new FamilyEmailChangeNoticeService(new SqlFamilyEmailChangeLog(factory), sender ?? new Sender(), settings ?? Open, TimeProvider.System));
    }

    private static AccessInvitationsApplicationService Invitations(SeededProfile admin, bool emailCode) => new(
        new AdministrationAccessResolver(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new AdminSession(admin.ExternalSubject)),
        new SqlAccessInvitationRepository(TestDatabase.ConnectionFactory), TimeProvider.System, new FamilyAccessPolicy(emailCode));

    private static async Task<(SeededProfile Admin, ResidentId ResidentId, Guid LinkId)> LinkedFamilyAsync(string? email)
    {
        var admin = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);
        var residentId = await CreateResidentAsync(admin, "Residente Cambio Correo");
        var added = await Service(admin, emailCode: false).AddFamilyMemberAsync(new AddFamilyMemberCommand(
            admin.ProfileScopeId, admin.CenterId, residentId, Guid.NewGuid(), "Lucía Pérez", "Hija", "600 123 456", email));
        Assert.True(added.Ok, added.Error?.Message);
        return (admin, residentId, added.Value);
    }

    private static Task<ApplicationResult<bool>> GrantAsync(
        SeededProfile admin, ResidentId residentId, Guid linkId, string subject, bool emailCode) =>
        Service(admin, emailCode).GrantFamilyPortalAccessAsync(new GrantFamilyPortalAccessCommand(admin.ProfileScopeId, admin.CenterId, residentId, linkId, subject));

    private static async Task<Family> FamilyWithAccessAsync(string? email = Old)
    {
        var (admin, residentId, linkId) = await LinkedFamilyAsync(email);
        var subject = $"familia-{Guid.NewGuid():N}";
        var granted = await GrantAsync(admin, residentId, linkId, subject, emailCode: false);
        Assert.True(granted.Ok, granted.Error?.Message);
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var (familyId, accountId) = await connection.QuerySingleAsync<(Guid FamilyId, Guid AccountId)>("""
            SELECT f.id, f.cuenta_id FROM dbo.residentes_familiares link JOIN dbo.familiares f ON f.id = link.familiar_id WHERE link.id = @linkId
            """, new { linkId });
        return new Family(admin, residentId, linkId, familyId, subject, AccountId.From(accountId));
    }

    private static async Task<ResidentFamilyMember> MemberAsync(Family family) =>
        Assert.Single((await Service(family.Admin, emailCode: false).FindResidentAsync(
            new FindAdministrativeResidentQuery(family.Admin.ProfileScopeId, family.Admin.CenterId, family.ResidentId))).Value!.Family);

    private static UpdateFamilyMemberCommand Update(Family family, ResidentFamilyMember before, string? email, string? phone = null) =>
        new(family.Admin.ProfileScopeId, family.Admin.CenterId, family.ResidentId, family.LinkId, before.DisplayName, before.Relationship, phone ?? before.Phone, email,
            new FamilyMemberData(before.DisplayName, before.Relationship, before.Phone, before.Email, before.IsReferent, before.IsLegalGuardian).Version,
            before.IsReferent, before.IsLegalGuardian);

    /// <summary>Una sesión abierta y un código sin usar de la cuenta.</summary>
    private static async Task<(Guid SessionId, Guid CodeId)> OpenAccessAsync(AccountId account)
    {
        var now = DateTime.UtcNow.AddSeconds(-5);
        var sessionId = Guid.NewGuid();
        await new SqlSessionStore(TestDatabase.ConnectionFactory).OpenAsync(sessionId, account, now, now.AddHours(1));
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.credenciales WHERE cuenta_id = @id)
                INSERT INTO dbo.credenciales (cuenta_id, actualizada_en) VALUES (@id, SYSUTCDATETIME())
            """, new { id = account.Value });
        var code = new NewAccessCode(Guid.NewGuid(), account, RandomNumberGenerator.GetBytes(32), now, now.AddMinutes(10), "es", "1");
        Assert.Equal(AccessCodeReservation.Reserved,
            await new SqlFamilyAccessCodeStore(TestDatabase.ConnectionFactory, TestDatabase.ConnectionString).ReserveAsync(code));
        return (sessionId, code.Id);
    }

    private static async Task<T> ScalarAsync<T>(string sql, object parameters)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    private static Task<string?> SessionClosedAsAsync(Guid sessionId) =>
        ScalarAsync<string?>("SELECT motivo_cierre FROM dbo.sesiones WHERE id = @sessionId", new { sessionId });

    private static Task<string?> CodeClosedAsAsync(Guid codeId) =>
        ScalarAsync<string?>("SELECT motivo_cierre FROM dbo.codigos_acceso_correo WHERE id = @codeId", new { codeId });

    private sealed record LogRow(string Template, string State, string? Reason, Guid ReferenceId, int Attempts, DateTime? ResolvedAt);

    private static async Task<IReadOnlyList<LogRow>> LogAsync(Guid familyId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<LogRow>("""
            SELECT plantilla_codigo AS Template, estado_codigo AS State, motivo_codigo AS Reason, referencia_id AS ReferenceId, CAST(intentos AS INT) AS Attempts,
                   resuelto_en AS ResolvedAt
              FROM dbo.envios_correo WITH (NOLOCK) WHERE familiar_id = @familyId
            """, new { familyId })).ToList();
    }

    [Theory]
    [InlineData(Next)]
    [InlineData(null)]
    public async Task CambiarOBorrarElCorreo_CierraSesionesYCodigo_YAvisaSoloALaAnterior(string? next)
    {
        var family = await FamilyWithAccessAsync();
        var other = await FamilyWithAccessAsync();
        var (sessionId, codeId) = await OpenAccessAsync(family.AccountId);
        var (otherSession, otherCode) = await OpenAccessAsync(other.AccountId);
        var sender = new Sender();
        var service = Service(family.Admin, emailCode: true, sender);
        var command = Update(family, await MemberAsync(family), next);

        var result = await service.UpdateFamilyMemberAsync(command);
        var resent = await service.UpdateFamilyMemberAsync(command);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(FamilyEmailNotice.Sent, result.Value);
        // El mismo formulario otra vez es un conflicto: no vuelve a avisar.
        Assert.Equal(ApplicationFailureCode.Conflict, resent.Error!.Code);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(Old, message.To);
        Assert.DoesNotContain(Next, message.PlainText + message.Html);
        Assert.DoesNotContain("Lucía", message.PlainText + message.Html);
        Assert.Equal("CAMBIO_CORREO", await SessionClosedAsAsync(sessionId));
        Assert.Equal("ANULADO", await CodeClosedAsAsync(codeId));
        Assert.Null(await SessionClosedAsAsync(otherSession));
        Assert.Null(await CodeClosedAsAsync(otherCode));
        var row = Assert.Single(await LogAsync(family.FamilyId));
        Assert.Equal(("AVISO_CAMBIO_CORREO", "ENVIADO", null, family.FamilyId, 1), (row.Template, row.State, row.Reason, row.ReferenceId, row.Attempts));
        Assert.NotNull(row.ResolvedAt);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE residente_id = @id AND accion_codigo = 'FAMILY_EMAIL_CHANGE'", new { id = family.ResidentId.Value }));
    }

    [Fact]
    public async Task DeBajaDeLosAvisos_ElDeSeguridadPorCambioDeCorreoSeEnviaIgual()
    {
        // Script 0123: la baja corta los avisos sobre citas, no los correos de seguridad.
        var family = await FamilyWithAccessAsync();
        Assert.True((await Service(family.Admin, emailCode: true).SetFamilyNoticesOptOutAsync(new SetFamilyNoticesOptOutCommand(
            family.Admin.ProfileScopeId, family.Admin.CenterId, family.ResidentId, family.LinkId, true, null))).Ok);
        var sender = new Sender();

        var result = await Service(family.Admin, emailCode: true, sender).UpdateFamilyMemberAsync(Update(family, await MemberAsync(family), Next));

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(FamilyEmailNotice.Sent, result.Value);
        Assert.Equal(Old, Assert.Single(sender.Sent).To);
        Assert.Equal("ENVIADO", Assert.Single(await LogAsync(family.FamilyId)).State);
    }

    [Theory]
    [InlineData("apagado")]
    [InlineData("solo-mayusculas")]
    [InlineData("solo-telefono")]
    [InlineData("perfil-mixto")]
    [InlineData("perfil-revocado")]
    public async Task SinCambioDeAcceso_NiCierraNiAvisa(string reason)
    {
        var family = await FamilyWithAccessAsync();
        var (sessionId, codeId) = await OpenAccessAsync(family.AccountId);
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            if (reason == "perfil-mixto")
            {
                await connection.ExecuteAsync("""
                    INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                    VALUES (NEWID(), @account, @center, 'AUXILIAR', 'ACTIVE', SYSUTCDATETIME(), @admin)
                    """, new { account = family.AccountId.Value, center = family.Admin.CenterId.Value, admin = family.Admin.AccountId.Value });
            }
            else if (reason == "perfil-revocado")
            {
                await connection.ExecuteAsync("""
                    UPDATE dbo.ambitos_perfil SET estado = 'REVOKED', revocado_en = SYSUTCDATETIME(), revocado_por_cuenta_id = @admin
                     WHERE cuenta_id = @account AND perfil_codigo = 'FAMILIAR'
                    """, new { account = family.AccountId.Value, admin = family.Admin.AccountId.Value });
            }
        }
        var sender = new Sender();
        var before = await MemberAsync(family);
        var command = reason switch
        {
            "solo-mayusculas" => Update(family, before, Old.ToUpperInvariant()),
            "solo-telefono" => Update(family, before, Old, "611 000 111"),
            _ => Update(family, before, Next),
        };

        var result = await Service(family.Admin, emailCode: reason != "apagado", sender).UpdateFamilyMemberAsync(command);

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(FamilyEmailNotice.NotNeeded, result.Value);
        Assert.Empty(sender.Sent);
        Assert.Null(await SessionClosedAsAsync(sessionId));
        Assert.Null(await CodeClosedAsAsync(codeId));
        Assert.Empty(await LogAsync(family.FamilyId));
    }

    [Fact]
    public async Task SinCuentaOSinDireccionAnterior_NoHayAQuienAvisar()
    {
        var sender = new Sender();
        var (admin, residentId, linkId) = await LinkedFamilyAsync(Old);
        var withoutAccount = new Family(admin, residentId, linkId, Guid.Empty, "", default!);
        var withoutAddress = await FamilyWithAccessAsync(email: null);
        var (sessionId, _) = await OpenAccessAsync(withoutAddress.AccountId);

        var first = await Service(admin, emailCode: true, sender).UpdateFamilyMemberAsync(Update(withoutAccount, await MemberAsync(withoutAccount), Next));
        var second = await Service(withoutAddress.Admin, emailCode: true, sender)
            .UpdateFamilyMemberAsync(Update(withoutAddress, await MemberAsync(withoutAddress), Next));

        Assert.Equal(FamilyEmailNotice.NotNeeded, first.Value);
        Assert.Equal(FamilyEmailNotice.NotNeeded, second.Value);
        Assert.Empty(sender.Sent);
        Assert.Null(await SessionClosedAsAsync(sessionId));
    }

    [Theory]
    [InlineData("rechazado", "FALLIDO", "ACS_RECHAZADO", 1)]
    [InlineData("excepcion", "FALLIDO", "ACS_NO_DISPONIBLE", 1)]
    [InlineData("correo-apagado", "OMITIDO", "CORREO_APAGADO", 0)]
    [InlineData("destino-no-permitido", "OMITIDO", "DESTINO_NO_PERMITIDO", 0)]
    public async Task SiElAvisoNoSale_ElCambioSeGuardaIgual_YQuedaAnotado(string problem, string state, string reason, int attempts)
    {
        var family = await FamilyWithAccessAsync();
        var (sessionId, _) = await OpenAccessAsync(family.AccountId);
        var sender = new Sender(_ => problem == "excepcion"
            ? throw new InvalidOperationException("fallo de prueba")
            : EmailSendResult.Fail(EmailLog.ProviderRejected));
        var settings = problem switch
        {
            "correo-apagado" => EmailSettings.Off,
            "destino-no-permitido" => Open with { AnyRecipient = false, AllowedRecipients = [Next] },
            _ => Open,
        };

        var result = await Service(family.Admin, emailCode: true, sender, settings).UpdateFamilyMemberAsync(Update(family, await MemberAsync(family), Next));

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(FamilyEmailNotice.NotSent, result.Value);
        Assert.Equal(Next, (await MemberAsync(family)).Email);
        Assert.Equal("CAMBIO_CORREO", await SessionClosedAsAsync(sessionId));
        Assert.Equal(attempts, sender.Sent.Count);
        var row = Assert.Single(await LogAsync(family.FamilyId));
        Assert.Equal((state, reason, attempts), (row.State, row.Reason, row.Attempts));
    }

    [Fact]
    public async Task ElRegistroNoAdmiteUnAvisoDeCambioPendiente()
    {
        var family = await FamilyWithAccessAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() => new SqlFamilyEmailChangeLog(TestDatabase.ConnectionFactory).RecordAsync(new FamilyEmailChangeEntry(
            family.Admin.CenterId, family.FamilyId, "1", "es", "PENDIENTE", null, null, 0, DateTime.UtcNow)));

        Assert.Equal(547, error.Number);
    }

    [Fact]
    public async Task DarAcceso_ConElCodigo_ExigeCorreo_YApagadoNo()
    {
        var (admin, residentId, linkId) = await LinkedFamilyAsync(null);
        var subject = $"familia-{Guid.NewGuid():N}";

        var refused = await GrantAsync(admin, residentId, linkId, subject, emailCode: true);
        var accounts = await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.cuentas WHERE sujeto_externo = @subject", new { subject });
        var granted = await GrantAsync(admin, residentId, linkId, subject, emailCode: false);

        Assert.Equal(ApplicationFailureCode.InvalidInput, refused.Error!.Code);
        Assert.Equal(ApplicationResultRunner.FamilyEmailRequiredMessage, refused.Error.Message);
        Assert.Equal(0, accounts);
        Assert.True(granted.Ok, granted.Error?.Message);
    }

    [Fact]
    public async Task Invitar_ConElCodigo_ExigeCorreoAlFamiliar_SinAnularLaInvitacionViva_YNoAlPersonal()
    {
        var family = await FamilyWithAccessAsync(email: null);
        var admin = family.Admin;
        var staff = $"auxiliar-{Guid.NewGuid():N}";
        using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
        {
            await connection.ExecuteAsync("""
                DECLARE @id UNIQUEIDENTIFIER = NEWID();
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en) VALUES (@id, @staff, 'ACTIVE', SYSUTCDATETIME());
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @id, @center, 'AUXILIAR', 'ACTIVE', SYSUTCDATETIME(), @admin)
                """, new { staff, center = admin.CenterId.Value, admin = admin.AccountId.Value });
        }
        AccountInvitationCommand For(string subject) => new(admin.ProfileScopeId, admin.CenterId, subject);
        Assert.True((await Invitations(admin, emailCode: false).IssueAsync(For(family.Subject))).Ok);

        var refused = await Invitations(admin, emailCode: true).IssueAsync(For(family.Subject));
        var live = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.invitaciones_acceso WHERE cuenta_id = @id AND usada_en IS NULL AND anulada_en IS NULL", new { id = family.AccountId.Value });
        var forStaff = await Invitations(admin, emailCode: true).IssueAsync(For(staff));
        Assert.True((await Service(admin, emailCode: true).UpdateFamilyMemberAsync(Update(family, await MemberAsync(family), Next))).Ok);
        var withEmail = await Invitations(admin, emailCode: true).IssueAsync(For(family.Subject));

        Assert.Equal(ApplicationResultRunner.FamilyEmailRequiredMessage, refused.Error!.Message);
        Assert.Equal(1, live);
        Assert.True(forStaff.Ok, forStaff.Error?.Message);
        Assert.True(withEmail.Ok, withEmail.Error?.Message);
    }

    [Fact]
    public async Task UltimaEntrada_EsLaDeOtraSesionDeLaMismaCuenta()
    {
        var family = await FamilyWithAccessAsync();
        var other = await FamilyWithAccessAsync();
        var (first, _) = await OpenAccessAsync(family.AccountId);
        var second = Guid.NewGuid();
        var now = DateTime.UtcNow.AddSeconds(-5);
        await new SqlSessionStore(TestDatabase.ConnectionFactory).OpenAsync(second, family.AccountId, now, now.AddHours(1));
        var log = new SqlAccessEventLog(TestDatabase.ConnectionFactory);
        var day = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        await log.RecordAsync(AccessEventType.Entrada, family.AccountId, first, "203.0.113.1", day);

        var alone = await log.FindOtherEntryAsync(first);
        await log.RecordAsync(AccessEventType.EntradaPasskey, family.AccountId, second, "203.0.113.2", day.AddDays(1));
        await log.RecordAsync(AccessEventType.ContrasenaFallida, family.AccountId, null, "203.0.113.3", day.AddDays(2));
        var (foreign, _) = await OpenAccessAsync(other.AccountId);
        await log.RecordAsync(AccessEventType.Entrada, other.AccountId, foreign, "203.0.113.4", day.AddDays(3));

        Assert.Null(alone);
        Assert.Equal(day.AddDays(1), await log.FindOtherEntryAsync(first));
        Assert.Equal(day, await log.FindOtherEntryAsync(second));
    }
}

file sealed class AdminSession(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
