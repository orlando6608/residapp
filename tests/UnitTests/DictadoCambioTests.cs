using System.Text.Json.Nodes;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Dictado de «Registrar cambio» (ADR 0009 y 0011): el esquema que se le da al modelo, el saneado de lo que contesta y el caso de uso con
/// proveedores falsos. Ninguna prueba llama a internet.</summary>
public class DictadoCambioTests
{
    private static string Area(DailyChangeAreaCode area) => area.ToCode();

    private static string Option(DailyChangeAreaOptionCode option) => option.ToCode();

    // ---- esquema ----

    [Fact]
    public void Esquema_TieneCadaAreaConSoloSusOpciones_YLaTemperaturaComoNumeroONulo()
    {
        var schema = DailyChangeDictationSchema.Build();
        var alternativas = schema["properties"]!["areas"]!["items"]!["anyOf"]!.AsArray();

        Assert.Equal(Enum.GetValues<DailyChangeAreaCode>().Length, alternativas.Count);
        foreach (var area in Enum.GetValues<DailyChangeAreaCode>())
        {
            var alternativa = alternativas.Single(a => a!["properties"]!["area"]!["enum"]![0]!.GetValue<string>() == Area(area))!;
            var esperadas = DailyChangeAreaOptionsCatalog.OptionsFor(area).Select(Option).Order().ToList();
            var opciones = alternativa["properties"]!["opciones"]!;
            if (esperadas.Count == 0)
            {
                Assert.Equal(0, opciones["maxItems"]!.GetValue<int>());
            }
            else
            {
                Assert.Equal(esperadas, opciones["items"]!["enum"]!.AsArray().Select(o => o!.GetValue<string>()).Order().ToList());
            }
            Assert.False(alternativa["additionalProperties"]!.GetValue<bool>());
        }

        // CJ fijó el rango el 2026-10-25: el modelo puede devolver la temperatura (obligatoria en el esquema estricto, nula si no se dijo).
        Assert.Contains("temperatura", schema["required"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Equal(["number", "null"], schema["properties"]!["temperatura"]!["type"]!.AsArray().Select(t => t!.GetValue<string>()));
    }

    [Theory]
    [InlineData("38.5", "38.5")]
    [InlineData("37.84", "37.8")]
    [InlineData("34.0", "34.0")]
    [InlineData("42.0", "42.0")]
    [InlineData("378", null)]
    [InlineData("33.9", null)]
    [InlineData("42.1", null)]
    public void Saneado_LaTemperaturaSoloSeSugiereEnElRangoDeCJ_RedondeadaAUnaDecima(string dicha, string? sugerida)
    {
        var resultado = SuggestDailyChangeFromVoice.SanitizeTemperature(decimal.Parse(dicha, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(sugerida is null ? null : decimal.Parse(sugerida, System.Globalization.CultureInfo.InvariantCulture), resultado);
        Assert.Null(SuggestDailyChangeFromVoice.SanitizeTemperature(null));
    }

    // ---- saneado ----

    private static VoiceModelOutput Output(params VoiceModelArea[] areas) => new([.. areas]);

    [Fact]
    public void Saneado_QuitaLaOpcionQueNoEsDelArea_YDescartaElAreaSiNoQuedaNada()
    {
        var resultado = SuggestDailyChangeFromVoice.Sanitize(Output(
            new VoiceModelArea(Area(DailyChangeAreaCode.AlimentacionHidratacion), null, [Option(DailyChangeAreaOptionCode.Upp)]),
            new VoiceModelArea(Area(DailyChangeAreaCode.LesionesPiel), "sacro enrojecido",
                [Option(DailyChangeAreaOptionCode.Upp), Option(DailyChangeAreaOptionCode.NulaIngesta)])));

        var area = Assert.Single(resultado);
        Assert.Equal(DailyChangeAreaCode.LesionesPiel, area.Area);
        Assert.Equal([DailyChangeAreaOptionCode.Upp], area.Options);
        Assert.Equal("sacro enrojecido", area.Text);
    }

    [Fact]
    public void Saneado_IgnoraLasAreasYOpcionesDesconocidas_YLasQueVienenNulas()
    {
        Assert.Empty(SuggestDailyChangeFromVoice.Sanitize(new VoiceModelOutput(null)));
        Assert.Empty(SuggestDailyChangeFromVoice.Sanitize(Output(
            new VoiceModelArea("TEMPERATURA", "37", ["FIEBRE"]),
            new VoiceModelArea(null, "algo", null),
            new VoiceModelArea(Area(DailyChangeAreaCode.Sueno), "   ", ["NO_EXISTE"]))));
    }

    [Fact]
    public void Saneado_UneLasAreasRepetidas_SinDuplicarOpciones()
    {
        var resultado = SuggestDailyChangeFromVoice.Sanitize(Output(
            new VoiceModelArea(Area(DailyChangeAreaCode.HecesDiuresis), "poca orina", [Option(DailyChangeAreaOptionCode.DisminucionDiuresis)]),
            new VoiceModelArea(Area(DailyChangeAreaCode.HecesDiuresis), "y diarrea",
                [Option(DailyChangeAreaOptionCode.Diarrea), Option(DailyChangeAreaOptionCode.DisminucionDiuresis)])));

        var area = Assert.Single(resultado);
        Assert.Equal([DailyChangeAreaOptionCode.DisminucionDiuresis, DailyChangeAreaOptionCode.Diarrea], area.Options);
        Assert.Equal("poca orina y diarrea", area.Text);
    }

    [Fact]
    public void Saneado_EnUnAreaSinListaRapida_SoloQuedaElTexto_YSeRecortaAlLimiteDelRegistro()
    {
        var largo = new string('a', SuggestDailyChangeFromVoice.MaxAreaTextLength + 50);
        var resultado = SuggestDailyChangeFromVoice.Sanitize(Output(
            new VoiceModelArea(Area(DailyChangeAreaCode.IncidenciasCaidas), largo, [Option(DailyChangeAreaOptionCode.Upp)])));

        var area = Assert.Single(resultado);
        Assert.Empty(area.Options);
        Assert.Equal(SuggestDailyChangeFromVoice.MaxAreaTextLength, area.Text!.Length);
    }

    // ---- caso de uso ----

    private static readonly Guid ScopeId = Guid.NewGuid();
    private static readonly CenterId Center = CenterId.New();
    private static readonly ResidentId Resident = ResidentId.New();

    private static SuggestDailyChangeFromVoiceCommand Command(
        Stream? audio = null, long? bytes = null, string contentType = "audio/webm;codecs=opus", ResidentId? resident = null)
    {
        var data = audio ?? new MemoryStream([1, 2, 3, 4]);
        return new SuggestDailyChangeFromVoiceCommand(ScopeId, Center, resident ?? Resident, data, bytes ?? 4, contentType, Guid.NewGuid());
    }

    private static (SuggestDailyChangeFromVoice UseCase, Fakes Fakes) Build(
        SystemProfile profile = SystemProfile.Auxiliar, bool assigned = true, bool signedIn = true, bool auditFails = false)
    {
        var fakes = new Fakes { AuditFails = auditFails };
        var scopes = new FakeScopes(signedIn ? [new ActiveProfileScope(ScopeId, AccountId.New(), Center, "Centro", profile)] : []);
        var residents = new FakeResidents(assigned ? [new AssignedResidentSummary(Resident, "Residente", UnitId.New(), "Unidad", false, false)] : []);
        return (new SuggestDailyChangeFromVoice(scopes, residents, new FakeSession(signedIn), fakes, fakes, fakes), fakes);
    }

    [Fact]
    public async Task Sugerir_AuditaAntesDeTranscribir_YDevuelveLaSugerenciaSaneada()
    {
        var (useCase, fakes) = Build();
        fakes.Transcript = "no quiere comer nada";
        fakes.Output = Output(new VoiceModelArea(Area(DailyChangeAreaCode.AlimentacionHidratacion), null, [Option(DailyChangeAreaOptionCode.NulaIngesta)]));

        var result = await useCase.ExecuteAsync(Command());

        Assert.True(result.Ok);
        Assert.Equal(["auditar", "transcribir:audio/webm", "estructurar"], fakes.Calls);
        Assert.Equal("no quiere comer nada", result.Value!.Transcript);
        Assert.Equal([DailyChangeAreaOptionCode.NulaIngesta], Assert.Single(result.Value.Areas).Options);
        Assert.Equal("registrar-cambio", fakes.PromptName);
    }

    [Fact]
    public async Task Sugerir_SiLaAuditoriaFalla_NoSaleNadaHaciaElProveedor()
    {
        var (useCase, fakes) = Build(auditFails: true);

        var result = await useCase.ExecuteAsync(Command());

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.Unavailable, result.Error!.Code);
        Assert.Equal(["auditar"], fakes.Calls);
    }

    [Theory]
    [InlineData(SystemProfile.Enfermeria, true, true)]
    [InlineData(SystemProfile.Auxiliar, false, true)]
    [InlineData(SystemProfile.Auxiliar, true, false)]
    public async Task Sugerir_SinPerfilAuxiliar_ResidenteAsignadoOSesion_EsAccesoDenegado_SinAuditarNiLlamarAlProveedor(
        SystemProfile profile, bool assigned, bool signedIn)
    {
        var (useCase, fakes) = Build(profile, assigned, signedIn);

        var result = await useCase.ExecuteAsync(Command());

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.AccessDenied, result.Error!.Code);
        Assert.Empty(fakes.Calls);
    }

    [Theory]
    [InlineData(0L, "audio/webm")]
    [InlineData(SuggestDailyChangeFromVoice.MaxAudioBytes + 1, "audio/webm")]
    [InlineData(4L, "text/plain")]
    [InlineData(4L, "")]
    public async Task Sugerir_ConUnAudioVacioGrandeOQueNoEsAudio_EsEntradaInvalida_SinAuditar(long bytes, string contentType)
    {
        var (useCase, fakes) = Build();

        var result = await useCase.ExecuteAsync(Command(bytes: bytes, contentType: contentType));

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.InvalidInput, result.Error!.Code);
        Assert.Empty(fakes.Calls);
    }

    [Fact]
    public async Task Sugerir_SiElModeloNoContestaConElEsquema_NoEstaDisponible_YNoDaDetalles()
    {
        var (useCase, fakes) = Build();
        fakes.Output = null;

        var result = await useCase.ExecuteAsync(Command());

        Assert.False(result.Ok);
        Assert.Equal(ApplicationFailureCode.Unavailable, result.Error!.Code);
        Assert.DoesNotContain("VOICE", result.Error.Message);
    }

    private sealed class Fakes : IVoiceDictationAudit, ISpeechTranscriber, IStructuredGenerator
    {
        public List<string> Calls { get; } = [];

        public string Transcript { get; set; } = "";

        public VoiceModelOutput? Output { get; set; } = new([]);

        public bool AuditFails { get; set; }

        public string? PromptName { get; private set; }

        public Task RecordAsync(VoiceDictationAuditInput input, CancellationToken ct = default)
        {
            Calls.Add("auditar");
            return AuditFails ? throw new InvalidOperationException("sin base de datos") : Task.CompletedTask;
        }

        public Task<string> TranscribeAsync(Stream audio, string contentType, string language, CancellationToken cancellationToken)
        {
            Calls.Add($"transcribir:{contentType}");
            return Task.FromResult(Transcript);
        }

        public Task<T?> GenerateAsync<T>(string promptName, string input, JsonObject schema, CancellationToken cancellationToken) where T : class
        {
            Calls.Add("estructurar");
            PromptName = promptName;
            return Task.FromResult(Output as T);
        }
    }

    private sealed class FakeSession(bool signedIn) : ISessionIdentityProvider
    {
        public Task<VerifiedIdentity?> GetVerifiedIdentityAsync(CancellationToken ct = default) =>
            Task.FromResult(signedIn ? new VerifiedIdentity("auxiliar-prueba") : null);
    }

    private sealed class FakeScopes(IReadOnlyList<ActiveProfileScope> scopes) : IProfileScopeDirectoryProvider
    {
        public Task<IReadOnlyList<ActiveProfileScope>> ListActiveAsync(string externalSubject, CancellationToken ct = default) => Task.FromResult(scopes);

        public Task<IReadOnlyList<ScopeUnit>> ListUnitsAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListPermissionsAsync(string externalSubject, Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeResidents(IReadOnlyList<AssignedResidentSummary> residents) : IAssignedResidentDirectory
    {
        public Task<IReadOnlyList<AssignedResidentSummary>> ListAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default) =>
            Task.FromResult(residents);
    }
}
