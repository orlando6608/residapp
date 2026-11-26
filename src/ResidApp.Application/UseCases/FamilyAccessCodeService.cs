using System.Security.Cryptography;
using System.Text;
using ResidApp.Application.Email;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Application.UseCases;

/// <summary>Los límites del código de acceso (decididos por Orlando el 2026-11-26; cifras de ingeniería, no un plazo legal).</summary>
public static class FamilyAccessCodeRules
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan WaitBetweenSends = TimeSpan.FromSeconds(60);
    public const int SendsPerHour = 5;
    public const int SendsPerDay = 10;
    public const int AttemptsPerCode = 5;
    public const int FailuresPerDayBeforeBlock = 15;

    /// <summary>Longitud mínima de la clave del resumen (<c>Acceso__CodigoFamiliarClave</c>).</summary>
    public const int MinimumKeyLength = 32;
}

/// <summary>La clave con la que se resume cada código. Está fuera de la base de datos y del repositorio; sin una de al menos 32 caracteres no se manda ni se admite ningún
/// código.</summary>
public sealed record FamilyAccessCodeKey(string? Value)
{
    public bool IsUsable => Value is { Length: >= FamilyAccessCodeRules.MinimumKeyLength };
}

/// <summary>Resultado de pedir un código.</summary>
public enum AccessCodeSendOutcome
{
    Sent,
    /// <summary>El correo o la clave no están configurados en este entorno.</summary>
    NotAvailable,
    NoAddress,
    Blocked,
    TooSoon,
    HourLimit,
    DayLimit,
    SendFailed,
}

public sealed record AccessCodeSend(AccessCodeSendOutcome Outcome, Guid? CodeId = null);

/// <summary>
/// El código por correo con el que entra la familia además de su contraseña (ADR 0012, pieza 3). Siempre falla cerrado: sin correo, sin clave, con el envío apagado, con un destino
/// no permitido, un rechazo, cualquier excepción del proveedor o un tiempo agotado, no sale ningún código que valga. <paramref name="log"/> recibe solo códigos cerrados: nunca la
/// dirección, el código ni el texto de una excepción.
/// </summary>
public sealed class FamilyAccessCodeService(
    IFamilyAccessCodeStore store, IEmailSender sender, EmailSettings settings, FamilyAccessCodeKey key, TimeProvider clock,
    Action<string>? log = null, TimeSpan? sendTimeout = null)
{
    public static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(10);

    private const string LogName = "CODIGO_ACCESO_FAMILIA";

    public async Task<AccessCodeSend> SendAsync(
        string externalSubject, AccountId accountId, IReadOnlyList<ActiveProfileScope> scopes, CancellationToken ct = default)
    {
        if (!settings.Active || !key.IsUsable)
        {
            log?.Invoke($"{LogName}: no disponible ({(settings.Active ? "falta la clave" : "correo apagado o sin configurar")})");
            return new AccessCodeSend(AccessCodeSendOutcome.NotAvailable);
        }

        if (await store.FindAddressAsync(externalSubject, accountId, scopes, ct) is not { } address)
        {
            return new AccessCodeSend(AccessCodeSendOutcome.NoAddress);
        }

        var codeId = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
        var now = clock.GetUtcNow().UtcDateTime;
        var reservation = await store.ReserveAsync(new NewAccessCode(
            codeId, accountId, Digest(codeId, accountId, address, code), now, now + FamilyAccessCodeRules.Lifetime,
            settings.Language, AccessCodeTemplate.Version), ct);
        if (reservation != AccessCodeReservation.Reserved)
        {
            return new AccessCodeSend(reservation switch
            {
                AccessCodeReservation.Blocked => AccessCodeSendOutcome.Blocked,
                AccessCodeReservation.TooSoon => AccessCodeSendOutcome.TooSoon,
                AccessCodeReservation.HourLimit => AccessCodeSendOutcome.HourLimit,
                _ => AccessCodeSendOutcome.DayLimit,
            });
        }

        var result = !settings.AnyRecipient && !settings.AllowedRecipients.Contains(address, StringComparer.OrdinalIgnoreCase)
            ? EmailSendResult.Fail(EmailLog.RecipientNotAllowed)
            : await DeliverAsync(address, code);
        // El resultado se anota aunque el navegador corte la petición.
        await store.RecordSendAsync(
            codeId, result.OperationId, result.Accepted ? null : result.FailureCode, clock.GetUtcNow().UtcDateTime, CancellationToken.None);
        log?.Invoke($"{LogName}: {(result.Accepted ? EmailLog.Sent : $"{EmailLog.Failed} {result.FailureCode}")}");
        return result.Accepted
            ? new AccessCodeSend(AccessCodeSendOutcome.Sent, codeId)
            : new AccessCodeSend(AccessCodeSendOutcome.SendFailed);
    }

    /// <summary>La dirección se vuelve a leer: si ha cambiado desde que se mandó el código, ese código ya no vale.</summary>
    public async Task<AccessCodeCheck> VerifyAsync(
        string externalSubject, AccountId accountId, IReadOnlyList<ActiveProfileScope> scopes, Guid codeId, string code,
        CancellationToken ct = default)
    {
        if (!key.IsUsable || await store.FindAddressAsync(externalSubject, accountId, scopes, ct) is not { } address)
        {
            return AccessCodeCheck.NotLive;
        }

        return await store.RedeemAsync(codeId, accountId, Digest(codeId, accountId, address, code), clock.GetUtcNow().UtcDateTime, ct);
    }

    public Task ResetAsync(AccountId accountId, CancellationToken ct = default) =>
        store.ResetAsync(accountId, clock.GetUtcNow().UtcDateTime, ct);

    private byte[] Digest(Guid codeId, AccountId accountId, string address, string code) =>
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key.Value!),
            Encoding.UTF8.GetBytes($"{codeId:N}|{accountId.Value:N}|{address.Trim().ToLowerInvariant()}|{code}"));

    private async Task<EmailSendResult> DeliverAsync(string address, string code)
    {
        var timeout = sendTimeout ?? DefaultSendTimeout;
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var email = AccessCodeTemplate.Render(settings.Language, code);
            return await sender.SendAsync(
                new EmailMessage(address, email.Subject, email.PlainText, email.Html, email.InlineImages), cts.Token).WaitAsync(timeout);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            return EmailSendResult.Fail(EmailLog.TimedOut);
        }
        catch (Exception ex)
        {
            // Nunca la excepción entera: la de ACS puede llevar la dirección.
            log?.Invoke($"{LogName}: el proveedor falló ({ex.GetType().Name})");
            return EmailSendResult.Fail(EmailLog.ProviderUnavailable);
        }
    }
}
