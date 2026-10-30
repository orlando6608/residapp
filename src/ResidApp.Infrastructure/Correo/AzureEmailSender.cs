using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using ResidApp.Application.Ports;
using AzureEmailMessage = Azure.Communication.Email.EmailMessage;
using EmailMessage = ResidApp.Application.Ports.EmailMessage;
using EmailSendResult = ResidApp.Application.Ports.EmailSendResult;

namespace ResidApp.Infrastructure.Correo;

/// <summary>
/// Adaptador de Azure Communication Services Email (ADR 0012). Un solo destinatario en «Para», sin copia, sin copia oculta y sin «Responder a»; seguimiento de aperturas y
/// clics apagado (lectura de privacidad, 2.6). Solo devuelve un código cerrado, nunca el mensaje de la excepción de ACS (puede llevar la dirección). No espera a que el
/// correo se entregue: «aceptado» es lo que se anota.
/// </summary>
public sealed class AzureEmailSender(IOptions<CorreoOptions> options) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var settings = options.Value;
        try
        {
            var client = new EmailClient(settings.CadenaConexion);
            var email = new AzureEmailMessage(
                settings.Remitente, message.To, new EmailContent(message.Subject) { PlainText = message.PlainText, Html = message.Html })
            {
                UserEngagementTrackingDisabled = true,
            };
            var operation = await client.SendAsync(WaitUntil.Started, email, ct);
            return EmailSendResult.Ok(operation.Id);
        }
        catch (RequestFailedException ex) when (ex.Status is >= 400 and < 500 and not 429)
        {
            return EmailSendResult.Fail(EmailLog.ProviderRejected);
        }
        catch (RequestFailedException)
        {
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
