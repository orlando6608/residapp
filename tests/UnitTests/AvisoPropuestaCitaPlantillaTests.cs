using System.Text.RegularExpressions;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El aviso de propuesta de cita (ADR 0012, pieza 1): la plantilla genérica en castellano y catalán, comparada con el texto aprobado en la lectura de
/// privacidad (docs/privacidad/aviso-cita-por-correo.md, 2.2), y el caso de uso con un almacén y un proveedor falsos. Ninguna prueba envía nada.</summary>
public class AvisoPropuestaCitaPlantillaTests
{
    private const string Url = "https://dev.example.test";

    [Fact]
    public void Castellano_EsElTextoAprobado()
    {
        var email = ProposalNoticeTemplate.Render("es", Url);

        Assert.Equal("Tienes una propuesta de cita pendiente en ResidApp", email.Subject);
        Assert.Equal(
            "Hay una propuesta de cita pendiente de tu respuesta en ResidApp.\n\n"
            + "Para verla, entra en ResidApp: https://dev.example.test\n\n"
            + "Las propuestas tienen un plazo para responder.\n\n"
            + "No respondas a este correo: nadie lo lee. Si necesitas algo, llama al centro.\n\n"
            + "Si no esperabas este aviso, ignóralo. ResidApp nunca te pedirá tu contraseña por correo, por teléfono ni por mensaje.\n\n"
            + "—\n"
            + "Recibes este aviso porque esta dirección figura como contacto en ResidApp. El responsable de tus datos es el centro que la registró; ResidApp los trata por su cuenta y solo para enviarte este aviso. Para saber más, ejercer tus derechos de protección de datos o dejar de recibir estos avisos, dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa.\n",
            email.PlainText);
    }

