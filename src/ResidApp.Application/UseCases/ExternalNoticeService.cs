using ResidApp.Application.Email;
using ResidApp.Application.Ports;

namespace ResidApp.Application.UseCases;

/// <summary>
/// Los avisos de citas externas a la familia, por correo (PR 6 de las citas externas; script 0105; docs/privacidad/citas-externas-lectura-dpo.md, apartado 5.2). Nadie los desencadena: la tarea
/// programada de la aplicación llama cada minuto a <see cref="ReserveDueAsync"/> (la base reserva una fila por persona y cita: a 7 días y la víspera, a quien tiene un encargo o, si nadie, a todos
/// los autorizados con correo confirmado; a 2 días, solo si sigue sin cubrir y no a quien se encargó o dijo «No puedo») y a <see cref="ProcessDueAsync"/> (reclama cada fila, vuelve a comprobar todo
/// al enviar —el aviso ya no vale si alguien se encargó, la cita se movió u ocultó, el residente se restringió, o la persona ya no tiene acceso o se dio de baja— y manda un solo correo a esa
/// persona). Solo se envía en la franja de las 10:00 a las 18:59 (hora de Madrid). Sin reintentos. <paramref name="log"/> recibe solo la plantilla, el estado y un código: nunca una dirección
/// ni el texto de una excepción. Apagado por defecto: hace falta <see cref="EmailSettings.Active"/> y <see cref="EmailSettings.ExternalNotice"/> (el interruptor del entorno) y, además, el del
/// centro, que la base comprueba al reservar y al enviar.
/// </summary>
public sealed class ExternalNoticeService(
    IExternalNoticeStore store, IEmailSender sender, EmailSettings settings, Action<string>? log = null, TimeSpan? sendTimeout = null, TimeZoneInfo? zone = null)
{
    /// <summary>Primera hora de la franja de envío (hora local de Madrid): las 10:00.</summary>
    public const int WindowStartHour = ReminderService.WindowStartHour;

    /// <summary>La franja termina antes de esta hora: la última vuelta posible es a las 18:59.</summary>
    public const int WindowEndHour = ReminderService.WindowEndHour;

    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);

    private const int BatchSize = 20;

    private bool Enabled => settings.Active && settings.ExternalNotice;

    /// <summary>Si <paramref name="now"/> está dentro de la franja de envío (hora de la zona de la aplicación, Europe/Madrid).</summary>
    public bool InWindow(DateTimeOffset now)
    {
        var hour = TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local).Hour;
        return hour >= WindowStartHour && hour < WindowEndHour;
    }

    /// <summary>Pide a la base de datos que reserve los avisos que tocan hoy. Devuelve cuántos reservó. Fuera de la franja ni siquiera llama.</summary>
    public async Task<int> ReserveDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Enabled || !InWindow(now))
        {
            return 0;
        }

        try
        {
            return await store.ReserveAsync(settings.Language, ExternalNoticeTemplate.Version, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{EmailLog.ExternalNoticeTemplate}: no se pudieron reservar los avisos ({ex.GetType().Name})");
            return 0;
        }
    }

    /// <summary>Procesa los avisos reservados y sin reclamar. Devuelve cuántos reclamó esta ejecución.</summary>
    public async Task<int> ProcessDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return 0;
        }

        var claimed = 0;
        IReadOnlyList<PendingExternalNotice> due;
        try
        {
            due = await store.ListPendingAsync(BatchSize, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{EmailLog.ExternalNoticeTemplate}: no se pudieron leer los avisos pendientes ({ex.GetType().Name})");
            return 0;
        }

        var inWindow = InWindow(now);
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
                if (inWindow)
                {
                    await ProcessAsync(notice, now, ct);
                }
                else
                {
                    await store.ResolveAsync(notice.Id, EmailLog.Omitted, EmailLog.OutOfWindow, null, 0, DateTimeOffset.UtcNow, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nunca la excepción entera: la de ACS o la de SQL pueden llevar la dirección.
                log?.Invoke($"{EmailLog.ExternalNoticeTemplate}: no se pudo registrar el envío ({ex.GetType().Name})");
            }
        }

        return claimed;
    }

    private async Task ProcessAsync(PendingExternalNotice notice, DateTimeOffset now, CancellationToken ct)
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

        var email = ExternalNoticeTemplate.Render(notice.Language, settings.BaseUrl, notice.Template == EmailLog.ExternalNoticeUncoveredTemplate);
        var result = await SendAsync(new EmailMessage(address, email.Subject, email.PlainText, email.Html, email.InlineImages), ct);
        await store.ResolveAsync(
            notice.Id, result.Accepted ? EmailLog.Sent : EmailLog.Failed, result.Accepted ? null : result.FailureCode, result.OperationId, 1, DateTimeOffset.UtcNow, ct);
        log?.Invoke($"{notice.Template}: {(result.Accepted ? EmailLog.Sent : $"{EmailLog.Failed} {result.FailureCode}")}");
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
            log?.Invoke($"{EmailLog.ExternalNoticeTemplate}: el proveedor falló ({ex.GetType().Name})");
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
