using System.Text.Json.Nodes;
using ResidApp.Application.Ports;

namespace ResidApp.IntegrationTests.TestSupport;

/// <summary>Un proveedor de IA falso (ADR 0011): contesta lo que se le diga y anota qué se le pidió, sin llamar nunca a internet.</summary>
public sealed class FakeVoiceProvider : ISpeechTranscriber, IStructuredGenerator
{
    public string Transcript { get; set; } = "";

    public object? Output { get; set; }

    public bool Fails { get; set; }

    public List<string> Calls { get; } = [];

    public Task<string> TranscribeAsync(Stream audio, string contentType, string language, CancellationToken cancellationToken)
    {
        Calls.Add("transcribir");
        return Fails ? throw new InvalidOperationException("VOICE_DICTATION_UNAVAILABLE") : Task.FromResult(Transcript);
    }

    public Task<T?> GenerateAsync<T>(string promptName, string input, JsonObject schema, CancellationToken cancellationToken) where T : class
    {
        Calls.Add("estructurar");
        return Fails ? throw new InvalidOperationException("VOICE_DICTATION_UNAVAILABLE") : Task.FromResult(Output as T);
    }
}
