using System.Text.Json.Nodes;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Pasa un audio a texto (ADR 0011). Un puerto es una interfaz que dice qué se necesita sin decir quién lo hace: el único adaptador hoy
/// es el de Mistral, en Infrastructure/IA. No guarda el audio ni el texto.</summary>
public interface ISpeechTranscriber
{
    Task<string> TranscribeAsync(Stream audio, string contentType, string language, CancellationToken cancellationToken);
}

/// <summary>Pide al modelo una respuesta que cumpla un esquema estricto (una plantilla que solo admite los campos y valores permitidos).
/// Lo que devuelve es una sugerencia: quien lo llama la valida como cualquier entrada. Devuelve null si el modelo no contesta con el esquema.
/// <paramref name="promptName"/> es el fichero de Infrastructure/IA/Prompts.</summary>
public interface IStructuredGenerator
{
    Task<T?> GenerateAsync<T>(string promptName, string input, JsonObject schema, CancellationToken cancellationToken) where T : class;
}

/// <summary>Un uso del dictado, anotado antes de que el audio salga de la aplicación. No lleva el audio ni el texto.</summary>
public sealed record VoiceDictationAuditInput(AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId, Guid OperationId);

/// <summary>Deja constancia de que se usó el dictado (VOICE_DICTATION_USED en dbo.eventos_auditoria). Si falla, el audio no se envía.</summary>
public interface IVoiceDictationAudit
{
    Task RecordAsync(VoiceDictationAuditInput input, CancellationToken ct = default);
}
