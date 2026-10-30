using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El aviso genérico por correo cuando el centro propone una cita (ADR 0012, pieza 1; CJ, tercera tanda 6.1). Se llama DESPUÉS de que la propuesta se haya guardado
/// (el <c>Commit</c> ya está hecho): un fallo, un tiempo agotado o un proveedor apagado nunca cambian el resultado de proponer, y el aviso dentro del portal cubre
/// a la familia. Vuelve a comprobar en el momento de enviar que la propuesta sigue viva y que la familia aún puede verla (la misma consulta que el portal), y manda un
/// solo correo, al familiar que hizo la solicitud. Sin reintentos: no hay tareas programadas (llegan con el recordatorio).
/// <paramref name="log"/> recibe solo la plantilla, el estado y un código: nunca una dirección ni el texto de una excepción.
/// </summary>
public sealed class ProposalNoticeService(
    IProposalNoticeStore store, IEmailSender sender, EmailSettings settings, Action<string>? log = null, TimeSpan? sendTimeout = null)
{
    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);

    public async Task NotifyAsync(CenterId centerId, Guid requestId, int changeNumber)
    {
        if (!settings.Active)
        {
            return;
        }

        try
        {
            var reservation = await store.ReserveAsync(
                centerId, requestId, changeNumber, settings.Language, ProposalNoticeTemplate.Version, DateTimeOffset.UtcNow);
            if (reservation?.Address is not { } address)
            {
                return;
            }

            if (!settings.AnyRecipient && !settings.AllowedRecipients.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                await store.ResolveAsync(reservation.Id, EmailLog.Omitted, EmailLog.RecipientNotAllowed, null, 0, DateTimeOffset.UtcNow);
                return;
            }

            var email = ProposalNoticeTemplate.Render(reservation.Language, settings.BaseUrl);
            var result = await SendAsync(new EmailMessage(address, email.Subject, email.PlainText, email.Html));
            await store.ResolveAsync(
                reservation.Id, result.Accepted ? EmailLog.Sent : EmailLog.Failed, result.Accepted ? null : result.FailureCode, result.OperationId, 1, DateTimeOffset.UtcNow);
            log?.Invoke($"{EmailLog.ProposalTemplate}: {(result.Accepted ? EmailLog.Sent : $"{EmailLog.Failed} {result.FailureCode}")}");
        }
        catch (Exception ex)
        {
            // Nunca la excepción entera: la de ACS o la de SQL pueden llevar la dirección.
            log?.Invoke($"{EmailLog.ProposalTemplate}: no se pudo registrar el envío ({ex.GetType().Name})");
        }
    }

    private async Task<EmailSendResult> SendAsync(EmailMessage message)
    {
        var timeout = sendTimeout ?? DefaultSendTimeout;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await sender.SendAsync(message, cts.Token).WaitAsync(timeout);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            return EmailSendResult.Fail(EmailLog.TimedOut);
        }
        catch (Exception ex)
        {
            log?.Invoke($"{EmailLog.ProposalTemplate}: el proveedor falló ({ex.GetType().Name})");
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
