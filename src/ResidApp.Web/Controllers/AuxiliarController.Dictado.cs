using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.IA;
using ResidApp.Shared;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Dictado por voz de «Registrar cambio» (ADR 0009 y 0011), solo el servidor: recibe un audio de un residente asignado al Auxiliar y devuelve una
/// <b>sugerencia</b> de áreas y opciones para el formulario, que la persona revisa antes de confirmar nada. No guarda ni el audio ni el texto.
/// Sin <c>IA:Mistral:ApiKey</c> el dictado está desactivado y esta acción responde 404. La pantalla (botón, micrófono y marca «Sugerido por voz»)
/// llega en el siguiente tramo.
/// </summary>
public sealed partial class AuxiliarController
{
    /// <summary>El audio más el resto del formulario.</summary>
    private const long MaxDictationRequestBytes = SuggestDailyChangeFromVoice.MaxAudioBytes + 64 * 1024;

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(IaOptions.RateLimitPolicy)]
    [RequestSizeLimit(MaxDictationRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDictationRequestBytes)]
    public async Task<IActionResult> DictarCambio(Guid residenteId, IFormFile? audio, Guid operacionId, CancellationToken ct)
    {
        if (!ia.Value.Activa)
        {
            return NotFound();
        }

        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || residenteId == Guid.Empty || operacionId == Guid.Empty || audio is null)
        {
            return BadRequest(new { mensaje = "Revisa los datos del dictado." });
        }

        await using var stream = audio.OpenReadStream();
        var result = await service.SuggestDailyChangeFromVoiceAsync(
            new SuggestDailyChangeFromVoiceCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId),
                stream, audio.Length, audio.ContentType ?? string.Empty, operacionId), ct);

        if (!result.Ok)
        {
            var error = result.Error!;
            return error.Code switch
            {
                ApplicationFailureCode.AccessDenied => StatusCode(StatusCodes.Status403Forbidden, new { mensaje = error.Message }),
                ApplicationFailureCode.InvalidInput => BadRequest(new { mensaje = error.Message }),
                ApplicationFailureCode.Conflict => Conflict(new { mensaje = error.Message }),
                _ => StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { mensaje = "El dictado no está disponible ahora. Puedes rellenar el formulario a mano." }),
            };
        }

        return Json(new
        {
            transcripcion = result.Value!.Transcript,
            areas = result.Value.Areas.Select(a => new
            {
                area = a.Area.ToCode(),
                opciones = a.Options.Select(o => o.ToCode()).ToList(),
                texto = a.Text,
            }).ToList(),
        });
    }
}
