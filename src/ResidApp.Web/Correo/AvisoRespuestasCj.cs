using System.Text;
using ResidApp.Application.Ports;
using ResidApp.Web.Security;

namespace ResidApp.Web.Correo;

/// <summary>
/// El aviso a Orlando de que CJ ha respondido (o cambiado) algo en los documentos de /pendientes-cj. Reutiliza el envío de correo de la aplicación (<see cref="IEmailSender"/>,
/// ADR 0012) sin tocar la base de datos: lo pendiente está en un fichero junto a las respuestas (<see cref="RespuestasCjStore"/>). Manda un solo correo cuando CJ lleva
/// <c>quiet</c> sin cambiar nada, para no avisar por cada pulsación. El correo dice qué documentos y cuántas preguntas completadas, nunca las respuestas ni los comentarios.
/// La dirección sale de <c>PendientesCj:AvisarA</c> (ajuste de la Web App, no del repositorio); sin ella, o con el correo apagado, no hace nada. Mismo criterio de destinos
/// que el resto de avisos: fuera de producción solo a las direcciones permitidas. Es un uso de desarrollo: producción no lleva estos documentos.
/// </summary>
public sealed class AvisoRespuestasCj(
    RespuestasCjStore store, IEmailSender sender, EmailSettings settings, string? address, TimeSpan quiet, Action<string> warn)
{
    private bool _notAllowedReported;

    /// <summary>Una vuelta de la tarea. True si mandó el correo.</summary>
    public async Task<bool> ProcessAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!settings.Active || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (!RecipientAllowed(settings, address))
        {
            if (!_notAllowedReported)
            {
                warn("Aviso de respuestas de CJ: la dirección de PendientesCj:AvisarA no está entre las permitidas (Correo:DireccionesPermitidas); no se envía.");
                _notAllowedReported = true;
            }

            return false;
        }

        var due = await store.PeekDueAsync(now, quiet, ct);
        if (due is null)
        {
            return false;
        }

        try
        {
            var result = await sender.SendAsync(Compose(due, settings.BaseUrl, address), ct);
            if (result.Accepted)
            {
                await store.MarkToldAsync(due, CancellationToken.None);
                return true;
            }

            warn($"Aviso de respuestas de CJ: el proveedor no aceptó el correo ({result.FailureCode}); se reintenta tras otro silencio.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warn($"Aviso de respuestas de CJ: el envío falló ({ex.GetType().Name}); se reintenta tras otro silencio.");
        }

        await store.DeferAsync(now, CancellationToken.None);
        return false;
    }

    /// <summary>Mismo criterio de destinos que el resto de avisos: fuera de producción, solo a las direcciones permitidas.</summary>
    public static bool RecipientAllowed(EmailSettings settings, string address) =>
        settings.AnyRecipient || settings.AllowedRecipients.Contains(address, StringComparer.OrdinalIgnoreCase);

    public static EmailMessage Compose(AvisoCjPendiente pending, string baseUrl, string to)
    {
        var root = baseUrl.TrimEnd('/');
        var documents = pending.Documentos.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
        var subject = documents.Count == 1
            ? "CJ ha respondido en 1 documento de ResidApp"
            : $"CJ ha respondido en {documents.Count} documentos de ResidApp";

        var text = new StringBuilder("CJ ha guardado cambios en sus respuestas:\n\n");
        var html = new StringBuilder("<p>CJ ha guardado cambios en sus respuestas:</p><ul>");
        foreach (var (name, change) in documents)
        {
            var url = $"{root}/pendientes-cj/{name}.html";
            text.Append($"- {name}: {change.Respondidas} de {change.Total} completadas\n  {url}\n");
            html.Append($"<li><a href=\"{url}\">{name}</a>: {change.Respondidas} de {change.Total} completadas</li>");
        }

        text.Append("\nEl correo no incluye lo que ha contestado: míralo en el propio documento o con tools/respuestas-cj.ps1.\n");
        html.Append("</ul><p>El correo no incluye lo que ha contestado: míralo en el propio documento o con <code>tools/respuestas-cj.ps1</code>.</p>");
        return new EmailMessage(to, subject, text.ToString(), html.ToString());
    }
}
