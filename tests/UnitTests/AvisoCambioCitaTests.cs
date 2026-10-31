using System.Text.RegularExpressions;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El aviso de cambio de cita (ADR 0012; script 0082): la plantilla genérica en castellano y catalán, comparada con el texto del borrador de privacidad
/// (docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md, 2.1), y el servicio con un almacén y un proveedor falsos. Ninguna prueba envía nada.</summary>
public class AvisoCambioCitaTests
{
    private const string Url = "https://dev.example.test";
    private const string Address = "familiar.ficticio@example.test";

    [Fact]
    public void Castellano_EsElTextoDelBorrador()
    {
        var email = ChangeNoticeTemplate.Render("es", Url);

        Assert.Equal("Tienes un cambio en una cita en ResidApp", email.Subject);
        Assert.Equal(
            "Hay un cambio en una cita que tienes en ResidApp.\n\n"
            + "Para verlo, entra en ResidApp: https://dev.example.test\n\n"
            + "Aquí no ponemos los detalles, para proteger tu privacidad: los verás al entrar.\n\n"
            + "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.\n\n"
            + "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.\n\n"
            + "—\n"
            + "Recibes este aviso porque esta dirección figura como contacto en ResidApp. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta y solo para enviarte este aviso. Para saber más, ejercer tus derechos de protección de datos o dejar de recibir estos avisos, dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa.\n",
            email.PlainText);
    }

