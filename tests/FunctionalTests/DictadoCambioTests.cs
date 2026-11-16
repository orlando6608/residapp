using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Infrastructure.Persistence;
using ResidApp.Shared;
using static ResidApp.FunctionalTests.ResidentsFlowTests;

namespace ResidApp.FunctionalTests;

/// <summary>El registro técnico de fallos (hallazgo A4) se conecta al runner con un único observador estático: cada servidor de pruebas que arranca lo
/// sustituye por el suyo, y xUnit arranca varios a la vez. En producción hay un solo servidor y no pasa. Una prueba que mira lo que ese registro escribe
/// necesita que ningún otro servidor se cree mientras corre, así que su clase va en una colección que no se ejecuta en paralelo con las demás.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RegistroTecnicoSinParalelismo
{
    public const string Name = "RegistroTecnicoSinParalelismo";
}

/// <summary>El dictado de «Registrar cambio» (ADR 0009 y 0011) por HTTP, con un proveedor de IA falso: solo el servidor, sin pantalla. Sin clave de
/// proveedor responde 404; con ella, exige sesión, token antiforgery, perfil Auxiliar y residente asignado, limita por cuenta, audita y nunca
/// filtra lo que falle en el proveedor.</summary>
[Collection(RegistroTecnicoSinParalelismo.Name)]
public class DictadoCambioTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Token = "__RequestVerificationToken";

    private static string ExtractValue(string html, string inputName) =>
        Regex.Match(html, $"name=\"{Regex.Escape(inputName)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;

    private async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> app, string subject)
    {
        var client = app.CreateClient();
        (await SesionTests.PostLoginAsync(client, subject)).EnsureSuccessStatusCode();
        return client;
    }

    private sealed record Seed(string Auxiliar, string OtroAuxiliar, string Enfermeria, Guid Resident, Guid AuxiliarAccount, Guid Center);

    private static async Task<Seed> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var adminId = Guid.NewGuid();
        var auxId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var nurseId = Guid.NewGuid();
        var centerId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var adminScope = Guid.NewGuid();
        var auxScope = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        var nurseScope = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var factoryConnection = new SqlConnectionFactory(WebAppFactory.TestConnectionString);
        using (var connection = await factoryConnection.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.cuentas (id, sujeto_externo, estado, creado_en)
                VALUES (@adminId, @admin, 'ACTIVE', @now), (@auxId, @aux, 'ACTIVE', @now), (@otherId, @other, 'ACTIVE', @now), (@nurseId, @nurse, 'ACTIVE', @now);
                INSERT INTO dbo.centros (id, codigo, nombre_visible, estado, creado_en) VALUES (@centerId, @code, @code, 'ACTIVE', @now);
                INSERT INTO dbo.unidades (id, centro_id, codigo, nombre_visible, estado, creado_en) VALUES (@unitId, @centerId, @unitCode, @unitCode, 'ACTIVE', @now);
                INSERT INTO dbo.ambitos_perfil (id, cuenta_id, centro_id, perfil_codigo, estado, concedido_en, concedido_por_cuenta_id)
                VALUES (@adminScope, @adminId, @centerId, 'ADMINISTRACION', 'ACTIVE', @now, @adminId),
                       (@auxScope, @auxId, @centerId, 'AUXILIAR', 'ACTIVE', @now, @adminId),
                       (@otherScope, @otherId, @centerId, 'AUXILIAR', 'ACTIVE', @now, @adminId),
                       (@nurseScope, @nurseId, @centerId, 'ENFERMERIA', 'ACTIVE', @now, @adminId);
                INSERT INTO dbo.ambitos_perfil_unidad (id, ambito_perfil_id, centro_id, unidad_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @adminScope, @centerId, @unitId, @now, @adminId), (NEWID(), @auxScope, @centerId, @unitId, @now, @adminId),
                       (NEWID(), @otherScope, @centerId, @unitId, @now, @adminId), (NEWID(), @nurseScope, @centerId, @unitId, @now, @adminId);
                """, new
            {
                adminId, auxId, otherId, nurseId, centerId, unitId, adminScope, auxScope, otherScope, nurseScope, now,
                admin = $"dictado-adm-{suffix}", aux = $"dictado-aux-{suffix}", other = $"dictado-aux2-{suffix}", nurse = $"dictado-enf-{suffix}",
                code = $"DICTADO-CENTER-{suffix}", unitCode = $"DICTADO-UNIT-{suffix}",
            });
        }

        var resident = await new SqlResidentRepository(factoryConnection).CreateWithInitialLocationAsync(new CreateResidentInput(
            AccountId.From(adminId), SystemProfile.Administracion, CenterId.From(centerId), UnitId.From(unitId),
            "Residente Dictado Funcional", new DateOnly(1949, 9, 9), DocumentedSexCode.Hombre, null, null, null, null, null, Guid.NewGuid()));
        using (var connection = await factoryConnection.OpenAsync())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ambitos_perfil_residente (id, ambito_perfil_id, centro_id, residente_id, concedido_en, concedido_por_cuenta_id)
                VALUES (NEWID(), @auxScope, @centerId, @residentId, SYSUTCDATETIME(), @adminId)
                """, new { auxScope, centerId, residentId = resident.ResidentId.Value, adminId });
        }
        return new Seed($"dictado-aux-{suffix}", $"dictado-aux2-{suffix}", $"dictado-enf-{suffix}", resident.ResidentId.Value, auxId, centerId);
    }

    /// <summary>La aplicación con la clave puesta (el dictado activo) y el proveedor falso en lugar de Mistral.</summary>
    private WebApplicationFactory<Program> AppWith(FakeVoice voice, int requestsPerMinute = 100, bool allModules = true) => factory.WithWebHostBuilder(builder =>
    {
        // allModules = false: como un centro real, el módulo solo lo tiene quien tiene filas en modulos_centro (en desarrollo, appsettings.Development.json los activa todos).
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Modulos:ActivarTodos"] = allModules ? "true" : "false" }));
        builder.UseSetting("IA:Mistral:ApiKey", "clave-de-prueba");
        builder.UseSetting("IA:PeticionesPorMinuto", requestsPerMinute.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ISpeechTranscriber>(voice);
            services.AddSingleton<IStructuredGenerator>(voice);
        });
    });

    private static async Task<string> TokenAsync(HttpClient client, string path) => ExtractValue(await client.GetStringAsync(path), Token);

    private static async Task<HttpResponseMessage> DictarAsync(
        HttpClient client, Guid resident, string? token, string contentType = "audio/webm;codecs=opus", bool withAudio = true)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(resident.ToString()), "residenteId" },
            { new StringContent(Guid.NewGuid().ToString()), "operacionId" },
        };
        if (withAudio)
        {
            var audio = new ByteArrayContent([1, 2, 3, 4, 5]);
            audio.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(audio, "audio", "dictado.webm");
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Auxiliar/DictarCambio") { Content = form };
        if (token is not null)
        {
            request.Headers.Add("RequestVerificationToken", token);
        }
        return await client.SendAsync(request);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(string Category, LogLevel Level, string Text)> _lines = new();

        public IReadOnlyCollection<(string Category, LogLevel Level, string Text)> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, System.Collections.Concurrent.ConcurrentQueue<(string, LogLevel, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }

    private static FakeVoice Voice() => new()
    {
        Transcript = "hoy no ha querido comer nada",
        Output = new VoiceModelOutput([new VoiceModelArea(
            DailyChangeAreaCode.AlimentacionHidratacion.ToCode(), "no ha querido comer nada", [DailyChangeAreaOptionCode.NulaIngesta.ToCode()])], 38.5m),
    };

    private static async Task<int> AuditRowsAsync(Guid account)
    {
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.eventos_auditoria WITH (NOLOCK) WHERE cuenta_id = @account AND accion_codigo = 'VOICE_DICTATION_USED'", new { account });
    }

    [Fact]
    public async Task ConElResidenteAsignado_DevuelveLaSugerencia_ConSoloLosCamposPrevistos_YDejaUnaFilaDeAuditoria()
    {
        var seed = await SeedAsync();
        var voice = Voice();
        using var app = AppWith(voice);
        var auxiliar = await LoginAsync(app, seed.Auxiliar);
        var token = await TokenAsync(auxiliar, $"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");

        var response = await DictarAsync(auxiliar, seed.Resident, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        Assert.Equal(["areas", "temperatura", "transcripcion"], json.Select(p => p.Key).Order().ToList());
        Assert.Equal("hoy no ha querido comer nada", json["transcripcion"]!.GetValue<string>());
        Assert.Equal(38.5m, json["temperatura"]!.GetValue<decimal>());
        var area = Assert.Single(json["areas"]!.AsArray())!.AsObject();
        Assert.Equal(["area", "opciones", "texto"], area.Select(p => p.Key).Order().ToList());
        Assert.Equal("ALIMENTACION_HIDRATACION", area["area"]!.GetValue<string>());
        Assert.Equal(DailyChangeAreaOptionCode.NulaIngesta.ToCode(), area["opciones"]![0]!.GetValue<string>());
        Assert.Equal(["transcribir", "estructurar"], voice.Calls);
        Assert.Equal(1, await AuditRowsAsync(seed.AuxiliarAccount));
    }

    [Fact]
    public async Task LaPantallaDeRegistrarCambio_OfreceElDictadoSoloConElModuloYLaClave_ConElAvisoDeNoDecirNombres()
    {
        var seed = await SeedAsync();
        using var app = AppWith(Voice());
        var conClave = await LoginAsync(app, seed.Auxiliar);
        var sinClave = await LoginAsync(factory, seed.Auxiliar);

        var activa = await conClave.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");
        var desactivada = await sinClave.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");

        Assert.Contains("id=\"dictado\"", activa);
        Assert.Contains($"data-residente=\"{seed.Resident}\"", activa);
        Assert.Contains("data-url=\"/Auxiliar/DictarCambio\"", activa);
        Assert.Contains("No digas nombres de personas.", activa);
        Assert.Contains("Sugerido por voz", activa);
        Assert.Contains("/js/dictado.js", activa);
        Assert.Contains("id=\"registrar-continuar\"", activa);
        // El bloque va oculto hasta que dictado.js comprueba que el navegador puede grabar: sin JavaScript, el formulario manual no cambia.
        Assert.Matches("<section[^>]*id=\"dictado\"[^>]*hidden", activa);
        Assert.DoesNotContain("id=\"dictado\"", desactivada);
        Assert.DoesNotContain("dictado.js", desactivada);
        Assert.DoesNotContain("No digas nombres", desactivada);
        Assert.Contains("id=\"registrar-continuar\"", desactivada);
    }

    [Fact]
    public async Task SinLaClaveDelProveedor_ElDictadoEstaDesactivado_Y404()
    {
        var seed = await SeedAsync();
        var auxiliar = await LoginAsync(factory, seed.Auxiliar);
        var token = await TokenAsync(auxiliar, $"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");

        var response = await DictarAsync(auxiliar, seed.Resident, token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await AuditRowsAsync(seed.AuxiliarAccount));
    }

    /// <summary>Un cambio de módulo escrito como lo dejaría Plataforma (aquí como db_owner, que salta la política): alta con vigencia o baja.</summary>
    private static async Task InsertModuleAsync(Seed seed, int number, bool enable, int fromDays = -1, int untilDays = 30)
    {
        using var connection = await new SqlConnectionFactory(WebAppFactory.TestConnectionString).OpenAsync();
        var today = DateTime.UtcNow.Date;
        await connection.ExecuteAsync("""
            INSERT INTO dbo.modulos_centro
                (id, centro_id, modulo_codigo, numero, estado_codigo, vigente_desde, vigente_hasta, referencia_anexo, version_anexo, version_ficha, condiciones_codigos,
                 motivo_baja_codigo, solicitud_recibida_en, canal_codigo, referencia_solicitud, cambiado_por_cuenta_id, cambiado_por_perfil, cambiado_en)
            VALUES (NEWID(), @center, 'DICTADO_VOZ', @number, @state, @from, @until, CASE WHEN @enable = 1 THEN 'ANX-1' END, CASE WHEN @enable = 1 THEN '1' END,
                    CASE WHEN @enable = 1 THEN '1' END, CASE WHEN @enable = 1 THEN 'A' END, CASE WHEN @enable = 0 THEN 'PETICION_CENTRO' END,
                    SYSUTCDATETIME(), 'CORREO', 'SOL-1', @actor, 'PLATAFORMA', SYSUTCDATETIME())
            """, new
        {
            center = seed.Center, number, enable, state = enable ? "ACTIVE" : "DISABLED", actor = seed.AuxiliarAccount,
            from = enable ? today.AddDays(fromDays) : (DateTime?)null, until = enable ? today.AddDays(untilDays) : (DateTime?)null,
        });
    }

    /// <summary>ADR 0015: sin el módulo en el centro (nunca activado, apagado o vencido) el servidor responde 404 con la clave puesta, sin auditar y sin una sola llamada al proveedor, y la pantalla no lo ofrece.</summary>
    [Fact]
    public async Task ConLaClaveYSinElModuloEnElCentro_NiNuncaActivado_NiApagado_NiVencido_Responde404_SinAuditarNiLlamarAlProveedor()
    {
        var seed = await SeedAsync();
        var voice = Voice();
        using var app = AppWith(voice, allModules: false);
        var auxiliar = await LoginAsync(app, seed.Auxiliar);
        var token = await TokenAsync(auxiliar, $"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");

        // 1. Nunca activado.
        Assert.Equal(HttpStatusCode.NotFound, (await DictarAsync(auxiliar, seed.Resident, token)).StatusCode);
        Assert.DoesNotContain("id=\"dictado\"", await auxiliar.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}"));

        // 2. Vencido (alta cuya vigencia acabó ayer).
        await InsertModuleAsync(seed, 1, true, fromDays: -30, untilDays: -1);
        Assert.Equal(HttpStatusCode.NotFound, (await DictarAsync(auxiliar, seed.Resident, token)).StatusCode);

        // 3. Activado y después apagado: el efecto es inmediato, en la petición siguiente.
        await InsertModuleAsync(seed, 2, true);
        Assert.Equal(HttpStatusCode.OK, (await DictarAsync(auxiliar, seed.Resident, token)).StatusCode);
        var callsWhileOn = voice.Calls.Count;
        await InsertModuleAsync(seed, 3, false);
        Assert.Equal(HttpStatusCode.NotFound, (await DictarAsync(auxiliar, seed.Resident, token)).StatusCode);
        Assert.DoesNotContain("id=\"dictado\"", await auxiliar.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}"));

        Assert.Equal(2, callsWhileOn);
        Assert.Equal(callsWhileOn, voice.Calls.Count);
        Assert.Equal(1, await AuditRowsAsync(seed.AuxiliarAccount));
    }

    [Fact]
    public async Task ConElModuloActivoEnElCentro_LaPantallaLoOfreceYElDictadoFunciona_PeroOtroCentroSinModuloNo()
    {
        var seed = await SeedAsync();
        var other = await SeedAsync();
        await InsertModuleAsync(seed, 1, true);
        var voice = Voice();
        using var app = AppWith(voice, allModules: false);
        var auxiliar = await LoginAsync(app, seed.Auxiliar);
        var otherAuxiliar = await LoginAsync(app, other.Auxiliar);

        var screen = await auxiliar.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");
        var otherScreen = await otherAuxiliar.GetStringAsync($"/Auxiliar/RegistrarCambio?residenteId={other.Resident}");
        var token = ExtractValue(screen, Token);
        var otherToken = ExtractValue(otherScreen, Token);

        Assert.Contains("id=\"dictado\"", screen);
        Assert.DoesNotContain("id=\"dictado\"", otherScreen);
        Assert.Equal(HttpStatusCode.OK, (await DictarAsync(auxiliar, seed.Resident, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DictarAsync(otherAuxiliar, other.Resident, otherToken)).StatusCode);
        Assert.Equal(1, await AuditRowsAsync(seed.AuxiliarAccount));
        Assert.Equal(0, await AuditRowsAsync(other.AuxiliarAccount));
    }

    [Fact]
    public async Task SinTokenAntiforgery_Responde400_SinAuditarNiLlamarAlProveedor()
    {
        var seed = await SeedAsync();
        var voice = Voice();
        using var app = AppWith(voice);
        var auxiliar = await LoginAsync(app, seed.Auxiliar);

        var response = await DictarAsync(auxiliar, seed.Resident, token: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(voice.Calls);
        Assert.Equal(0, await AuditRowsAsync(seed.AuxiliarAccount));
    }

    [Fact]
    public async Task SinSesion_NoLlegaAlDictado()
    {
        var voice = Voice();
        using var app = AppWith(voice);
        var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await DictarAsync(anonymous, Guid.NewGuid(), token: null);

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, response.StatusCode.ToString());
        Assert.Empty(voice.Calls);
    }

    [Fact]
    public async Task ConOtroPerfilOUnResidenteNoAsignado_Responde403_SinAuditarNiLlamarAlProveedor()
    {
        var seed = await SeedAsync();
        var voice = Voice();
        using var app = AppWith(voice);
        var nurse = await LoginAsync(app, seed.Enfermeria);
        var other = await LoginAsync(app, seed.OtroAuxiliar);

        var asNurse = await DictarAsync(nurse, seed.Resident, await TokenAsync(nurse, "/"));
        var notAssigned = await DictarAsync(other, seed.Resident, await TokenAsync(other, "/"));

        Assert.Equal(HttpStatusCode.Forbidden, asNurse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notAssigned.StatusCode);
        Assert.Empty(voice.Calls);
        Assert.Equal(0, await AuditRowsAsync(seed.AuxiliarAccount));
    }

    [Fact]
    public async Task UnAccesoDenegado_DejaEnElRegistroSuCodigo_LaSesionYElCentro_ConLaAppEntera()
    {
        // Hallazgo A4: el runner convierte la denegación en un 403 sin más; el registro técnico es lo único que deja rastro.
        var seed = await SeedAsync();
        var capture = new CapturingLoggerProvider();
        using var app = AppWith(Voice()).WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        var other = await LoginAsync(app, seed.OtroAuxiliar);

        var response = await DictarAsync(other, seed.Resident, await TokenAsync(other, "/"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var line = Assert.Single(capture.Lines, l => l.Category == "ResidApp.Fallos" && l.Level == LogLevel.Warning).Text;
        Assert.Matches("Acceso denegado: [A-Z][A-Z0-9_]+ en .+ \\(sesión [0-9a-f-]{36}, centro declarado [0-9a-f-]{36}\\)\\.", line);
        Assert.DoesNotContain(seed.Resident.ToString(), line);
        Assert.DoesNotContain(seed.OtroAuxiliar, line);
    }

    [Fact]
    public async Task ElLimiteSeAplicaPorCuenta_NoPorDireccionIp()
    {
        var seed = await SeedAsync();
        using var app = AppWith(Voice(), requestsPerMinute: 2);
        var first = await LoginAsync(app, seed.Auxiliar);
        var second = await LoginAsync(app, seed.OtroAuxiliar);
        var token = await TokenAsync(first, $"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");
        var otherToken = await TokenAsync(second, "/");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await DictarAsync(first, seed.Resident, token)).StatusCode);
        }
        // La otra cuenta tiene su propio cupo: llega al caso de uso y se le niega por no tener asignado al residente (403), no por el límite (429).
        var other = await DictarAsync(second, seed.Resident, otherToken);

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    [Fact]
    public async Task SiElProveedorFalla_Responde503_SinFiltrarDetalles()
    {
        var seed = await SeedAsync();
        using var app = AppWith(new FakeVoice { Fails = true });
        var auxiliar = await LoginAsync(app, seed.Auxiliar);
        var token = await TokenAsync(auxiliar, $"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");

        var response = await DictarAsync(auxiliar, seed.Resident, token);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("rellenar el formulario a mano", body);
        Assert.DoesNotContain("VOICE", body);
        Assert.DoesNotContain("Mistral", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("audio/webm", false)]
    public async Task ConUnFicheroQueNoEsAudioOSinFichero_Responde400_SinLlamarAlProveedor(string contentType, bool withAudio)
    {
        var seed = await SeedAsync();
        var voice = Voice();
        using var app = AppWith(voice);
        var auxiliar = await LoginAsync(app, seed.Auxiliar);
        var token = await TokenAsync(auxiliar, $"/Auxiliar/RegistrarCambio?residenteId={seed.Resident}");

        var response = await DictarAsync(auxiliar, seed.Resident, token, contentType, withAudio);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(voice.Calls);
    }

    private sealed class FakeVoice : ISpeechTranscriber, IStructuredGenerator
    {
        public string Transcript { get; set; } = "";

        public object? Output { get; set; }

        public bool Fails { get; set; }

        public List<string> Calls { get; } = [];

        public Task<string> TranscribeAsync(Stream audio, string contentType, string language, CancellationToken cancellationToken)
        {
            Calls.Add("transcribir");
            return Fails ? throw new InvalidOperationException("VOICE_DICTATION_UNAVAILABLE: Mistral") : Task.FromResult(Transcript);
        }

        public Task<T?> GenerateAsync<T>(string promptName, string input, JsonObject schema, CancellationToken cancellationToken) where T : class
        {
            Calls.Add("estructurar");
            return Task.FromResult(Output as T);
        }
    }
}
