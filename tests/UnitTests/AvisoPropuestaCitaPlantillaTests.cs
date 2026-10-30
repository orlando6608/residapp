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
    public void UnSoloEnlace_ElConfigurado_SinParametros_Imagenes_NiSeguimiento(string language)
    {
        var email = ProposalNoticeTemplate.Render(language, Url + "/");

        var urls = Regex.Matches(email.PlainText + email.Html, @"https?://[^\s<""']+").Select(m => m.Value).Distinct().ToList();
        Assert.Equal([Url], urls);
        Assert.Single(Regex.Matches(email.PlainText, @"https?://"));
        Assert.DoesNotContain("<img", email.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", string.Join(' ', urls));
        Assert.Contains($"<a href=\"{Url}\">{Url}</a>", email.Html);
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
        Assert.Equal(Regex.Matches(es.Html, "<p>").Count, Regex.Matches(ca.Html, "<p>").Count);
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
