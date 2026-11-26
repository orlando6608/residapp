using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El aviso a la dirección anterior cuando Administración cambia o borra el correo con el que entra un familiar (ADR 0012, pieza 3). Se llama DESPUÉS de guardar el cambio y con
/// su propio tiempo límite, sin depender de que el navegador siga ahí: ni un fallo del envío ni uno del registro deshacen nada. No mira la baja de avisos, la marca de dirección
/// confirmada ni los interruptores de cada aviso: es un aviso de seguridad. Anota una sola fila ya resuelta, sin direcciones. <paramref name="warn"/> recibe solo códigos
/// cerrados: nunca la dirección ni el texto de una excepción.
/// </summary>
public sealed class FamilyEmailChangeNoticeService(
    IFamilyEmailChangeLog log, IEmailSender sender, EmailSettings settings, TimeProvider clock, Action<string>? warn = null, TimeSpan? sendTimeout = null)
{
    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);

    public async Task<FamilyEmailNotice> NotifyAsync(CenterId centerId, FamilyEmailChanged change)
    {
        var attempts = 0;
        EmailSendResult result;
        string state;
        if (!settings.Active)
        {
            (state, result) = (EmailLog.Omitted, EmailSendResult.Fail(EmailLog.MailOff));
        }
        else if (!settings.AnyRecipient && !settings.AllowedRecipients.Contains(change.PreviousAddress, StringComparer.OrdinalIgnoreCase))
        {
            (state, result) = (EmailLog.Omitted, EmailSendResult.Fail(EmailLog.RecipientNotAllowed));
        }
        else
        {
            attempts = 1;
            result = await SendAsync(change.PreviousAddress);
            state = result.Accepted ? EmailLog.Sent : EmailLog.Failed;
        }

        var reason = result.Accepted ? null : result.FailureCode;
        try
        {
            await log.RecordAsync(new FamilyEmailChangeEntry(
                centerId, change.FamilyId, EmailChangeTemplate.Version, settings.Language, state, reason, result.OperationId, attempts, clock.GetUtcNow().UtcDateTime));
            warn?.Invoke($"{EmailLog.EmailChangeTemplate}: {state}{(reason is null ? "" : $" {reason}")}");
        }
        catch (Exception ex)
        {
            // Nunca la excepción entera: la de SQL puede llevar datos de la fila.
            warn?.Invoke($"{EmailLog.EmailChangeTemplate}: no se pudo registrar el envío ({ex.GetType().Name})");
        }

        return state == EmailLog.Sent ? FamilyEmailNotice.Sent : FamilyEmailNotice.NotSent;
    }

    private async Task<EmailSendResult> SendAsync(string address)
    {
        var timeout = sendTimeout ?? DefaultSendTimeout;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var email = EmailChangeTemplate.Render(settings.Language);
            return await sender.SendAsync(
                new EmailMessage(address, email.Subject, email.PlainText, email.Html, email.InlineImages), cts.Token).WaitAsync(timeout);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            return EmailSendResult.Fail(EmailLog.TimedOut);
        }
        catch (Exception ex)
        {
            warn?.Invoke($"{EmailLog.EmailChangeTemplate}: el proveedor falló ({ex.GetType().Name})");
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
