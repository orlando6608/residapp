using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResidApp.Infrastructure.Correo;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Web.Correo;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>El aviso a CJ de que ingeniería ha dejado notas nuevas en sus documentos: un solo correo por despliegue, con el título y el enlace de cada documento y nunca el texto de las
/// notas, sin avisar dos veces de lo mismo, con reintento si falla y sin dejar la dirección ni el texto en el estado ni en el registro. Con un proveedor de correo falso.</summary>
public sealed class AvisoNotasCjTests : IDisposable
{
    private const string Address = "cj.ficticio@example.test";
    private const string SecretNote = "texto-privado-de-la-nota";
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "residapp-notas-estado-" + Guid.NewGuid().ToString("N"));
    private readonly string _docs = Path.Combine(Path.GetTempPath(), "residapp-notas-docs-" + Guid.NewGuid().ToString("N"));
    private readonly RespuestasCjStore _store;
    private readonly FakeSender _sender = new();
    private readonly List<string> _warnings = [];
    private readonly NotasAvisoMemoria _memory = new();

    public AvisoNotasCjTests()
    {
        Directory.CreateDirectory(_docs);
        _store = new RespuestasCjStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PendientesCj:CarpetaRespuestas"] = _state }).Build());
        Document("pruebas-2-medicina-indicaciones", "Guía 2 de pruebas · Medicina e indicaciones");
        Document("pruebas-3-basal-barthel", "Guía 3 de pruebas · Basal &amp; Barthel");
    }

    public void Dispose()
    {
        foreach (var folder in new[] { _state, _docs })
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        if (File.Exists(_state))
        {
            File.Delete(_state);
        }
    }

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool Reject { get; set; }

        public Exception? Throw { get; set; }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Reject)
            {
                return Task.FromResult(EmailSendResult.Fail(EmailLog.ProviderUnavailable));
            }

            Sent.Add(message);
            return Task.FromResult(EmailSendResult.Ok("op-1"));
        }
    }

    private void Document(string name, string title) =>
        File.WriteAllText(Path.Combine(_docs, name + ".html"), $"<!doctype html><html><head><title>{title}</title></head><body></body></html>");

    private void Notes(string name, params (string Key, string Text)[] notes) =>
        File.WriteAllText(Path.Combine(_docs, name + ".notas.json"),
            "{\"documento\": \"" + name + "\", \"notas\": {" + string.Join(",", notes.Select(n => $"\"{n.Key}\": \"{n.Text}\"")) + "}}");

    private AvisoNotasCj Notice(string? address = Address, bool active = true, bool anyRecipient = false, string[]? allowed = null, string? folder = null) =>
        new(_store, folder ?? _docs, _sender, new EmailSettings(active, "https://dev.example.test/", "es", anyRecipient, allowed ?? [Address]), address, _memory, _warnings.Add);

    [Fact]
    public async Task SinNotas_NoAvisaDeNada()
    {
        Notes("pruebas-2-medicina-indicaciones");
        Notes("pruebas-3-basal-barthel", ("1", "   "));

        Assert.False(await Notice().ProcessAsync(T0, CancellationToken.None));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task UnaNotaNueva_AvisaUnaVez_ConElTituloYElEnlace_PeroSinElTexto()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", SecretNote));
        var notice = Notice();

        Assert.True(await notice.ProcessAsync(T0, CancellationToken.None));

        var mail = Assert.Single(_sender.Sent);
        Assert.Equal(Address, mail.To);
        Assert.Equal("Ingeniería ha dejado notas en 1 documento de ResidApp", mail.Subject);
        Assert.Contains("Guía 2 de pruebas · Medicina e indicaciones", mail.PlainText);
        Assert.Contains("https://dev.example.test/pendientes-cj/pruebas-2-medicina-indicaciones.html", mail.PlainText);
        Assert.DoesNotContain(SecretNote, mail.PlainText + mail.Html + mail.Subject);
        // El asunto y el cuerpo no nombran a nadie (se ven en pantallas de bloqueo); solo el pie dice a quién decírselo para dejar de recibirlo.
        var body = System.Text.RegularExpressions.Regex.Replace(mail.Subject + "\n" + mail.PlainText.Split("—\n")[0], "https?://\\S+", "");
        Assert.DoesNotContain("cj", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orlando", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nunca te pedirá tu clave por correo", mail.PlainText);
        Assert.Contains("nunca te pedirá tu clave por correo", System.Net.WebUtility.HtmlDecode(mail.Html));
        Assert.DoesNotContain("clave de los documentos", mail.PlainText); // nada que parezca «entra y escribe tu clave»

        Assert.False(await notice.ProcessAsync(T0.AddMinutes(1), CancellationToken.None)); // lo mismo no se avisa dos veces
        Assert.False(await Notice().ProcessAsync(T0.AddHours(5), CancellationToken.None));
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task VariosDocumentos_SonUnSoloCorreo_ConTitulosDecodificados()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        Notes("pruebas-3-basal-barthel", ("1", "b"));

        Assert.True(await Notice().ProcessAsync(T0, CancellationToken.None));

        var mail = Assert.Single(_sender.Sent);
        Assert.Equal("Ingeniería ha dejado notas en 2 documentos de ResidApp", mail.Subject);
        Assert.Contains("Guía 3 de pruebas · Basal & Barthel", mail.PlainText);
        Assert.Contains("Basal &amp; Barthel", mail.Html); // codificado para el HTML del correo
        Assert.True(mail.PlainText.IndexOf("Guía 2", StringComparison.Ordinal) < mail.PlainText.IndexOf("Guía 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnaNotaCambiada_AvisaOtraVez_SoloDeEsaYUnaRetiradaNoAvisa()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "primera"));
        Notes("pruebas-3-basal-barthel", ("1", "otra"));
        var notice = Notice();
        await notice.ProcessAsync(T0, CancellationToken.None);

        Notes("pruebas-2-medicina-indicaciones", ("5", "segunda")); // cambia solo la de la guía 2
        Assert.True(await notice.ProcessAsync(T0.AddDays(1), CancellationToken.None));
        Assert.Equal(2, _sender.Sent.Count);
        Assert.Contains("Medicina", _sender.Sent[1].PlainText);
        Assert.DoesNotContain("Barthel", _sender.Sent[1].PlainText);

        Notes("pruebas-2-medicina-indicaciones"); // la retira
        Assert.False(await notice.ProcessAsync(T0.AddDays(2), CancellationToken.None));
        Notes("pruebas-2-medicina-indicaciones", ("5", "segunda")); // y la vuelve a poner igual: es nueva otra vez
        Assert.True(await notice.ProcessAsync(T0.AddDays(3), CancellationToken.None));
    }

    [Fact]
    public async Task ElFormatoDelFichero_NoCuentaComoCambio()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "igual"), ("6", "otra"));
        await Notice().ProcessAsync(T0, CancellationToken.None);

        File.WriteAllText(Path.Combine(_docs, "pruebas-2-medicina-indicaciones.notas.json"),
            "{\r\n  \"notas\": {\r\n    \"6\": \"otra\",\r\n    \"5\": \"  igual \"\r\n  },\r\n  \"documento\": \"pruebas-2-medicina-indicaciones\"\r\n}\r\n");

        Assert.False(await Notice().ProcessAsync(T0.AddHours(1), CancellationToken.None));
    }

    [Fact]
    public async Task SiElEnvioFalla_NoSePierdeElAviso_Y_NoSeReintentaCadaMinuto()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        _sender.Reject = true;
        var notice = Notice();

        Assert.False(await notice.ProcessAsync(T0, CancellationToken.None));
        Assert.Single(_warnings);

        _sender.Reject = false;
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(1), CancellationToken.None)); // aún no han pasado los 10 minutos
        Assert.Empty(_sender.Sent);
        Assert.True(await notice.ProcessAsync(T0.AddMinutes(10), CancellationToken.None));
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task SiElProveedorLanzaUnaExcepcion_SeReintenta_SinEscribirSuMensajeEnElRegistro()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        _sender.Throw = new InvalidOperationException(Address);

        Assert.False(await Notice().ProcessAsync(T0, CancellationToken.None));

        Assert.Single(_warnings);
        Assert.DoesNotContain(Address, _warnings[0]);
        _sender.Throw = null;
        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(10), CancellationToken.None));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(Address, false)]
    public async Task SinDireccionOConElCorreoApagado_NoEnviaNada_Y_NoPierdeElAviso(string? address, bool active)
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));

        Assert.False(await Notice(address, active).ProcessAsync(T0, CancellationToken.None));
        Assert.Empty(_sender.Sent);

        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(1), CancellationToken.None)); // al encenderlo, sigue ahí
    }

    [Fact]
    public async Task UnaDireccionNoPermitida_NoRecibe()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));

        Assert.False(await Notice(allowed: ["otra@example.test"]).ProcessAsync(T0, CancellationToken.None));

        Assert.Empty(_sender.Sent);
        Assert.True(await Notice(anyRecipient: true, allowed: []).ProcessAsync(T0.AddMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task SinLosDocumentosDeCj_ElAvisoEsInerte_ComoEnUnPaqueteDeProduccion()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));

        Assert.False(await Notice(folder: Path.Combine(_docs, "no-existe")).ProcessAsync(T0, CancellationToken.None));

        Assert.Empty(_sender.Sent);
        Assert.False(Directory.Exists(_state) && Directory.EnumerateFileSystemEntries(_state).Any());
    }

    [Fact]
    public async Task ElEstado_GuardaSoloHuellas_NiLaDireccionNiElTextoDeLasNotas()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", SecretNote));

        await Notice().ProcessAsync(T0, CancellationToken.None);

        var state = await File.ReadAllTextAsync(Path.Combine(_state, "avisos-notas.json"));
        Assert.Contains("pruebas-2-medicina-indicaciones", state);
        Assert.DoesNotContain(Address, state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SecretNote, state);
        Assert.DoesNotContain("example.test", state);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("{}")]
    [InlineData("{\"huellas\": null}")]
    public async Task UnEstadoIlegible_SeTrataComoSinEstado_Y_NoRompeLaTarea(string content)
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        Directory.CreateDirectory(_state);
        await File.WriteAllTextAsync(Path.Combine(_state, "avisos-notas.json"), content);

        Assert.True(await Notice().ProcessAsync(T0, CancellationToken.None)); // vuelve a avisar de lo que hay: un correo de más, nunca uno de menos
        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(1), CancellationToken.None));
    }

    [Fact]
    public async Task UnFicheroDeNotasRoto_NoAvisaDeNada_NiRompeLosDemas()
    {
        File.WriteAllText(Path.Combine(_docs, "pruebas-2-medicina-indicaciones.notas.json"), "{ roto");
        Notes("pruebas-3-basal-barthel", ("1", "b"));

        Assert.True(await Notice().ProcessAsync(T0, CancellationToken.None));

        var mail = Assert.Single(_sender.Sent);
        Assert.DoesNotContain("Medicina", mail.PlainText);
    }

    [Fact]
    public async Task ElCorreo_LlevaElMarcoVisualDeLosDemasAvisos()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));

        await Notice().ProcessAsync(T0, CancellationToken.None);

        var mail = Assert.Single(_sender.Sent);
        Assert.Contains("background:#0A58CA", mail.Html);
        Assert.Contains("cid:" + EmailLayout.IconContentId, mail.Html);
        Assert.Equal(EmailLayout.IconContentId, Assert.Single(mail.InlineImages!).ContentId);
        Assert.Contains("prefers-color-scheme: dark", mail.Html);
        Assert.Contains(">https://dev.example.test/pendientes-cj/pruebas-2-medicina-indicaciones.html</a>", mail.Html);
        Assert.Contains("Aviso del entorno de pruebas de ResidApp", mail.Html);
    }

    [Fact]
    public void LasNotasReales_DelRepositorio_SeLeenSinErrores()
    {
        // Los 14 ficheros publicados (hoy sin notas) tienen el formato que la tarea espera.
        var folder = Path.Combine(AppContext.BaseDirectory, "pendientes-cj");

        var (fingerprints, unreadable) = AvisoNotasCj.CurrentFingerprints(folder);

        Assert.True(Directory.GetFiles(folder, "*.notas.json").Length >= 14);
        Assert.Empty(unreadable); // ninguno de los 14 ficheros publicados está roto
        Assert.All(fingerprints.Keys, key => Assert.True(File.Exists(Path.Combine(folder, key[..key.IndexOf('/')] + ".html"))));
    }

    [Fact]
    public void CadaDocumentoConNotas_TieneUnTituloHtml_ParaQueElCorreoLoNombre()
    {
        // El correo nombra cada documento por su <title>; sin él caería al identificador del fichero.
        var folder = Path.Combine(AppContext.BaseDirectory, "pendientes-cj");

        foreach (var notes in Directory.GetFiles(folder, "*.notas.json"))
        {
            var html = File.ReadAllText(Path.ChangeExtension(Path.ChangeExtension(notes, null), "html"));
            Assert.Matches("<title>[^<]+</title>", html);
        }
    }

    [Fact]
    public async Task RetirarUnaNotaDeVarias_NoAvisa_PeroUnaNuevaEnEseDocumentoSi()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"), ("6", "b"));
        await Notice().ProcessAsync(T0, CancellationToken.None);

        Notes("pruebas-2-medicina-indicaciones", ("6", "b")); // se quita la 5 porque ya está implementada
        Assert.False(await Notice().ProcessAsync(T0.AddHours(1), CancellationToken.None));

        Notes("pruebas-2-medicina-indicaciones", ("6", "b"), ("9", "nueva"));
        Assert.True(await Notice().ProcessAsync(T0.AddHours(2), CancellationToken.None));
        Assert.Equal(2, _sender.Sent.Count);
    }

    [Fact]
    public async Task UnFicheroQueSeRompeUnRato_NoHaceOlvidarLoYaAvisado()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        await Notice().ProcessAsync(T0, CancellationToken.None);
        var good = File.ReadAllText(Path.Combine(_docs, "pruebas-2-medicina-indicaciones.notas.json"));

        File.WriteAllText(Path.Combine(_docs, "pruebas-2-medicina-indicaciones.notas.json"), "{ \"notas\": { \"5\": \"a"); // a medio copiar
        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(1), CancellationToken.None));
        File.WriteAllText(Path.Combine(_docs, "pruebas-2-medicina-indicaciones.notas.json"), good); // y ya completo

        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(2), CancellationToken.None)); // no es nuevo: no se avisa otra vez
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task SiFaltaLaCarpetaDeDocumentosUnaVuelta_NoSeVuelveAAvisarDeTodo()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        await Notice().ProcessAsync(T0, CancellationToken.None);

        Assert.False(await Notice(folder: Path.Combine(_docs, "no-existe")).ProcessAsync(T0.AddMinutes(1), CancellationToken.None));

        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(2), CancellationToken.None));
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task SiNoSePuedeAnotarElEstado_DespuesDeEnviar_NoSeReenviaCadaMinuto()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        File.WriteAllText(_state, "esto es un fichero donde debería haber una carpeta"); // la carpeta de estado no se puede crear
        var notice = Notice();

        Assert.True(await notice.ProcessAsync(T0, CancellationToken.None)); // el correo sale
        Assert.Single(_warnings);
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(1), CancellationToken.None));
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(9), CancellationToken.None));

        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task UnFalloRepetido_NoVuelveAAvisarDeLoYaAvisado()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        await Notice().ProcessAsync(T0, CancellationToken.None);
        Notes("pruebas-3-basal-barthel", ("1", "b"));
        _sender.Reject = true;
        var notice = Notice();

        Assert.False(await notice.ProcessAsync(T0.AddMinutes(1), CancellationToken.None));
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(11), CancellationToken.None));
        _sender.Reject = false;
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(15), CancellationToken.None)); // 4 minutos tras el segundo fallo
        Assert.True(await notice.ProcessAsync(T0.AddMinutes(21), CancellationToken.None));

        Assert.Equal(2, _sender.Sent.Count); // el de la guía 2 (antes) y el de la 3 (ahora), sin repetir la 2
        Assert.DoesNotContain("Medicina", _sender.Sent[^1].PlainText);
        Assert.Contains("Barthel", _sender.Sent[^1].PlainText);
    }

    [Fact]
    public async Task UnDocumentoSinHtml_NoEsDeCj_NoAvisa()
    {
        Notes("pruebas-9-no-existe", ("1", "a"));

        Assert.False(await Notice().ProcessAsync(T0, CancellationToken.None));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task LaDireccion_SeCompararSinMayusculasNiEspacios()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));

        Assert.True(await Notice("  " + Address.ToUpperInvariant() + " ").ProcessAsync(T0, CancellationToken.None));

        Assert.Equal(Address.ToUpperInvariant(), Assert.Single(_sender.Sent).To);
    }

    [Fact]
    public async Task LaMismaNota_EnNfcYNfd_NoCuentaComoCambio()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "Corregido: pantalla de valoración"));
        await Notice().ProcessAsync(T0, CancellationToken.None);

        Notes("pruebas-2-medicina-indicaciones", ("5", "Corregido: pantalla de valoración".Normalize(System.Text.NormalizationForm.FormD)));

        Assert.False(await Notice().ProcessAsync(T0.AddHours(1), CancellationToken.None));
    }

    [Fact]
    public async Task UnaNotaConUnSustitutoSueltoUnicode_NoRompeLaTareaNiBloqueaLosDemasDocumentos()
    {
        File.WriteAllText(Path.Combine(_docs, "pruebas-2-medicina-indicaciones.notas.json"), "{\"documento\":\"x\",\"notas\":{\"5\":\"roto \\ud800\"}}");
        Notes("pruebas-3-basal-barthel", ("1", "b"));

        Assert.True(await Notice().ProcessAsync(T0, CancellationToken.None));

        Assert.Contains("Barthel", Assert.Single(_sender.Sent).PlainText);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private AvisoNotasCjWorker Worker(string? notifyTo, string? orlando, string[] allowed, ListLogger<AvisoNotasCjWorker> logger)
    {
        var services = new ServiceCollection().AddSingleton<IEmailSender>(_sender).BuildServiceProvider();
        var values = new Dictionary<string, string?> { ["PendientesCj:CarpetaRespuestas"] = _state, ["PendientesCj:AvisarACj"] = notifyTo, ["PendientesCj:AvisarA"] = orlando };
        var options = Options.Create(new CorreoOptions { Activo = true, CadenaConexion = "endpoint=x", UrlBase = "https://dev.example.test", DireccionesPermitidas = [.. allowed] });
        return new AvisoNotasCjWorker(
            _store, services.GetRequiredService<IServiceScopeFactory>(), options, new ConfigurationBuilder().AddInMemoryCollection(values).Build(), logger,
            TimeProvider.System, TimeSpan.FromMilliseconds(30), _docs);
    }

    [Fact]
    public async Task LaTarea_UsaAvisarACj_NoAvisarA_YMandaUnSoloCorreo()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        var worker = Worker(Address, "orlando.ficticio@example.test", [Address, "orlando.ficticio@example.test"], new ListLogger<AvisoNotasCjWorker>());

        await worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 100 && _sender.Sent.Count == 0; i++)
        {
            await Task.Delay(50);
        }

        await Task.Delay(400); // varias vueltas más: no debe repetirse
        await worker.StopAsync(CancellationToken.None);

        var mail = Assert.Single(_sender.Sent);
        Assert.Equal(Address, mail.To);
    }

    [Fact]
    public async Task LaTarea_ConUnaDireccionNoPermitida_AvisaUnaSolaVezEnElRegistro_SinLaDireccion_YNoEnvia()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        var logger = new ListLogger<AvisoNotasCjWorker>();
        var worker = Worker(Address, null, ["otra@example.test"], logger);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(500); // unas quince vueltas
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_sender.Sent);
        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("example.test", message);
    }

    [Fact]
    public async Task LaTarea_SinDireccion_NoHaceNada()
    {
        Notes("pruebas-2-medicina-indicaciones", ("5", "a"));
        var worker = Worker(null, "orlando.ficticio@example.test", [Address, "orlando.ficticio@example.test"], new ListLogger<AvisoNotasCjWorker>());

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(_sender.Sent); // la dirección de Orlando (AvisarA) no vale para el aviso a CJ
        Assert.False(Directory.Exists(_state) && Directory.EnumerateFileSystemEntries(_state).Any());
    }
}
