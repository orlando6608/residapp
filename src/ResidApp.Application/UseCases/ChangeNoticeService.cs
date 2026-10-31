using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El aviso genérico por correo cuando el centro reprograma o cancela una cita confirmada (ADR 0012; lectura de privacidad en
/// docs/privacidad/aviso-cita-cambio-cancelacion-por-correo.md). Va en dos tiempos. <see cref="RegisterAsync"/> se llama DESPUÉS de que el cambio se haya guardado y solo
/// reserva una fila PENDIENTE: un fallo ahí nunca cambia el resultado de reprogramar o cancelar. <see cref="ProcessDueAsync"/> lo llama la tarea programada de la
/// aplicación: espera unos minutos desde el cambio (si la cita cambia otra vez, el aviso del primer cambio ya no es vigente y se omite), vuelve a comprobar que sigue
/// siendo vigente y a quién escribir, y manda un solo correo a quien reservó la cita. Sin reintentos. <paramref name="log"/> recibe solo la plantilla, el estado y un
/// código: nunca una dirección ni el texto de una excepción. Apagado por defecto: hace falta <see cref="EmailSettings.Active"/> y <see cref="EmailSettings.ChangeNotice"/>.
/// </summary>
public sealed class ChangeNoticeService(
    IChangeNoticeStore store, IEmailSender sender, EmailSettings settings, Action<string>? log = null, TimeSpan? sendTimeout = null, TimeSpan? delay = null)
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);

    private const int BatchSize = 20;

    private bool Enabled => settings.Active && settings.ChangeNotice;

    public async Task RegisterAsync(CenterId centerId, Guid appointmentId, int changeNumber)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            await store.RegisterAsync(centerId, appointmentId, changeNumber, settings.Language, ChangeNoticeTemplate.Version, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            log?.Invoke($"{EmailLog.ChangeTemplate}: no se pudo reservar el aviso ({ex.GetType().Name})");
        }
    }

    /// <summary>Procesa los avisos reservados hace más de <see cref="DefaultDelay"/>. Devuelve cuántos reclamó esta ejecución.</summary>
    public async Task<int> ProcessDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return 0;
        }

        var claimed = 0;
        IReadOnlyList<PendingChangeNotice> due;
        try
        {
            due = await store.ListDueAsync(now - (delay ?? DefaultDelay), BatchSize, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{EmailLog.ChangeTemplate}: no se pudieron leer los avisos pendientes ({ex.GetType().Name})");
            return 0;
        }

        foreach (var notice in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!await store.ClaimAsync(notice.Id, ct))
                {
                    continue;
                }

                claimed++;
                await ProcessAsync(notice, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nunca la excepción entera: la de ACS o la de SQL pueden llevar la dirección.
                log?.Invoke($"{EmailLog.ChangeTemplate}: no se pudo registrar el envío ({ex.GetType().Name})");
            }
        }

        return claimed;
    }

    private async Task ProcessAsync(PendingChangeNotice notice, DateTimeOffset now, CancellationToken ct)
    {
        var preparation = await store.PrepareAsync(notice, now, ct);
        if (preparation.Address is not { } address)
        {
            await store.ResolveAsync(notice.Id, EmailLog.Omitted, preparation.OmitReason ?? EmailLog.NoAddress, null, 0, DateTimeOffset.UtcNow, ct);
            return;
        }

        if (!settings.AnyRecipient && !settings.AllowedRecipients.Contains(address, StringComparer.OrdinalIgnoreCase))
        {
            await store.ResolveAsync(notice.Id, EmailLog.Omitted, EmailLog.RecipientNotAllowed, null, 0, DateTimeOffset.UtcNow, ct);
            return;
        }

        var email = ChangeNoticeTemplate.Render(notice.Language, settings.BaseUrl);
        var result = await SendAsync(new EmailMessage(address, email.Subject, email.PlainText, email.Html, email.InlineImages), ct);
        await store.ResolveAsync(
            notice.Id, result.Accepted ? EmailLog.Sent : EmailLog.Failed, result.Accepted ? null : result.FailureCode, result.OperationId, 1, DateTimeOffset.UtcNow, ct);
        log?.Invoke($"{EmailLog.ChangeTemplate}: {(result.Accepted ? EmailLog.Sent : $"{EmailLog.Failed} {result.FailureCode}")}");
    }

    private async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var timeout = sendTimeout ?? DefaultSendTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await sender.SendAsync(message, cts.Token).WaitAsync(timeout, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException && !ct.IsCancellationRequested)
        {
            return EmailSendResult.Fail(EmailLog.TimedOut);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{EmailLog.ChangeTemplate}: el proveedor falló ({ex.GetType().Name})");
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
