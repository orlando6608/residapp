using System.Text.RegularExpressions;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El recordatorio de cita del día antes (ADR 0012; script 0092): la plantilla en castellano y catalán, comparada con el texto del borrador de privacidad
/// (docs/privacidad/recordatorio-cita-por-correo.md, apartado 1), y el servicio con un almacén y un proveedor falsos. Condiciones R1 a R3, R10 y R11 de ese documento.
/// Ninguna prueba envía nada.</summary>
public class RecordatorioCitaTests
{
    private const string Url = "https://dev.example.test";
    private const string Address = "familiar.ficticio@example.test";

    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void Castellano_EsElTextoDelBorrador()
    {
        var email = ReminderTemplate.Render("es", Url);

        Assert.Equal("Tienes una cita mañana en ResidApp", email.Subject);
        Assert.Equal(
            "Tienes una cita mañana en ResidApp.\n\n"
            + "Para ver los detalles, entra en ResidApp: https://dev.example.test\n\n"
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
        var email = ReminderTemplate.Render("ca", Url);

        Assert.Equal("Tens una cita demà a ResidApp", email.Subject);
        Assert.Equal(
            "Tens una cita demà a ResidApp.\n\n"
            + "Per veure'n els detalls, entra a ResidApp: https://dev.example.test\nDe moment, la pantalla a la qual porta l'enllaç és en castellà.\n\n"
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
    public void ElPieEsElMismoQueElDeLosOtrosAvisos(string language)
    {
        var reminderFooter = ReminderTemplate.Render(language, Url).PlainText.Split("—\n")[1];

        Assert.Equal(ProposalNoticeTemplate.Render(language, Url).PlainText.Split("—\n")[1], reminderFooter);
        Assert.Equal(ChangeNoticeTemplate.Render(language, Url).PlainText.Split("—\n")[1], reminderFooter);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void UnSoloEnlace_ElConfigurado_SinParametros_NiSeguimiento(string language)
    {
        var email = ReminderTemplate.Render(language, Url + "/");

        var urls = Regex.Matches(email.PlainText + email.Html, @"https?://[^\s<""']+").Select(m => m.Value).Distinct().ToList();
        Assert.Equal([Url], urls);
        Assert.DoesNotContain("?", string.Join(' ', urls));
        Assert.Single(Regex.Matches(email.Html, @"<img "));
        Assert.Same(EmailLayout.Icon, Assert.Single(email.InlineImages!));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void ElAsuntoYLaPrimeraLinea_NoLlevanLaPalabraCentro_NiMasDatosDeLaCita(string language)
    {
        var email = ReminderTemplate.Render(language, Url);
        var preview = email.Subject + " " + email.PlainText.Split("\n\n")[0];

        Assert.DoesNotMatch(@"(?i)centr|\bhora\b|\bhores\b|equip|modalidad|modalitat|motivo|motiu|\bcon\b|\bamb\b|médic|metge|enfermer|\d", preview);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void NoNombraNiPuedeNombrar_ResidenteSaludNiMotivo_ElUnicoDatoDeLaCitaEsMañana(string language)
    {
        var email = ReminderTemplate.Render(language, Url);
        var all = email.Subject + email.PlainText + email.Html;

        foreach (var forbidden in new[] { "residente", "residencia", "resident", "salud", "salut", "médico", "metge", "enfermer", "unidad", "unitat", "motivo", "motiu", "cancel", "reprogram" })
        {
            Assert.DoesNotContain(forbidden, all, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(language == "es" ? "mañana" : "demà", email.Subject);
    }

    [Fact]
    public void LaPlantillaSoloRecibeIdiomaYDireccion_NiFechaNiHoraNiEquipo()
    {
        var render = typeof(ReminderTemplate).GetMethod(nameof(ReminderTemplate.Render))!;

        Assert.Equal([typeof(string), typeof(string)], render.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(["language", "baseUrl"], render.GetParameters().Select(p => p.Name));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("ca")]
    public void LosTresAvisos_TienenAsuntoYPrimeraLineaDistintos(string language)
    {
        var emails = new[] { ProposalNoticeTemplate.Render(language, Url), ChangeNoticeTemplate.Render(language, Url), ReminderTemplate.Render(language, Url) };

        Assert.Equal(3, emails.Select(e => e.Subject).Distinct().Count());
        Assert.Equal(3, emails.Select(e => e.PlainText.Split("\n\n")[0]).Distinct().Count());
    }

    [Fact]
    public void LasDosLenguas_TienenLaMismaEstructura_YUnIdiomaDesconocidoNoSeAdmite()
    {
        var es = ReminderTemplate.Render("es", Url);
        var ca = ReminderTemplate.Render("ca", Url);

        Assert.Equal(es.PlainText.Split("\n\n").Length, ca.PlainText.Split("\n\n").Length);
        Assert.Equal(Regex.Matches(es.Html, "<p[ >]").Count, Regex.Matches(ca.Html, "<p[ >]").Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReminderTemplate.Render("en", Url));
    }

    private sealed class FakeStore : IReminderStore
    {
        public List<(Guid Id, string State, string? Reason, int Attempts)> Resolved { get; } = [];

        public List<(string Language, string Version)> Reserved { get; } = [];

        public int Listed { get; private set; }

        public Func<IReadOnlyList<PendingReminder>> Pending { get; set; } = () => [];

        public Func<bool> Claim { get; set; } = () => true;

        public Func<ChangeNoticePreparation> Prepare { get; set; } = () => new(Address, null);

        public Action? OnReserve { get; set; }

        public Task<int> ReserveAsync(string language, string templateVersion, CancellationToken ct = default)
        {
            OnReserve?.Invoke();
            Reserved.Add((language, templateVersion));
            return Task.FromResult(3);
        }

        public Task<IReadOnlyList<PendingReminder>> ListPendingAsync(int limit, CancellationToken ct = default)
        {
            Listed++;
            return Task.FromResult(Pending());
        }

        public Func<TimeZoneInfo> Zone { get; set; } = () => Madrid;

        public Task<TimeZoneInfo> ZoneOfCenterAsync(CenterId centerId, CancellationToken ct = default) => Task.FromResult(Zone());

        public Task<bool> ClaimAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Claim());

        public Task<ChangeNoticePreparation> PrepareAsync(PendingReminder reminder, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(Prepare());

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

    private static readonly PendingReminder Reminder = new(Guid.NewGuid(), CenterId.From(Guid.NewGuid()), Guid.NewGuid(), "2026-11-16", "es");

    // Un lunes de invierno (UTC+1): las 10:00 de Madrid son las 09:00 UTC.
    private static DateTimeOffset Madrid1(int hour, int minute = 0) => new(2026, 11, 15, hour, minute, 0, TimeSpan.FromHours(1));

    private static EmailSettings On(bool anyRecipient = true, params string[] allowed) => new(true, Url, "es", anyRecipient, allowed, false, true);

    private static ReminderService Service(FakeStore store, FakeSender sender, EmailSettings settings, Action<string>? log = null, TimeSpan? timeout = null) =>
        new(store, sender, settings, log, timeout, Madrid);

    private static FakeStore WithOnePending() => new() { Pending = () => [Reminder] };

    [Theory]
    [InlineData(9, 59, false)]
    [InlineData(10, 0, true)]
    [InlineData(18, 59, true)]
    [InlineData(19, 0, false)]
    [InlineData(23, 30, false)]
    [InlineData(3, 0, false)]
    public void LaFranjaEsDeLas10ALas1859_HoraDeMadrid(int hour, int minute, bool expected)
    {
        var service = Service(new FakeStore(), new FakeSender(), On());

        Assert.Equal(expected, service.InWindow(Madrid1(hour, minute)));
    }

    [Fact]
    public void LaFranjaSigueLaHoraDeMadrid_TambienEnVerano_NoLaHoraUtc()
    {
        var service = Service(new FakeStore(), new FakeSender(), On());

        // 10:00 de Madrid en verano son las 08:00 UTC; 09:00 UTC ya son las 11:00 en Madrid.
        Assert.True(service.InWindow(new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero)));
        Assert.False(service.InWindow(new DateTimeOffset(2026, 7, 15, 7, 59, 0, TimeSpan.Zero)));
        Assert.False(service.InWindow(new DateTimeOffset(2026, 7, 15, 17, 0, 0, TimeSpan.Zero)));
        // El domingo del cambio de hora de octubre (25): a las 10:00 ya son UTC+1.
        Assert.True(service.InWindow(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero)));
        Assert.False(service.InWindow(new DateTimeOffset(2026, 10, 25, 8, 59, 0, TimeSpan.Zero)));
        // Y el de marzo (29): a las 10:00 ya son UTC+2.
        Assert.True(service.InWindow(new DateTimeOffset(2026, 3, 29, 8, 0, 0, TimeSpan.Zero)));
        Assert.False(service.InWindow(new DateTimeOffset(2026, 3, 29, 7, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task ConElEnvioApagado_NiSeReservaNiSeLista()
    {
        var store = WithOnePending();
        store.OnReserve = () => throw new InvalidOperationException("no debería llamarse");
        var sender = new FakeSender();
        var allOff = new[] { new EmailSettings(true, Url, "es", true, []), EmailSettings.Off with { Reminder = true }, EmailSettings.Off };

        foreach (var settings in allOff)
        {
            var service = Service(store, sender, settings);
            Assert.Equal(0, await service.ReserveDueAsync(Madrid1(11)));
            Assert.Equal(0, await service.ProcessDueAsync(Madrid1(11)));
        }

        Assert.Empty(store.Reserved);
        Assert.Equal(0, store.Listed);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task Reservar_PideSiempreALaBase_ConElIdiomaYLaVersionDeLaPlantilla_YLaFranjaLaDecideLaBase()
    {
        var store = new FakeStore();
        var service = Service(store, new FakeSender(), On());

        // La franja depende de la zona de cada centro y la calcula el procedimiento (script 0110): el servicio no la filtra con una sola hora.
        Assert.Equal(3, await service.ReserveDueAsync(Madrid1(9, 59)));
        Assert.Equal(3, await service.ReserveDueAsync(Madrid1(19, 0)));
        Assert.Equal(2, store.Reserved.Count);
        Assert.All(store.Reserved, reserved => Assert.Equal(("es", ReminderTemplate.Version), reserved));
    }

    [Theory]
    [InlineData(9, 30, false)]
    [InlineData(10, 30, true)]
    [InlineData(18, 30, true)]
    [InlineData(19, 30, false)]
    public async Task Procesar_UsaLaFranjaDelCentro_NoLaDeMadrid(int canaryHour, int minute, bool sends)
    {
        // Canarias va una hora por detrás de la península: las 10:30 allí son las 11:30 en Madrid, y a las 19:30 de Madrid todavía son las 18:30 allí.
        var canary = TimeZoneInfo.FindSystemTimeZoneById("Atlantic/Canary");
        var store = WithOnePending();
        store.Zone = () => canary;
        var sender = new FakeSender();
        var now = new DateTimeOffset(2026, 11, 15, canaryHour, minute, 0, TimeSpan.Zero);

        await new ReminderService(store, sender, On()).ProcessDueAsync(now);

        Assert.Equal(sends ? 1 : 0, sender.Sent.Count);
        Assert.Equal(sends ? "ENVIADO" : "OMITIDO", Assert.Single(store.Resolved).State);
    }

    [Fact]
    public async Task Procesar_SiNoSePuedeLeerLaZonaDelCentro_NoReclamaNiEnvia_YSeReintentaDespues()
    {
        var store = WithOnePending();
        store.Zone = () => throw new InvalidOperationException("sin zona");
        var claimed = false;
        store.Claim = () => claimed = true;
        var sender = new FakeSender();

        Assert.Equal(0, await new ReminderService(store, sender, On()).ProcessDueAsync(Madrid1(11)));

        Assert.False(claimed);
        Assert.Empty(sender.Sent);
        Assert.Empty(store.Resolved);
    }

    [Fact]
    public async Task SiLaReservaFalla_NoLanzaNada_YElRegistroNoLlevaLaDireccion()
    {
        var log = new List<string>();
        var store = new FakeStore { OnReserve = () => throw new InvalidOperationException($"fallo con {Address}") };

        Assert.Equal(0, await Service(store, new FakeSender(), On(), log.Add).ReserveDueAsync(Madrid1(11)));

        var line = Assert.Single(log);
        Assert.DoesNotContain("example.test", line);
        Assert.Contains("InvalidOperationException", line);
    }

    [Fact]
    public async Task Procesar_DentroDeLaFranja_EnviaUnSoloCorreoGenerico_AQuienReservo_YLoAnotaConUnIntento()
    {
        var store = WithOnePending();
        var sender = new FakeSender();

        var claimed = await Service(store, sender, On()).ProcessDueAsync(Madrid1(10));

        Assert.Equal(1, claimed);
        var message = Assert.Single(sender.Sent);
        Assert.Equal(Address, message.To);
        Assert.Equal("Tienes una cita mañana en ResidApp", message.Subject);
        Assert.Equal((Reminder.Id, "ENVIADO", (string?)null, 1), Assert.Single(store.Resolved));
    }

    [Theory]
    [InlineData(9, 59)]
    [InlineData(19, 0)]
    [InlineData(23, 0)]
    public async Task Procesar_FueraDeLaFranja_NoComprueba_NiEnvia_YLoReservadoQuedaFueraDePlazo(int hour, int minute)
    {
        var store = WithOnePending();
        store.Prepare = () => throw new InvalidOperationException("no debería llamarse");
        var sender = new FakeSender();

        await Service(store, sender, On()).ProcessDueAsync(Madrid1(hour, minute));

        Assert.Empty(sender.Sent);
        Assert.Equal((Reminder.Id, "OMITIDO", (string?)"FUERA_DE_PLAZO", 0), Assert.Single(store.Resolved));
    }

    [Fact]
    public async Task SiOtraEjecucionYaLoReclamo_NoSeComprueba_NiSeEnvia()
    {
        var store = WithOnePending();
        store.Claim = () => false;
        store.Prepare = () => throw new InvalidOperationException("no debería llamarse");
        var sender = new FakeSender();

        Assert.Equal(0, await Service(store, sender, On()).ProcessDueAsync(Madrid1(11)));

        Assert.Empty(sender.Sent);
        Assert.Empty(store.Resolved);
    }

    [Theory]
    [InlineData("CITA_NO_VIGENTE")]
    [InlineData("CITA_PASADA")]
    [InlineData("FUERA_DE_PLAZO")]
    [InlineData("SIN_ACCESO")]
    [InlineData("SIN_CORREO")]
    [InlineData("DIRECCION_NO_CONFIRMADA")]
    public async Task SiElAlmacenDiceQueNoSeEnvia_SeAnotaOmitidoConSuMotivo_SinEnvio(string reason)
    {
        var store = WithOnePending();
        store.Prepare = () => new(null, reason);
        var sender = new FakeSender();

        await Service(store, sender, On()).ProcessDueAsync(Madrid1(11));

        Assert.Empty(sender.Sent);
        Assert.Equal((Reminder.Id, "OMITIDO", (string?)reason, 0), Assert.Single(store.Resolved));
    }

    [Fact]
    public async Task FueraDeProduccion_SoloSeEnviaALasDireccionesPermitidas()
    {
        var blockedStore = WithOnePending();
        var allowedStore = WithOnePending();
        var sender = new FakeSender();

        await Service(blockedStore, sender, On(anyRecipient: false, "otra.persona@example.test")).ProcessDueAsync(Madrid1(11));
        await Service(allowedStore, sender, On(anyRecipient: false, Address.ToUpperInvariant())).ProcessDueAsync(Madrid1(11));

        Assert.Equal((Reminder.Id, "OMITIDO", (string?)"DESTINO_NO_PERMITIDO", 0), Assert.Single(blockedStore.Resolved));
        Assert.Equal("ENVIADO", Assert.Single(allowedStore.Resolved).State);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task SiElProveedorLanzaUnaExcepcionConLaDireccion_ElRegistroDiceUnCodigoCerrado_YElLogNoLaLleva()
    {
        var log = new List<string>();
        var store = WithOnePending();
        var sender = new FakeSender((_, _) => throw new InvalidOperationException($"no se pudo enviar a {Address}"));

        await Service(store, sender, On(), log.Add).ProcessDueAsync(Madrid1(11));

        Assert.Equal((Reminder.Id, "FALLIDO", (string?)"ACS_NO_DISPONIBLE", 1), Assert.Single(store.Resolved));
        Assert.All(log, line => Assert.DoesNotContain("example.test", line));
    }

    [Fact]
    public async Task SiElProveedorRechaza_SeAnotaFallidoConSuCodigo_SinReintento()
    {
        var store = WithOnePending();
        var sender = new FakeSender((_, _) => Task.FromResult(EmailSendResult.Fail("ACS_RECHAZADO")));

        await Service(store, sender, On()).ProcessDueAsync(Madrid1(11));

        Assert.Equal((Reminder.Id, "FALLIDO", (string?)"ACS_RECHAZADO", 1), Assert.Single(store.Resolved));
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task SiElProveedorTardaMasDelMaximo_SeAnotaTiempoAgotado()
    {
        var store = WithOnePending();
        var sender = new FakeSender(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return EmailSendResult.Ok("nunca");
        });

        await Service(store, sender, On(), timeout: TimeSpan.FromMilliseconds(50)).ProcessDueAsync(Madrid1(11));

        Assert.Equal((Reminder.Id, "FALLIDO", (string?)"TIEMPO_AGOTADO", 1), Assert.Single(store.Resolved));
    }

    [Fact]
    public async Task SiElAlmacenFallaAlLeerLosPendientes_NoLanzaNada_YNoEnviaNada()
    {
        var log = new List<string>();
        var store = new FakeStore { Pending = () => throw new InvalidOperationException($"fallo con {Address}") };
        var sender = new FakeSender();

        Assert.Equal(0, await Service(store, sender, On(), log.Add).ProcessDueAsync(Madrid1(11)));

        Assert.Empty(sender.Sent);
        Assert.All(log, line => Assert.DoesNotContain("example.test", line));
    }

    [Fact]
    public async Task UnRecordatorioQueFalla_NoImpideProcesarLosSiguientes()
    {
        var second = Reminder with { Id = Guid.NewGuid() };
        var store = new FakeStore { Pending = () => [Reminder, second] };
        var calls = 0;
        store.Prepare = () => ++calls == 1 ? throw new InvalidOperationException("fallo") : new(Address, null);
        var sender = new FakeSender();

        Assert.Equal(2, await Service(store, sender, On()).ProcessDueAsync(Madrid1(11)));

        Assert.Single(sender.Sent);
        Assert.Equal(second.Id, Assert.Single(store.Resolved).Id);
    }
}
