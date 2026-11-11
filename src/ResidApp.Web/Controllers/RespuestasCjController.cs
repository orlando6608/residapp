using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Las respuestas de CJ en el servidor (/pendientes-cj/api/respuestas/{documento}): cada documento recupera aquí lo último que se guardó y lo sustituye al guardar.
/// Anónimo como AccesoCjController; la puerta es la clave de CJ, que el middleware de Program.cs exige antes de llegar aquí (sin ella, 401). Ver RespuestasCjStore.
/// </summary>
[AllowAnonymous]
[Route("pendientes-cj/api/respuestas")]
public sealed class RespuestasCjController(RespuestasCjStore store, TimeProvider clock) : ControllerBase
{
    public const string RateLimitPolicy = "respuestas-cj";

    [HttpGet("{documento}")]
    public async Task<IActionResult> Get(string documento, CancellationToken ct)
    {
        if (!RespuestasCjStore.IsValidName(documento))
        {
            return NotFound();
        }

        var json = await store.ReadAsync(documento, ct);
        Response.Headers.CacheControl = "no-store";
        // Sin nada guardado todavía: 204 y no 404, para que la primera visita no deje un error rojo en la consola del navegador.
        return json is null ? NoContent() : Content(json, "application/json", Encoding.UTF8);
    }

    [HttpPut("{documento}")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Put(string documento, CancellationToken ct)
    {
        if (!RespuestasCjStore.IsValidName(documento))
        {
            return NotFound();
        }

        if (!Request.HasJsonContentType())
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var body = await ReadLimitedAsync(Request.Body, ct);
        if (body is null)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!TryParse(body, documento, out var change))
        {
            return BadRequest();
        }

        // La carga de respuestas antiguas (tools/respuestas-cj.ps1 -Accion Subir) lo dice con esta cabecera: no se avisa a Orlando de algo que CJ no ha hecho.
        var notify = Request.Headers["X-Sin-Aviso"] != "1";
        await store.SaveAsync(documento, body, change!, clock.GetUtcNow(), notify, ct);
        return NoContent();
    }

    /// <summary>Lee el cuerpo como mucho hasta el máximo; null si se pasa.</summary>
    private static async Task<string?> ReadLimitedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > RespuestasCjStore.MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Un objeto JSON con el mismo <c>documento</c> que la ruta y su <c>respuestas</c> (objeto). Solo se guarda lo que tiene esta forma.</summary>
    private static bool TryParse(string body, string documento, out CambioCj? change)
    {
        change = null;
        try
        {
            using var parsed = JsonDocument.Parse(body);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("documento", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != documento
                || !root.TryGetProperty("respuestas", out var answers) || answers.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            change = new CambioCj(Count(root, "respondidas"), Count(root, "total"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int Count(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= 0 ? n : 0;
}
