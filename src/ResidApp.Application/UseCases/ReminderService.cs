using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>
/// El recordatorio de una cita confirmada, el día antes (ADR 0012; CJ, tercera tanda 6.2 B; lectura de privacidad en docs/privacidad/recordatorio-cita-por-correo.md). Nadie lo
/// desencadena: la tarea programada de la aplicación llama cada minuto a <see cref="ReserveDueAsync"/> (pide a la base de datos que reserve una fila por cada cita confirmada de
/// mañana) y a <see cref="ProcessDueAsync"/> (reclama cada fila, vuelve a comprobar que sigue siendo verdad que la cita es mañana y a quién escribir, y manda un solo correo a
/// quien reservó la cita). Solo se envía en la franja de las 10:00 a las 18:59 (hora del centro): fuera de ella no se reserva ni se envía nada, y lo que quedó reservado se cierra
/// como OMITIDO / FUERA_DE_PLAZO, porque «mañana» ya no sería verdad. Sin reintentos. <paramref name="log"/> recibe solo la plantilla, el estado y un código: nunca una dirección ni
/// el texto de una excepción. Apagado por defecto: hace falta <see cref="EmailSettings.Active"/> y <see cref="EmailSettings.Reminder"/>.
/// </summary>
public sealed class ReminderService(
    IReminderStore store, IEmailSender sender, EmailSettings settings, Action<string>? log = null, TimeSpan? sendTimeout = null, TimeZoneInfo? zone = null)
{
    /// <summary>Primera hora de la franja de envío (hora local del centro): las 10:00.</summary>
    public const int WindowStartHour = 10;

    /// <summary>La franja termina antes de esta hora: la última vuelta posible es a las 18:59.</summary>
    public const int WindowEndHour = 19;

    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);

    private const int BatchSize = 20;

    private bool Enabled => settings.Active && settings.Reminder;

    /// <summary>Si <paramref name="now"/> está dentro de la franja de envío en la zona dada (la del centro del recordatorio). Con una zona fijada al construir el servicio (las pruebas), manda esa; sin ninguna, la de Madrid.</summary>
    public bool InWindow(DateTimeOffset now, TimeZoneInfo? centerZone = null)
    {
        var hour = TimeZoneInfo.ConvertTime(now, zone ?? centerZone ?? CenterTime.Madrid).Hour;
        return hour >= WindowStartHour && hour < WindowEndHour;
    }

    /// <summary>Pide a la base de datos que reserve los recordatorios de las citas de mañana. Devuelve cuántos reservó. La franja la decide la base, centro a centro con la zona de cada uno
    /// (<paramref name="now"/> no se usa: el procedimiento lee su propio reloj).</summary>
    public async Task<int> ReserveDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return 0;
        }

        try
        {
            return await store.ReserveAsync(settings.Language, ReminderTemplate.Version, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{EmailLog.ReminderTemplate}: no se pudieron reservar los recordatorios ({ex.GetType().Name})");
            return 0;
        }
    }

    /// <summary>Procesa los recordatorios reservados y sin reclamar. Devuelve cuántos reclamó esta ejecución.</summary>
    public async Task<int> ProcessDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return 0;
        }

        var claimed = 0;
        IReadOnlyList<PendingReminder> due;
        try
        {
            due = await store.ListPendingAsync(BatchSize, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log?.Invoke($"{EmailLog.ReminderTemplate}: no se pudieron leer los recordatorios pendientes ({ex.GetType().Name})");
            return 0;
        }

        var zones = new Dictionary<Guid, TimeZoneInfo>();
        foreach (var reminder in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // La franja es la del centro del recordatorio. Si no se puede leer su zona, no se reclama: se reintenta en la vuelta siguiente.
                if (zone is null && !zones.ContainsKey(reminder.CenterId.Value))
                {
                    zones[reminder.CenterId.Value] = await store.ZoneOfCenterAsync(reminder.CenterId, ct);
                }

                var inWindow = InWindow(now, zones.GetValueOrDefault(reminder.CenterId.Value));
                if (!await store.ClaimAsync(reminder.Id, ct))
                {
                    continue;
                }

                claimed++;
                if (inWindow)
                {
                    await ProcessAsync(reminder, now, ct);
                }
                else
                {
                    await store.ResolveAsync(reminder.Id, EmailLog.Omitted, EmailLog.OutOfWindow, null, 0, DateTimeOffset.UtcNow, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nunca la excepción entera: la de ACS o la de SQL pueden llevar la dirección.
                log?.Invoke($"{EmailLog.ReminderTemplate}: no se pudo registrar el envío ({ex.GetType().Name})");
            }
        }

        return claimed;
    }

    private async Task ProcessAsync(PendingReminder reminder, DateTimeOffset now, CancellationToken ct)
    {
        var preparation = await store.PrepareAsync(reminder, now, ct);
        if (preparation.Address is not { } address)
        {
            await store.ResolveAsync(reminder.Id, EmailLog.Omitted, preparation.OmitReason ?? EmailLog.NoAddress, null, 0, DateTimeOffset.UtcNow, ct);
            return;
        }

        if (!settings.AnyRecipient && !settings.AllowedRecipients.Contains(address, StringComparer.OrdinalIgnoreCase))
        {
            await store.ResolveAsync(reminder.Id, EmailLog.Omitted, EmailLog.RecipientNotAllowed, null, 0, DateTimeOffset.UtcNow, ct);
            return;
        }

        var email = ReminderTemplate.Render(reminder.Language, settings.BaseUrl);
        var result = await SendAsync(new EmailMessage(address, email.Subject, email.PlainText, email.Html, email.InlineImages), ct);
        await store.ResolveAsync(
            reminder.Id, result.Accepted ? EmailLog.Sent : EmailLog.Failed, result.Accepted ? null : result.FailureCode, result.OperationId, 1, DateTimeOffset.UtcNow, ct);
        log?.Invoke($"{EmailLog.ReminderTemplate}: {(result.Accepted ? EmailLog.Sent : $"{EmailLog.Failed} {result.FailureCode}")}");
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
            log?.Invoke($"{EmailLog.ReminderTemplate}: el proveedor falló ({ex.GetType().Name})");
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
