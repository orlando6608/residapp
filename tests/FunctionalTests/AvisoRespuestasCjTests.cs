using Microsoft.Extensions.Configuration;
using ResidApp.Application.Ports;
using ResidApp.Web.Correo;
using ResidApp.Web.Security;

namespace ResidApp.FunctionalTests;

/// <summary>El aviso a Orlando de que CJ ha respondido: un solo correo cuando CJ deja de cambiar cosas, sin sus respuestas, y nunca se pierde un aviso por un fallo del envío.
/// Con el almacén real sobre una carpeta temporal y un proveedor de correo falso: ninguna prueba envía nada.</summary>
public sealed class AvisoRespuestasCjTests : IDisposable
{
    private const string Address = "Orlando.Ficticio@example.test";
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "residapp-aviso-cj-" + Guid.NewGuid().ToString("N"));
    private readonly RespuestasCjStore _store;
    private readonly FakeSender _sender = new();
    private readonly List<string> _warnings = [];

    public AvisoRespuestasCjTests() =>
        _store = new RespuestasCjStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PendientesCj:CarpetaRespuestas"] = _folder }).Build());

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool Reject { get; set; }

        public Exception? Throw { get; set; }

        public Func<Task>? DuringSend { get; set; }

        public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            if (DuringSend is not null)
            {
                await DuringSend();
            }

            if (Reject)
            {
                return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
            }

            Sent.Add(message);
            return EmailSendResult.Ok("op-1");
        }
    }

    private AvisoRespuestasCj Notice(string? address = Address, bool active = true, bool anyRecipient = false, string[]? allowed = null) =>
        new(_store, _sender, new EmailSettings(active, "https://dev.example.test/", "es", anyRecipient, allowed ?? [Address]), address, Quiet, _warnings.Add);

    private Task SaveAsync(string document, int answered, DateTimeOffset at, string comment = "x", bool notify = true) =>
        _store.SaveAsync(document, "{\"documento\":\"" + document + "\",\"comentario\":\"" + comment + "\"}", new CambioCj(answered, 13), at, notify, CancellationToken.None);

    [Fact]
    public async Task VariosCambiosSeguidos_SonUnSoloCorreo_DespuesDelSilencio()
    {
        var notice = Notice();
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        await SaveAsync("pruebas-2-medicina-indicaciones", 3, T0.AddMinutes(3));
        await SaveAsync("modulos-base-y-opcionales-cj", 16, T0.AddMinutes(4));

        Assert.False(await notice.ProcessAsync(T0.AddMinutes(5), CancellationToken.None));
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(13), CancellationToken.None)); // CJ cambió hace 9 minutos: sigue respondiendo
        Assert.Empty(_sender.Sent);

        Assert.True(await notice.ProcessAsync(T0.AddMinutes(14), CancellationToken.None));
        var mail = Assert.Single(_sender.Sent);
        Assert.Equal(Address, mail.To);
        Assert.Equal("CJ ha respondido en 2 documentos de ResidApp", mail.Subject);
        Assert.Contains("modulos-base-y-opcionales-cj: 16 de 13 completadas", mail.PlainText);
        Assert.Contains("pruebas-2-medicina-indicaciones: 3 de 13 completadas", mail.PlainText);
        Assert.Contains("https://dev.example.test/pendientes-cj/pruebas-2-medicina-indicaciones.html", mail.PlainText);
        Assert.Contains("<a href=\"https://dev.example.test/pendientes-cj/pruebas-2-medicina-indicaciones.html\">", mail.Html);

        Assert.False(await notice.ProcessAsync(T0.AddMinutes(60), CancellationToken.None)); // ya avisado: nada nuevo
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task UnSoloDocumento_LoDiceEnSingular()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);

        await Notice().ProcessAsync(T0.AddMinutes(10), CancellationToken.None);

        Assert.Equal("CJ ha respondido en 1 documento de ResidApp", Assert.Single(_sender.Sent).Subject);
    }

    [Fact]
    public async Task ElCorreo_NoLleva_LoQueCjHaContestado()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0, comment: "texto-privado-de-cj");

        await Notice().ProcessAsync(T0.AddMinutes(10), CancellationToken.None);

        var mail = Assert.Single(_sender.Sent);
        Assert.DoesNotContain("texto-privado-de-cj", mail.PlainText + mail.Html + mail.Subject);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(Address, false)]
    public async Task SinDireccionOConElCorreoApagado_NoEnviaNada_Y_NoPierdeElAviso(string? address, bool active)
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);

        Assert.False(await Notice(address, active).ProcessAsync(T0.AddMinutes(30), CancellationToken.None));
        Assert.Empty(_sender.Sent);

        // al encenderlo después, el aviso sigue ahí
        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(31), CancellationToken.None));
    }

    [Fact]
    public async Task UnaDireccionNoPermitida_NoRecibe_ConAvisoUnaSolaVez_Y_SinPerderElPendiente()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        var notice = Notice(allowed: ["otra@example.test"]);

        Assert.False(await notice.ProcessAsync(T0.AddMinutes(30), CancellationToken.None));
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(31), CancellationToken.None));

        Assert.Empty(_sender.Sent);
        Assert.Single(_warnings);
        Assert.True(await Notice(anyRecipient: true, allowed: []).ProcessAsync(T0.AddMinutes(32), CancellationToken.None));
    }

    [Fact]
    public async Task LaDireccionPermitida_SeCompara_SinMirarMayusculas()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);

        Assert.True(await Notice(allowed: [Address.ToLowerInvariant()]).ProcessAsync(T0.AddMinutes(10), CancellationToken.None));
    }

    [Fact]
    public async Task SiElEnvioFalla_NoSePierdeElAviso_Y_SeReintentaTrasOtroSilencio()
    {
        var notice = Notice();
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        _sender.Reject = true;

        Assert.False(await notice.ProcessAsync(T0.AddMinutes(10), CancellationToken.None));
        Assert.Single(_warnings);
        Assert.Empty(_sender.Sent);

        _sender.Reject = false;
        Assert.False(await notice.ProcessAsync(T0.AddMinutes(11), CancellationToken.None)); // no se repite cada minuto
        Assert.True(await notice.ProcessAsync(T0.AddMinutes(20), CancellationToken.None));
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task UnCambioDeCjMientrasSeEnvia_NoSePierde_ParaElSiguienteAviso()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        _sender.DuringSend = () => SaveAsync("pruebas-2-medicina-indicaciones", 5, T0.AddMinutes(11)); // CJ sigue respondiendo mientras sale el correo

        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(10), CancellationToken.None));
        _sender.DuringSend = null;

        Assert.Contains("1 de 13 completadas", Assert.Single(_sender.Sent).PlainText);
        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(15), CancellationToken.None)); // aún no ha pasado el silencio del último cambio
        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(21), CancellationToken.None));
        Assert.Contains("5 de 13 completadas", _sender.Sent[1].PlainText);
    }

    [Fact]
    public async Task SiLaAplicacionSePara_DuranteElEnvio_ElAvisoSigueAhi()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        _sender.Throw = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Notice().ProcessAsync(T0.AddMinutes(10), CancellationToken.None));

        _sender.Throw = null;
        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(11), CancellationToken.None));
    }

    [Fact]
    public async Task SiElProveedorLanzaUnaExcepcion_SeReintentaTrasOtroSilencio()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        _sender.Throw = new InvalidOperationException("secreto@example.test");

        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(10), CancellationToken.None));

        Assert.Single(_warnings);
        Assert.DoesNotContain("secreto@example.test", _warnings[0]); // nunca el mensaje de la excepción (puede llevar la dirección)
        _sender.Throw = null;
        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(11), CancellationToken.None));
        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(20), CancellationToken.None));
    }

    [Fact]
    public async Task UnaCargaDeRespuestasAntiguas_NoAvisaAOrlando()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 13, T0, notify: false);

        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(60), CancellationToken.None));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task TrasUnReinicio_ElAvisoPendienteSigueAhi()
    {
        await SaveAsync("pruebas-2-medicina-indicaciones", 1, T0);
        var restarted = new RespuestasCjStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PendientesCj:CarpetaRespuestas"] = _folder }).Build());

        var notice = new AvisoRespuestasCj(restarted, _sender, new EmailSettings(true, "https://dev.example.test/", "es", false, [Address]), Address, Quiet, _warnings.Add);

        Assert.True(await notice.ProcessAsync(T0.AddMinutes(10), CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"documentos\": null}")]
    [InlineData("esto no es json")]
    public async Task UnEstadoPendienteIlegible_NoRompeLaTarea(string content)
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, "aviso-pendiente.json"), content);

        Assert.False(await Notice().ProcessAsync(T0.AddMinutes(60), CancellationToken.None));

        await SaveAsync("pruebas-2-medicina-indicaciones", 2, T0.AddMinutes(61)); // y el siguiente guardado lo arregla
        Assert.True(await Notice().ProcessAsync(T0.AddMinutes(71), CancellationToken.None));
    }
}