    [Fact]
    public void Catalan_EsElTextoDelBorrador_YAvisaDeQueLaPantallaEstaEnCastellano()
    {
        var email = ChangeNoticeTemplate.Render("ca", Url);

        Assert.Equal("Tens un canvi en una cita a ResidApp", email.Subject);
        Assert.Equal(
            "Hi ha un canvi en una cita que tens a ResidApp.\n\n"
            + "Per veure'l, entra a ResidApp: https://dev.example.test\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.\n\n"
            + "Aquí no posem els detalls, per protegir la teva privadesa: els veuràs en entrar.\n\n"
            + "No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.\n\n"
            + "Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.\n\n"
            + "—\n"
            + "Reps aquest avís perquè aquesta adreça figura com a contacte a ResidApp. El responsable de les teves dades és el centre que la va registrar; ResidApp les tracta per compte seu i només per enviar-te aquest avís. Per saber-ne més, exercir els teus drets de protecció de dades o deixar de rebre aquests avisos, adreça't al centre. A l'apartat «Privacidad» del portal (en castellà) trobaràs la informació completa.\n",
            email.PlainText);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElPieEsElMismoQueElDelAvisoDePropuesta(string language)
    {
        var footer = (ChangeNoticeTemplate.Render(language, Url).PlainText.Split("—\n")[1], ProposalNoticeTemplate.Render(language, Url).PlainText.Split("—\n")[1]);

        Assert.Equal(footer.Item2, footer.Item1);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void UnSoloEnlace_ElConfigurado_SinParametros_NiSeguimiento_YVisibleEnSuRecuadro(string language)
    {
        var email = ChangeNoticeTemplate.Render(language, Url + "/");

        var urls = Regex.Matches(email.PlainText + email.Html, @"https?://[^\s<""']+").Select(m => m.Value).Distinct().ToList();
        Assert.Equal([Url], urls);
        Assert.Single(Regex.Matches(email.PlainText, @"https?://"));
        Assert.DoesNotContain("?", string.Join(' ', urls));
        var highlighted = Assert.Single(Regex.Matches(email.Html, @"<p class=""texto destacado""[^>]*>(.*?)</p>")).Groups[1].Value;
        Assert.Contains($"<a href=\"{Url}\">{Url}</a>", highlighted);
        Assert.Equal($"<a href=\"{Url}\">{Url}</a>", Regex.Match(email.Html, @"<a [^>]*>.*?</a>").Value);
        Assert.Single(Regex.Matches(email.Html, @"<img "));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElAsuntoYLaPrimeraLinea_NoLlevanLaPalabraCentro_NiDicenQueHaPasado(string language)
    {
        var email = ChangeNoticeTemplate.Render(language, Url);
        var preview = email.Subject + " " + email.PlainText.Split("\n\n")[0];

        Assert.DoesNotMatch(@"(?i)centr|cancel|reprogram|\bhora\b|\bdía\b|\bdia\b|equip|modalidad|modalitat|motivo|motiu", preview);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void NoNombraNiPuedeNombrar_ResidenteUnidadMotivoSaludNiLoQueHaPasado(string language)
    {
        var email = ChangeNoticeTemplate.Render(language, Url);
        var all = email.Subject + email.PlainText + email.Html;

        foreach (var forbidden in new[] { "residente", "residencia", "resident", "salud", "salut", "médico", "metge", "enfermer", "unidad", "unitat", "motivo", "motiu", "cancel", "reprogram" })
        {
            Assert.DoesNotContain(forbidden, all, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LaPlantillaSoloRecibeIdiomaYDireccion_NoLaAccionDelCambio()
    {
        var render = typeof(ChangeNoticeTemplate).GetMethod(nameof(ChangeNoticeTemplate.Render))!;

        Assert.Equal([typeof(string), typeof(string)], render.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(["language", "baseUrl"], render.GetParameters().Select(p => p.Name));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElHtml_SoloUsaEtiquetasAtributosYClasesDeLaListaCerrada_ConElIconoComoUnicaImagen(string language)
    {
        var email = ChangeNoticeTemplate.Render(language, Url);
        var html = email.Html;

        Assert.All(Regex.Matches(html, @"<([a-zA-Z][a-zA-Z0-9]*)"), m => Assert.Contains(m.Groups[1].Value, AvisoPropuestaCitaPlantillaTests.AllowedTags));
        foreach (Match tag in Regex.Matches(html, @"<[a-zA-Z][^>]*>"))
        {
            foreach (Match attribute in Regex.Matches(tag.Value, @"\s([a-zA-Z][\w-]*)=""([^""]*)"""))
            {
                Assert.Contains(attribute.Groups[1].Value, AvisoPropuestaCitaPlantillaTests.AllowedAttributes);
                if (attribute.Groups[1].Value == "class")
                {
                    Assert.All(attribute.Groups[2].Value.Split(' '), c => Assert.Contains(c, AvisoPropuestaCitaPlantillaTests.AllowedClasses));
                }
            }
        }

        Assert.DoesNotContain("<!--", html);
        Assert.Equal([$"cid:{EmailLayout.IconContentId}"], Regex.Matches(html, @"src=""([^""]*)""").Select(m => m.Groups[1].Value));
        Assert.Same(EmailLayout.Icon, Assert.Single(email.InlineImages!));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElTextoQueSeVeEnElHtml_EsElDelTextoPlano_MasLaPalabraResidApp(string language)
    {
        var email = ChangeNoticeTemplate.Render(language, Url);

        var visible = Regex.Replace(email.Html, @"<(style|title)>.*?</\1>", "", RegexOptions.Singleline);
        visible = System.Net.WebUtility.HtmlDecode(Regex.Replace(visible, "<[^>]*>", " "));
        Assert.Equal(Collapse("ResidApp " + email.PlainText.Replace("—", "")), Collapse(visible));

        static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    }

    [Fact]
    public void LasDosLenguas_TienenLaMismaEstructura_YUnIdiomaDesconocidoNoSeAdmite()
    {
        var es = ChangeNoticeTemplate.Render("es", Url);
        var ca = ChangeNoticeTemplate.Render("ca", Url);

        Assert.Equal(es.PlainText.Split("\n\n").Length, ca.PlainText.Split("\n\n").Length);
        Assert.Equal(es.PlainText.Split("\n\n").Length, Regex.Matches(es.Html, "<p[ >]").Count);
        Assert.Equal(Regex.Matches(es.Html, "<p[ >]").Count, Regex.Matches(ca.Html, "<p[ >]").Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => ChangeNoticeTemplate.Render("en", Url));
    }

    private sealed class FakeStore : IChangeNoticeStore
    {
        public List<(Guid Id, string State, string? Reason, int Attempts)> Resolved { get; } = [];

        public List<(int Number, string Language, string Version)> Registered { get; } = [];

        public List<DateTimeOffset> ListedBefore { get; } = [];

        public Func<IReadOnlyList<PendingChangeNotice>> Due { get; set; } = () => [];

        public Func<bool> Claim { get; set; } = () => true;

        public Func<ChangeNoticePreparation> Prepare { get; set; } = () => new(Address, null);

        public Action? OnRegister { get; set; }

        public Task RegisterAsync(CenterId centerId, Guid appointmentId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default)
        {
            OnRegister?.Invoke();
            Registered.Add((changeNumber, language, templateVersion));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PendingChangeNotice>> ListDueAsync(DateTimeOffset reservedBefore, int limit, CancellationToken ct = default)
        {
            ListedBefore.Add(reservedBefore);
            return Task.FromResult(Due());
        }

        public Task<bool> ClaimAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Claim());

        public Task<ChangeNoticePreparation> PrepareAsync(PendingChangeNotice notice, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(Prepare());

        public Task ResolveAsync(Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default)
        {
            Resolved.Add((id, state, reason, attempts));
            return Task.CompletedTask;
        }

        public Task<bool> HasTaskIdentityAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class FakeSender(Func<EmailMessage, CancellationToken, Task<EmailSendResult>>? behavior = null) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return behavior is null ? Task.FromResult(EmailSendResult.Ok("op")) : behavior(message, ct);
        }
    }

    private static readonly PendingChangeNotice Notice = new(Guid.NewGuid(), CenterId.From(Guid.NewGuid()), Guid.NewGuid(), 2, "es");

    private static EmailSettings On(bool anyRecipient = true, params string[] allowed) => new(true, Url, "es", anyRecipient, allowed, true);

    private static FakeStore WithOneDue() => new() { Due = () => [Notice] };

    [Fact]
    public async Task ReservarSinElInterruptorPropio_NoToca_ElAlmacen_AunqueElCorreoEsteActivo()
    {
        var store = new FakeStore { OnRegister = () => throw new InvalidOperationException("no debería llamarse") };

        await new ChangeNoticeService(store, new FakeSender(), new EmailSettings(true, Url, "es", true, [])).RegisterAsync(Notice.CenterId, Notice.AppointmentId, 2);
        await new ChangeNoticeService(store, new FakeSender(), EmailSettings.Off with { ChangeNotice = true }).RegisterAsync(Notice.CenterId, Notice.AppointmentId, 2);

        Assert.Empty(store.Registered);
    }

    [Fact]
    public async Task Reservar_GuardaElNumeroDelCambio_ElIdiomaYLaVersionDeLaPlantilla()
    {
        var store = new FakeStore();

        await new ChangeNoticeService(store, new FakeSender(), On()).RegisterAsync(Notice.CenterId, Notice.AppointmentId, 3);

        Assert.Equal((3, "es", ChangeNoticeTemplate.Version), Assert.Single(store.Registered));
    }

    [Fact]
    public async Task SiElAlmacenFallaAlReservar_NoLanzaNada_YElRegistroNoLlevaLaDireccion()
    {
        var log = new List<string>();
        var store = new FakeStore { OnRegister = () => throw new InvalidOperationException($"fallo con {Address}") };

        await new ChangeNoticeService(store, new FakeSender(), On(), log.Add).RegisterAsync(Notice.CenterId, Notice.AppointmentId, 2);

        var line = Assert.Single(log);
        Assert.DoesNotContain("example.test", line);
        Assert.Contains("InvalidOperationException", line);
    }

    [Fact]
    public async Task ConElEnvioApagado_ProcesarNiSiquieraLista_ElAlmacen()
    {
        var store = WithOneDue();
        var sender = new FakeSender();

        Assert.Equal(0, await new ChangeNoticeService(store, sender, new EmailSettings(true, Url, "es", true, [])).ProcessDueAsync(DateTimeOffset.UtcNow));
        Assert.Equal(0, await new ChangeNoticeService(store, sender, EmailSettings.Off).ProcessDueAsync(DateTimeOffset.UtcNow));

        Assert.Empty(store.ListedBefore);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Procesar_EnviaUnSoloCorreoGenerico_AQuienReservo_YLoAnotaConUnIntento()
    {
        var store = WithOneDue();
        var sender = new FakeSender();
        var now = DateTimeOffset.UtcNow;

        var claimed = await new ChangeNoticeService(store, sender, On()).ProcessDueAsync(now);

        Assert.Equal(1, claimed);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(Address, message.To);
        Assert.Equal("Tienes un cambio en una cita en ResidApp", message.Subject);
        Assert.Equal((Notice.Id, "ENVIADO", (string?)null, 1), Assert.Single(store.Resolved));
        Assert.Equal(now - ChangeNoticeService.DefaultDelay, Assert.Single(store.ListedBefore));
    }

    [Fact]
    public async Task SiOtraEjecucionYaLoReclamo_NoSeComprueba_NiSeEnvia()
    {
        var store = WithOneDue();
        store.Claim = () => false;
        store.Prepare = () => throw new InvalidOperationException("no debería llamarse");
        var sender = new FakeSender();

        Assert.Equal(0, await new ChangeNoticeService(store, sender, On()).ProcessDueAsync(DateTimeOffset.UtcNow));

        Assert.Empty(sender.Sent);
        Assert.Empty(store.Resolved);
    }

    [Theory]
    [InlineData("CITA_NO_VIGENTE")]
    [InlineData("CITA_PASADA")]
    [InlineData("SIN_ACCESO")]
    [InlineData("SIN_CORREO")]
    public async Task SiElAlmacenDiceQueNoSeEnvia_SeAnotaOmitidoConSuMotivo_SinEnvio(string reason)
    {
        var store = WithOneDue();
        store.Prepare = () => new(null, reason);
        var sender = new FakeSender();

        await new ChangeNoticeService(store, sender, On()).ProcessDueAsync(DateTimeOffset.UtcNow);

        Assert.Empty(sender.Sent);
        Assert.Equal((Notice.Id, "OMITIDO", (string?)reason, 0), Assert.Single(store.Resolved));
    }

    [Fact]
    public async Task FueraDeProduccion_SoloSeEnviaALasDireccionesPermitidas()
    {
        var blockedStore = WithOneDue();
        var allowedStore = WithOneDue();
        var sender = new FakeSender();

        await new ChangeNoticeService(blockedStore, sender, On(anyRecipient: false, "otra.persona@example.test")).ProcessDueAsync(DateTimeOffset.UtcNow);
        await new ChangeNoticeService(allowedStore, sender, On(anyRecipient: false, Address.ToUpperInvariant())).ProcessDueAsync(DateTimeOffset.UtcNow);

        Assert.Equal((Notice.Id, "OMITIDO", (string?)"DESTINO_NO_PERMITIDO", 0), Assert.Single(blockedStore.Resolved));
        Assert.Equal("ENVIADO", Assert.Single(allowedStore.Resolved).State);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task SiElProveedorLanzaUnaExcepcionConLaDireccion_ElRegistroDiceUnCodigoCerrado_YElLogNoLaLleva()
    {
        var log = new List<string>();
        var store = WithOneDue();
        var sender = new FakeSender((_, _) => throw new InvalidOperationException($"no se pudo enviar a {Address}"));

        await new ChangeNoticeService(store, sender, On(), log.Add).ProcessDueAsync(DateTimeOffset.UtcNow);

        Assert.Equal((Notice.Id, "FALLIDO", (string?)"ACS_NO_DISPONIBLE", 1), Assert.Single(store.Resolved));
        Assert.All(log, line => Assert.DoesNotContain("example.test", line));
    }

    [Fact]
    public async Task SiElProveedorRechaza_SeAnotaFallidoConSuCodigo()
    {
        var store = WithOneDue();
        var sender = new FakeSender((_, _) => Task.FromResult(EmailSendResult.Fail("ACS_RECHAZADO")));

        await new ChangeNoticeService(store, sender, On()).ProcessDueAsync(DateTimeOffset.UtcNow);

        Assert.Equal((Notice.Id, "FALLIDO", (string?)"ACS_RECHAZADO", 1), Assert.Single(store.Resolved));
    }

    [Fact]
    public async Task SiElProveedorTardaMasDelMaximo_SeAnotaTiempoAgotado()
    {
        var store = WithOneDue();
        var sender = new FakeSender(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return EmailSendResult.Ok("nunca");
        });

        await new ChangeNoticeService(store, sender, On(), sendTimeout: TimeSpan.FromMilliseconds(50)).ProcessDueAsync(DateTimeOffset.UtcNow);

        Assert.Equal((Notice.Id, "FALLIDO", (string?)"TIEMPO_AGOTADO", 1), Assert.Single(store.Resolved));
    }

    [Fact]
    public async Task SiElAlmacenFallaAlLeerLosPendientes_NoLanzaNada_YNoEnviaNada()
    {
        var log = new List<string>();
        var store = new FakeStore { Due = () => throw new InvalidOperationException($"fallo con {Address}") };
        var sender = new FakeSender();

        Assert.Equal(0, await new ChangeNoticeService(store, sender, On(), log.Add).ProcessDueAsync(DateTimeOffset.UtcNow));

        Assert.Empty(sender.Sent);
        Assert.All(log, line => Assert.DoesNotContain("example.test", line));
    }

    [Fact]
    public async Task UnAvisoQueFalla_NoImpideProcesarLosSiguientes()
    {
        var second = Notice with { Id = Guid.NewGuid() };
        var store = new FakeStore { Due = () => [Notice, second] };
        var calls = 0;
        store.Prepare = () => ++calls == 1 ? throw new InvalidOperationException("fallo") : new(Address, null);
        var sender = new FakeSender();

        Assert.Equal(2, await new ChangeNoticeService(store, sender, On()).ProcessDueAsync(DateTimeOffset.UtcNow));

        Assert.Single(sender.Sent);
        Assert.Equal(second.Id, Assert.Single(store.Resolved).Id);
    }
}
