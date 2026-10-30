using Dapper;
using Microsoft.Data.SqlClient;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.Sessions;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Authorization;
using ResidApp.Infrastructure.Persistence;
using ResidApp.IntegrationTests.TestSupport;
using ResidApp.Shared;
using static ResidApp.IntegrationTests.CitasSolicitudesTests;
using static ResidApp.IntegrationTests.PortalFamiliarTests;

namespace ResidApp.IntegrationTests;

/// <summary>El aviso genérico por correo cuando el centro propone una cita (ADR 0012, pieza 1; script 0081). Nunca llama a ACS: el envío es un falso. Cada prueba crea
/// su propio centro. Las condiciones de lo que se comprueba están en docs/privacidad/aviso-cita-por-correo.md, apartado 3.</summary>
public class AvisoPropuestaCitaTests
{
    private const string FamilyAddress = "familiar.ficticio@example.test";
    private const string BaseUrl = "https://dev.example.test";

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Func<EmailMessage, CancellationToken, Task<EmailSendResult>>? Behavior { get; set; }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            lock (Sent)
            {
                Sent.Add(message);
            }

            return Behavior is null ? EmailSendResult.Ok("op-" + Guid.NewGuid().ToString("N")) : await Behavior(message, ct);
        }
    }

    private sealed record EmailRow(string Estado, string? Motivo, string? Operacion, int Intentos, string Clave, string Idioma, string Version);

    private static EmailSettings Active(bool anyRecipient = true, params string[] allowed) => new(true, BaseUrl, "es", anyRecipient, allowed);

    private static ProposalNoticeService Notice(FakeEmailSender sender, EmailSettings settings, TimeSpan? timeout = null) =>
        new(new SqlProposalNoticeStore(TestDatabase.ConnectionFactory), sender, settings, null, timeout);

    private static AppointmentRequestsApplicationService AdminRequests(string subject, ProposalNoticeService notice) => new(
        new AdministrationAccessResolver(new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedAvisoSessionIdentityProvider(subject)),
        new SqlAppointmentRequestRepository(TestDatabase.ConnectionFactory), notice);

    private static Task<ApplicationResult<bool>> ProposeAsync(Setup setup, Guid requestId, AppointmentSlot slot, ProposalNoticeService notice) =>
        AdminRequests(setup.Admin.ExternalSubject, notice).ProposeAsync(new ProposeAppointmentCommand(
            setup.Admin.ProfileScopeId, setup.Admin.CenterId, requestId, slot.StartsAt, null));

    /// <summary>Un centro con el servicio configurado, el familiar con correo y una solicitud pendiente de su residente.</summary>
    private static async Task<(Setup Setup, FamilyAppointmentRequest Request)> WithRequestAsync(string name, string? address = FamilyAddress)
    {
        var setup = await SetupAsync(name);
        await ConfigureAsync(setup);
        await SetAddressAsync(setup.LinkId, address);
        return (setup, await SentAsync(setup.Subject, setup.LinkId));
    }

    private static async Task SetAddressAsync(Guid linkId, string? address)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        await connection.ExecuteAsync(
            "UPDATE dbo.familiares SET correo = @address WHERE id = (SELECT familiar_id FROM dbo.residentes_familiares WHERE id = @linkId)", new { address, linkId });
    }

    private static async Task<List<EmailRow>> RowsAsync(Guid requestId)
    {
        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        return (await connection.QueryAsync<EmailRow>("""
            SELECT estado_codigo AS Estado, motivo_codigo AS Motivo, acs_operacion_id AS Operacion, CAST(intentos AS INT) AS Intentos, referencia_clave AS Clave,
                   idioma AS Idioma, plantilla_version AS Version
              FROM dbo.envios_correo WITH (NOLOCK) WHERE referencia_id = @requestId ORDER BY creado_en
            """, new { requestId })).ToList();
    }

    private static Task<int> LastChangeNumberAsync(Guid requestId) => CountAsync(
        "SELECT MAX(numero) FROM dbo.citas_solicitudes_cambios WHERE solicitud_id = @requestId", new { requestId });

    private static Task<int> StatusCountAsync(Guid requestId, string status) => SavedAsync(requestId, status);

    [Fact]
    public async Task Proponer_EnviaUnSoloCorreo_AlFamiliarDeLaSolicitud_ConElTextoGenerico_YLoAnotaSinLaDireccion()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Uno");
        var otherLink = await AddFamilyAsync(setup.Admin, setup.ResidentId, "Marta Ficticia");
        await SetAddressAsync(otherLink, "otra.persona@example.test");
        var sender = new FakeEmailSender();

        var proposed = await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(sender, Active()));

        Assert.True(proposed.Ok, proposed.Error?.Message);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(FamilyAddress, message.To);
        Assert.Equal("Tienes una propuesta de cita pendiente en ResidApp", message.Subject);
        Assert.Contains(BaseUrl, message.PlainText);
        Assert.DoesNotContain("Residente Aviso Correo Uno", message.PlainText + message.Html + message.Subject);
        Assert.DoesNotContain("Lucía", message.PlainText + message.Html + message.Subject);
        var row = Assert.Single(await RowsAsync(request.Id));
        Assert.Equal(("ENVIADO", (string?)null, 1, "es", "1"), (row.Estado, row.Motivo, row.Intentos, row.Idioma, row.Version));
        Assert.StartsWith("op-", row.Operacion);
        Assert.Equal((await LastChangeNumberAsync(request.Id)).ToString(), row.Clave);
        Assert.Equal(1, await StatusCountAsync(request.Id, "PROPUESTA"));
    }

    [Fact]
    public async Task ElRegistro_NoTieneColumnasDeContenido_NiGuardaLaDireccion()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Esquema");
        var sender = new FakeEmailSender();
        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(sender, Active()))).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var columns = (await connection.QueryAsync<string>(
            "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('dbo.envios_correo') ORDER BY column_id")).ToList();
        Assert.Equal(
            ["id", "centro_id", "plantilla_codigo", "plantilla_version", "idioma", "familiar_id", "referencia_id", "referencia_clave", "estado_codigo", "motivo_codigo",
             "acs_operacion_id", "intentos", "creado_en", "resuelto_en"], columns);
        var dump = string.Join('|', await connection.QueryAsync<string>(
            "SELECT CONCAT(plantilla_codigo COLLATE DATABASE_DEFAULT, estado_codigo COLLATE DATABASE_DEFAULT, motivo_codigo COLLATE DATABASE_DEFAULT, acs_operacion_id) FROM dbo.envios_correo WITH (NOLOCK) WHERE referencia_id = @id",
            new { id = request.Id }));
        Assert.DoesNotContain("example.test", dump);
    }

    [Fact]
    public async Task SinCorreo_SeAnotaOmitido_SinErrorNiEnvio()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Sin Correo", address: null);
        var sender = new FakeEmailSender();

        var proposed = await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(sender, Active()));

        Assert.True(proposed.Ok, proposed.Error?.Message);
        Assert.Empty(sender.Sent);
        var row = Assert.Single(await RowsAsync(request.Id));
        Assert.Equal(("OMITIDO", "SIN_CORREO", 0), (row.Estado, row.Motivo, row.Intentos));
    }

    [Fact]
    public async Task ConElEnvioApagado_NoSeEnviaNiSeAnotaNada_YLaPropuestaSeGuarda()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Apagado");
        var sender = new FakeEmailSender();

        var proposed = await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(sender, EmailSettings.Off));

        Assert.True(proposed.Ok, proposed.Error?.Message);
        Assert.Empty(sender.Sent);
        Assert.Empty(await RowsAsync(request.Id));
        Assert.Equal(1, await StatusCountAsync(request.Id, "PROPUESTA"));
    }

    [Fact]
    public async Task FueraDeProduccion_SoloSeEnviaALasDireccionesPermitidas()
    {
        var (setup, blocked) = await WithRequestAsync("Residente Aviso Correo Permitida Uno");
        var other = await OtherResidentFamilyAsync(setup, "Residente Aviso Correo Permitida Dos");
        await SetAddressAsync(other.LinkId, "Permitida.Ficticia@Example.Test");
        var allowed = await SentAsync(other.Subject, other.LinkId);
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active(false, "permitida.ficticia@example.test"));

        Assert.True((await ProposeAsync(setup, blocked.Id, await FarSlotAsync(setup, blocked.Id), notice)).Ok);
        Assert.True((await ProposeAsync(setup, allowed.Id, await FarSlotAsync(setup, allowed.Id), notice)).Ok);

        Assert.Equal("Permitida.Ficticia@Example.Test", Assert.Single(sender.Sent).To);
        var blockedRow = Assert.Single(await RowsAsync(blocked.Id));
        Assert.Equal(("OMITIDO", "DESTINO_NO_PERMITIDO"), (blockedRow.Estado, blockedRow.Motivo));
        Assert.Equal("ENVIADO", Assert.Single(await RowsAsync(allowed.Id)).Estado);
    }

    [Theory]
    [InlineData("lanza", "FALLIDO", "ACS_NO_DISPONIBLE")]
    [InlineData("rechaza", "FALLIDO", "ACS_RECHAZADO")]
    [InlineData("tarda", "FALLIDO", "TIEMPO_AGOTADO")]
    public async Task SiElProveedorFalla_LaPropuestaSeGuardaIgual_YElRegistroDiceElMotivoCerrado(string failure, string state, string reason)
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Falla " + failure);
        var sender = new FakeEmailSender
        {
            Behavior = failure switch
            {
                "lanza" => (_, _) => throw new InvalidOperationException("Fallo con la dirección " + FamilyAddress),
                "rechaza" => (_, _) => Task.FromResult(EmailSendResult.Fail("ACS_RECHAZADO")),
                _ => async (_, _) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)); // sin mirar el token: el servicio no puede esperarlo
                    return EmailSendResult.Ok("tarde");
                },
            },
        };

        var proposed = await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(sender, Active(), TimeSpan.FromMilliseconds(300)));

        Assert.True(proposed.Ok, proposed.Error?.Message);
        Assert.Equal(1, await StatusCountAsync(request.Id, "PROPUESTA"));
        var row = Assert.Single(await RowsAsync(request.Id));
        Assert.Equal((state, reason, 1), (row.Estado, row.Motivo, row.Intentos));
    }

    /// <summary>Entre proponer y enviar puede pasar de todo: cada caso vuelve a comprobarse en el momento de enviar (aquí se llama al aviso a mano, como lo haría un
    /// envío diferido) y no sale ningún correo.</summary>
    [Theory]
    [InlineData("baja", "PROPUESTA_NO_VIGENTE")]
    [InlineData("cerrada", "PROPUESTA_NO_VIGENTE")]
    [InlineData("otroHorario", "PROPUESTA_NO_VIGENTE")]
    [InlineData("caducada", "PROPUESTA_NO_VIGENTE")]
    [InlineData("sustituida", "PROPUESTA_NO_VIGENTE")]
    [InlineData("suspension", "SIN_ACCESO")]
    [InlineData("desvinculado", "SIN_ACCESO")]
    [InlineData("revocada", "SIN_ACCESO")]
    public async Task ElAviso_VuelveAComprobarLaVigenciaYElAcceso_EnElMomentoDeEnviar(string change, string reason)
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Vigencia " + change);
        var silent = new FakeEmailSender();
        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(silent, EmailSettings.Off))).Ok);
        var number = await LastChangeNumberAsync(request.Id);
        var status = EstadoResidenteTests.Build(setup.Admin.ExternalSubject);
        var familyRequests = new FamilyAppointmentRequestsApplicationService(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedAvisoSessionIdentityProvider(setup.Subject),
            new SqlFamilyAppointmentRequests(TestDatabase.ConnectionFactory));

        switch (change)
        {
            case "baja":
                Assert.True((await status.DischargeAsync(new DischargeResidentCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), ResidentDischargeReason.AltaVoluntaria, null))).Ok);
                break;
            case "suspension":
                Assert.True((await status.SuspendAsync(new SuspendResidentCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, Guid.NewGuid(), null))).Ok);
                break;
            case "cerrada":
                Assert.True((await AdminRequests(setup.Admin.ExternalSubject, Notice(silent, EmailSettings.Off)).CloseAsync(new CloseAppointmentRequestCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, request.Id, "Motivo interno de prueba."))).Ok);
                break;
            case "otroHorario":
                Assert.True((await familyRequests.RequestAnotherTimeAsync(await QueryAsync(setup.Subject), setup.LinkId, request.Id)).Ok);
                break;
            case "caducada":
                using (var connection = await TestDatabase.ConnectionFactory.OpenAsync())
                {
                    await connection.ExecuteAsync(
                        "UPDATE dbo.citas_solicitudes SET propuesta_en = DATEADD(HOUR, -49, SYSUTCDATETIME()) WHERE id = @id", new { id = request.Id });
                }

                break;
            case "sustituida":
                number--; // el aviso de un cambio anterior al último: otra propuesta lo sustituyó
                break;
            case "desvinculado":
                Assert.True((await AdministracionResidentesTests.Build(setup.Admin.ExternalSubject).UnlinkFamilyMemberAsync(new UnlinkFamilyMemberCommand(
                    setup.Admin.ProfileScopeId, setup.Admin.CenterId, setup.ResidentId, setup.LinkId, "Ya no es familiar."))).Ok);
                break;
            case "revocada":
                await ChangeAsync(setup.Admin, setup.ResidentId, setup.LinkId, FamilyAuthorizationChange.Revocar, 2, "Deja de estar autorizado.");
                break;
        }

        var sender = new FakeEmailSender();
        await Notice(sender, Active()).NotifyAsync(setup.Admin.CenterId, request.Id, number);

        Assert.Empty(sender.Sent);
        var row = Assert.Single(await RowsAsync(request.Id));
        Assert.Equal(("OMITIDO", reason), (row.Estado, row.Motivo));
    }

    [Fact]
    public async Task DosAvisosDeLaMismaPropuestaALaVez_SoloSaleUnCorreo()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Concurrencia");
        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(new FakeEmailSender(), EmailSettings.Off))).Ok);
        var number = await LastChangeNumberAsync(request.Id);
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => notice.NotifyAsync(setup.Admin.CenterId, request.Id, number)));

        Assert.Single(sender.Sent);
        Assert.Equal("ENVIADO", Assert.Single(await RowsAsync(request.Id)).Estado);
    }

    [Fact]
    public async Task OtraPropuestaElMismoDia_EsOtroAviso_PorqueLaClaveEsElNumeroDelCambio()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Otra Propuesta");
        var sender = new FakeEmailSender();
        var notice = Notice(sender, Active());
        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), notice)).Ok);
        var familyRequests = new FamilyAppointmentRequestsApplicationService(
            new SqlProfileScopeDirectoryProvider(TestDatabase.ConnectionFactory), new FixedAvisoSessionIdentityProvider(setup.Subject),
            new SqlFamilyAppointmentRequests(TestDatabase.ConnectionFactory));
        Assert.True((await familyRequests.RequestAnotherTimeAsync(await QueryAsync(setup.Subject), setup.LinkId, request.Id)).Ok);

        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id, 3), notice)).Ok);

        Assert.Equal(2, sender.Sent.Count);
        var rows = await RowsAsync(request.Id);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.Clave).Distinct().Count());
        Assert.All(rows, r => Assert.Equal("ENVIADO", r.Estado));
    }

    [Fact]
    public async Task ElRegistro_SoloPasaDePendienteAUnEstadoFinal_YSuIdentidadNoCambia()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Guarda");
        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(new FakeEmailSender(), Active()))).Ok);

        using var connection = await TestDatabase.ConnectionFactory.OpenAsync();
        var identity = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.envios_correo SET referencia_clave = '99' WHERE referencia_id = @id", new { id = request.Id }));
        Assert.Contains("EMAIL_LOG_IDENTITY_IMMUTABLE", identity.Message);
        var resolved = await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteAsync(
            "UPDATE dbo.envios_correo SET estado_codigo = 'FALLIDO', motivo_codigo = 'ACS_NO_DISPONIBLE' WHERE referencia_id = @id", new { id = request.Id }));
        Assert.Contains("EMAIL_LOG_ALREADY_RESOLVED", resolved.Message);
    }

    [Fact]
    public async Task ElRegistro_EsDelCentro_YOtroCentroNoLoVe()
    {
        var (setup, request) = await WithRequestAsync("Residente Aviso Correo Aislamiento");
        Assert.True((await ProposeAsync(setup, request.Id, await FarSlotAsync(setup, request.Id), Notice(new FakeEmailSender(), Active()))).Ok);
        var otherCenter = await SeedFixture.CreateProfileAsync(SystemProfile.Administracion);

        var seenFromOther = await new SqlProposalNoticeStore(TestDatabase.ConnectionFactory)
            .ReserveAsync(otherCenter.CenterId, request.Id, await LastChangeNumberAsync(request.Id), "es", "1", DateTimeOffset.UtcNow);

        Assert.Null(seenFromOther);
        Assert.Single(await RowsAsync(request.Id));
    }
}

file sealed class FixedAvisoSessionIdentityProvider(string externalSubject) : ISessionIdentityProvider
{
    public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
        Task.FromResult<VerifiedIdentity?>(new VerifiedIdentity(externalSubject));
}
