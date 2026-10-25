using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

public sealed record SuggestDailyChangeFromVoiceCommand(
    Guid AmbitoPerfilId, CenterId CentroId, ResidentId ResidenteId, Stream Audio, long AudioBytes, string ContentType, Guid OperacionId);

/// <summary>Un área sugerida por el dictado. Es una sugerencia: la persona la revisa y la confirma antes de registrar nada.</summary>
public sealed record VoiceSuggestedArea(DailyChangeAreaCode Area, IReadOnlyList<DailyChangeAreaOptionCode> Options, string? Text);

/// <summary>Lo entendido (la transcripción, que no se guarda) y lo sugerido para el formulario «Registrar cambio»: áreas y, si se dijo, la
/// temperatura.</summary>
public sealed record VoiceSuggestion(string Transcript, IReadOnlyList<VoiceSuggestedArea> Areas, decimal? Temperature = null);

/// <summary>Lo que contesta el modelo, tal cual, antes de validarlo. Los nombres coinciden con el esquema de <see cref="DailyChangeDictationSchema"/>.</summary>
public sealed record VoiceModelArea(string? Area, string? Texto, List<string>? Opciones);

public sealed record VoiceModelOutput(List<VoiceModelArea>? Areas, decimal? Temperatura = null);

/// <summary>
/// Dictado de «Registrar cambio» (ADR 0009 y 0011): de un audio del Auxiliar saca una sugerencia de áreas y opciones del formulario. Repite la
/// comprobación de ámbito Auxiliar y residente asignado de <see cref="RegisterDailyChange"/>. El uso se audita <b>antes</b> de que el audio salga
/// de la aplicación, y ni el audio ni el texto se guardan. Lo que el modelo devuelve no se da por bueno: se valida contra el catálogo cerrado.
/// </summary>
public sealed class SuggestDailyChangeFromVoice(
    IProfileScopeDirectoryProvider scopes, IAssignedResidentDirectory directory, ISessionIdentityProvider session,
    IVoiceDictationAudit audit, ISpeechTranscriber transcriber, IStructuredGenerator generator)
{
    public const long MaxAudioBytes = 5 * 1024 * 1024;

    /// <summary>El texto libre de un área admite hasta 1000 caracteres (dbo.cierres_cotidianos_cambio_areas.texto_libre).</summary>
    public const int MaxAreaTextLength = 1000;

    public const string Language = "es";
    public const string PromptName = "registrar-cambio";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/webm", "audio/ogg", "audio/mp4", "audio/mpeg", "audio/wav", "audio/x-wav", "audio/wave",
    };

    public Task<ApplicationResult<VoiceSuggestion>> ExecuteAsync(
        SuggestDailyChangeFromVoiceCommand command, CancellationToken ct = default) =>
        ApplicationResultRunner.RunAsync(async () =>
        {
            var baseType = command.ContentType.Split(';', 2)[0].Trim();
            if (command.AudioBytes <= 0 || command.AudioBytes > MaxAudioBytes || !AllowedContentTypes.Contains(baseType))
            {
                throw new DomainValidationException("VOICE_DICTATION_INPUT_INVALID");
            }

            var identity = await session.GetVerifiedIdentityAsync(ct);
            if (identity is null)
            {
                throw new AccessDeniedException();
            }

            var activeScopes = await scopes.ListActiveAsync(identity.ExternalSubject, ct);
            var scope = activeScopes.FirstOrDefault(s => s.ProfileScopeId == command.AmbitoPerfilId && s.CenterId == command.CentroId);
            if (scope is null || scope.Profile != SystemProfile.Auxiliar)
            {
                throw new AccessDeniedException();
            }

            var assigned = (await directory.ListAsync(command.AmbitoPerfilId, command.CentroId, ct))
                .FirstOrDefault(r => r.ResidentId == command.ResidenteId);
            if (assigned is null)
            {
                throw new AccessDeniedException();
            }

            // Primero la constancia y después el envío: si la auditoría falla, el audio no sale de la aplicación.
            await audit.RecordAsync(
                new VoiceDictationAuditInput(scope.AccountId, command.CentroId, assigned.UnitId, command.ResidenteId, command.OperacionId), ct);

            var transcript = await transcriber.TranscribeAsync(command.Audio, baseType, Language, ct);
            var output = await generator.GenerateAsync<VoiceModelOutput>(PromptName, transcript, DailyChangeDictationSchema.Build(), ct)
                ?? throw new InvalidOperationException("VOICE_DICTATION_UNAVAILABLE");

            return new VoiceSuggestion(transcript, Sanitize(output), SanitizeTemperature(output.Temperatura));
        });

    /// <summary>
    /// Deja solo lo que el formulario admitiría: el código de área tiene que existir, las opciones tienen que ser del área, las áreas repetidas se unen
    /// y un área sin opciones ni texto se descarta. Es lo mismo que haría <see cref="RegisterDailyChange"/> al registrar, pero aquí no se rechaza: se
    /// quita, porque es una sugerencia y no una orden.
    /// </summary>
    public static IReadOnlyList<VoiceSuggestedArea> Sanitize(VoiceModelOutput output)
    {
        var byArea = new Dictionary<DailyChangeAreaCode, (List<DailyChangeAreaOptionCode> Options, string? Text)>();
        var order = new List<DailyChangeAreaCode>();
        foreach (var raw in output.Areas ?? [])
        {
            if (raw.Area is null || !EnumCode.TryParseCode<DailyChangeAreaCode>(raw.Area, out var area))
            {
                continue;
            }

            var allowed = DailyChangeAreaOptionsCatalog.OptionsFor(area);
            var options = new List<DailyChangeAreaOptionCode>();
            foreach (var code in raw.Opciones ?? [])
            {
                if (EnumCode.TryParseCode<DailyChangeAreaOptionCode>(code, out var option) && allowed.Contains(option) && !options.Contains(option))
                {
                    options.Add(option);
                }
            }
            var text = string.IsNullOrWhiteSpace(raw.Texto) ? null : raw.Texto.Trim();

            if (!byArea.TryGetValue(area, out var previous))
            {
                byArea[area] = (options, text);
                order.Add(area);
                continue;
            }
            previous.Options.AddRange(options.Except(previous.Options));
            byArea[area] = (previous.Options, JoinTexts(previous.Text, text));
        }

        return order
            .Select(area => new VoiceSuggestedArea(area, byArea[area].Options, Truncate(byArea[area].Text)))
            .Where(a => a.Options.Count > 0 || a.Text is not null)
            .ToList();
    }

    /// <summary>La temperatura sugerida, redondeada a una décima, solo si está en el rango de CJ (<see cref="DailyChangeTemperature"/>): fuera de él
    /// casi siempre es un error de transcripción y el formulario no la dejaría guardar, así que no se sugiere.</summary>
    public static decimal? SanitizeTemperature(decimal? celsius) =>
        celsius is { } value && DailyChangeTemperature.IsValid(Math.Round(value, 1)) ? Math.Round(value, 1) : null;

    private static string? JoinTexts(string? first, string? second) =>
        first is null ? second : second is null || second == first ? first : $"{first} {second}";

    private static string? Truncate(string? text) =>
        text is null || text.Length <= MaxAreaTextLength ? text : text[..MaxAreaTextLength].TrimEnd();
}