    [Fact]
    public void Catalan_EsElTextoAprobado_YAvisaDeQueLaPantallaEstaEnCastellano()
    {
        var email = ProposalNoticeTemplate.Render("ca", Url);

        Assert.Equal("Tens una proposta de cita pendent a ResidApp", email.Subject);
        Assert.Equal(
            "Hi ha una proposta de cita pendent de la teva resposta a ResidApp.\n\n"
            + "Per veure-la, entra a ResidApp: https://dev.example.test\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.\n\n"
            + "Les propostes tenen un termini per respondre.\n\n"
            + "No responguis a aquest correu: no el llegeix ningú. Si necessites alguna cosa, truca al centre.\n\n"
            + "Si no esperaves aquest avís, ignora'l. ResidApp mai no et demanarà la contrasenya per correu, per telèfon ni per missatge.\n\n"
            + "—\n"
            + "Reps aquest avís perquè aquesta adreça figura com a contacte a ResidApp. El responsable de les teves dades és el centre que la va registrar; ResidApp les tracta per compte seu i només per enviar-te aquest avís. Per saber-ne més, exercir els teus drets de protecció de dades o deixar de rebre aquests avisos, adreça't al centre. A l'apartat «Privacidad» del portal (en castellà) trobaràs la informació completa.\n",
            email.PlainText);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void UnSoloEnlace_ElConfigurado_SinParametros_ImagenesRemotas_NiSeguimiento(string language)
    {
        var email = ProposalNoticeTemplate.Render(language, Url + "/");

        var urls = Regex.Matches(email.PlainText + email.Html, @"https?://[^\s<""']+").Select(m => m.Value).Distinct().ToList();
        Assert.Equal([Url], urls);
        Assert.Single(Regex.Matches(email.PlainText, @"https?://"));
        Assert.Single(Regex.Matches(email.Html, @"<a "));
        Assert.DoesNotContain("?", string.Join(' ', urls));
        Assert.Contains($"<a href=\"{Url}\">{Url}</a>", email.Html);
        Assert.DoesNotContain("url(", email.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void LaUnicaImagen_EsElIconoDeResidApp_DentroDelMensaje_YDecorativa(string language)
    {
        var email = ProposalNoticeTemplate.Render(language, Url);

        var image = Assert.Single(email.InlineImages!);
        Assert.Equal(EmailLayout.IconContentId, image.ContentId);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.Content.Take(4));
        Assert.InRange(image.Content.Length, 1, 30_000);
        var tag = Assert.Single(Regex.Matches(email.Html, @"<img [^>]*>")).Value;
        Assert.Contains($"src=\"cid:{EmailLayout.IconContentId}\"", tag);
        Assert.Contains("alt=\"\"", tag);
        Assert.DoesNotContain("<link", email.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", email.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElEnlaceVisible_EstaDestacado_YNoSeEscondeTrasOtraPalabra(string language)
    {
        var email = ProposalNoticeTemplate.Render(language, Url);

        var highlighted = Assert.Single(Regex.Matches(email.Html, @"<p class=""texto destacado""[^>]*>(.*?)</p>")).Groups[1].Value;
        Assert.Contains($"<a href=\"{Url}\">{Url}</a>", highlighted);
        Assert.Equal($"<a href=\"{Url}\">{Url}</a>", Regex.Match(email.Html, @"<a [^>]*>.*?</a>").Value);
    }

    internal static readonly string[] AllowedTags = ["html", "head", "meta", "title", "style", "body", "table", "tr", "td", "img", "p", "a", "br"];

    internal static readonly string[] AllowedAttributes =
        ["lang", "charset", "name", "content", "class", "style", "role", "width", "height", "cellpadding", "cellspacing", "border", "align", "src", "alt", "href"];

    internal static readonly string[] AllowedClasses = ["fondo", "tarjeta", "texto", "destacado", "pie"];

    /// <summary>Lista cerrada de lo permitido (docs/privacidad/aviso-cita-icono-en-correo.md, A2, A4 y A6): ninguna etiqueta, atributo ni clase fuera de lista, así que un
    /// recurso remoto, un texto oculto o una palabra escondida en un atributo no caben aunque nadie los hubiera previsto.</summary>
    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElHtml_SoloUsaEtiquetasAtributosYClasesDeLaListaCerrada_SinTextoOcultoNiComentarios(string language)
    {
        var html = ProposalNoticeTemplate.Render(language, Url).Html;

        Assert.All(Regex.Matches(html, @"<([a-zA-Z][a-zA-Z0-9]*)"), m => Assert.Contains(m.Groups[1].Value, AllowedTags));
        foreach (Match tag in Regex.Matches(html, @"<[a-zA-Z][^>]*>"))
        {
            foreach (Match attribute in Regex.Matches(tag.Value, @"\s([a-zA-Z][\w-]*)=""([^""]*)"""))
            {
                Assert.Contains(attribute.Groups[1].Value, AllowedAttributes);
                var value = attribute.Groups[2].Value;
                if (attribute.Groups[1].Value == "class")
                {
                    Assert.All(value.Split(' '), c => Assert.Contains(c, AllowedClasses));
                }

                if (attribute.Groups[1].Value == "alt")
                {
                    Assert.Equal("", value);
                }
            }
        }

        Assert.DoesNotContain("<!--", html);
        foreach (var hidden in new[] { "display:none", "visibility:hidden", "font-size:0", "opacity:0", "max-height:0", "mso-hide", "@font-face", "data:", "javascript:" })
        {
            Assert.DoesNotContain(hidden, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElTextoQueSeVeEnElHtml_EsElDelTextoPlano_MasLaPalabraResidApp(string language)
    {
        var email = ProposalNoticeTemplate.Render(language, Url);

        var visible = Regex.Replace(email.Html, @"<(style|title)>.*?</\1>", "", RegexOptions.Singleline);
        visible = System.Net.WebUtility.HtmlDecode(Regex.Replace(visible, "<[^>]*>", " "));
        Assert.Equal(Collapse("ResidApp " + email.PlainText.Replace("—", "")), Collapse(visible));

        static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElMarcadoDelHtml_NoLlevaPalabrasDeCentroNiDeSalud_FueraDelTextoQueSeVe(string language)
    {
        var markup = Regex.Replace(ProposalNoticeTemplate.Render(language, Url).Html, ">[^<]*<", "><");

        Assert.DoesNotMatch(@"(?i)resid(?!app)|centr|salud|salut|sanit|m[eé]dic|metge|enfermer|geri|cl[ií]nic|asist|cuid|mayor|anci|hospital|unitat|unidad", markup);
    }

    [Fact]
    public void ElIcono_EsSiempreElMismoArchivo_YNoLlevaMetadatosDeQuienLoExporto()
    {
        var icon = EmailLayout.Icon;

        Assert.Equal("b2b58a45302260bad5855c265b15965cdf2c39e64ff3cd05792d007b170baca2", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(icon.Content)).ToLowerInvariant());
        Assert.Equal(("residapp-icono", "icono-residapp.png", "image/png"), (icon.ContentId, icon.Name, icon.ContentType));
        var chunks = new List<string>();
        for (var offset = 8; offset < icon.Content.Length;)
        {
            chunks.Add(System.Text.Encoding.Latin1.GetString(icon.Content, offset + 4, 4));
            offset += 12 + (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(icon.Content.AsSpan(offset, 4));
        }

        Assert.Equal("IHDR", chunks[0]);
        Assert.Equal("IEND", chunks[^1]);
        Assert.Empty(chunks.Intersect(["tEXt", "iTXt", "zTXt", "eXIf", "tIME"]));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void NoNombraNiPuedeNombrar_ResidenteCentroUnidadMotivoNiSalud(string language)
    {
        var email = ProposalNoticeTemplate.Render(language, Url);
        var all = email.Subject + email.PlainText + email.Html;

        foreach (var forbidden in new[] { "residente", "residencia", "resident", "salud", "salut", "médico", "metge", "enfermer", "unidad", "unitat", "motivo", "motiu" })
        {
            Assert.DoesNotContain(forbidden, all, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LasDosLenguas_TienenLaMismaEstructura_YUnIdiomaDesconocidoNoSeAdmite()
    {
        var es = ProposalNoticeTemplate.Render("es", Url);
        var ca = ProposalNoticeTemplate.Render("ca", Url);

        Assert.Equal(es.PlainText.Split("\n\n").Length, ca.PlainText.Split("\n\n").Length);
        Assert.Equal(Regex.Matches(es.Html, "<p[ >]").Count, Regex.Matches(ca.Html, "<p[ >]").Count);
        Assert.Equal(es.PlainText.Split("\n\n").Length, Regex.Matches(es.Html, "<p[ >]").Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProposalNoticeTemplate.Render("en", Url));
        Assert.Equal(["es", "ca"], ProposalNoticeTemplate.Languages);
    }

    private sealed class FakeStore(Func<NoticeReservation?> reserve) : IProposalNoticeStore
    {
        public List<(string State, string? Reason)> Resolved { get; } = [];

        public Task<NoticeReservation?> ReserveAsync(CenterId centerId, Guid requestId, int changeNumber, string language, string templateVersion, DateTimeOffset now, CancellationToken ct = default) =>
            Task.FromResult(reserve());

        public Task ResolveAsync(Guid id, string state, string? reason, string? operationId, int attempts, DateTimeOffset now, CancellationToken ct = default)
        {
            Resolved.Add((state, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSender(Func<Task<EmailSendResult>> send) : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default) => send();
    }

    [Fact]
    public async Task SiElAlmacenFalla_ElAvisoNoLanzaNada_YLoQueSeRegistraNoLlevaLaDireccion()
    {
        var log = new List<string>();
        var service = new ProposalNoticeService(
            new FakeStore(() => throw new InvalidOperationException("fallo con familiar.ficticio@example.test")),
            new FakeSender(() => Task.FromResult(EmailSendResult.Ok("op"))), new EmailSettings(true, Url, "es", true, []), log.Add);

        await service.NotifyAsync(CenterId.From(Guid.NewGuid()), Guid.NewGuid(), 2);

        var line = Assert.Single(log);
        Assert.DoesNotContain("example.test", line);
        Assert.Contains("InvalidOperationException", line);
    }

    [Fact]
    public async Task SiElProveedorLanzaUnaExcepcionConLaDireccion_ElRegistroDiceUnCodigoCerrado_YElLogNoLaLleva()
    {
        var log = new List<string>();
        var store = new FakeStore(() => new NoticeReservation(Guid.NewGuid(), "familiar.ficticio@example.test", "es"));
        var service = new ProposalNoticeService(
            store, new FakeSender(() => throw new InvalidOperationException("no se pudo enviar a familiar.ficticio@example.test")),
            new EmailSettings(true, Url, "es", true, []), log.Add);

        await service.NotifyAsync(CenterId.From(Guid.NewGuid()), Guid.NewGuid(), 2);

        Assert.Equal(("FALLIDO", "ACS_NO_DISPONIBLE"), Assert.Single(store.Resolved));
        Assert.All(log, line => Assert.DoesNotContain("example.test", line));
    }

    [Fact]
    public async Task ConElEnvioApagado_NiSiquieraSeToca_ElAlmacen()
    {
        var touched = false;
        var service = new ProposalNoticeService(
            new FakeStore(() => { touched = true; return null; }), new FakeSender(() => Task.FromResult(EmailSendResult.Ok("op"))), EmailSettings.Off);

        await service.NotifyAsync(CenterId.From(Guid.NewGuid()), Guid.NewGuid(), 2);

        Assert.False(touched);
    }
}
