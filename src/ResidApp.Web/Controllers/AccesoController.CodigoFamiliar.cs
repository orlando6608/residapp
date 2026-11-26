using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Infrastructure.Identity;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// El segundo paso de la familia: el código de 6 cifras que le llega por correo (ADR 0012, pieza 3). Todo falla cerrado: sin código válido no hay sesión, y nada aquí enseña la
/// dirección. Los mensajes de esta parte solo se ven después de acertar la contraseña (o de usar una invitación). Los códigos erróneos cuentan para el bloqueo de la cuenta y por
/// IP, como los de la aplicación de códigos, y además tienen sus propios límites (<see cref="FamilyAccessCodeRules"/>).
/// </summary>
public sealed partial class AccesoController
{
    public const string FamilyClosedMessage =
        "Ahora mismo no se puede entrar al portal de las familias. Llama a tu centro.";
    public const string FamilyNoAddressMessage =
        "Para entrar en el portal necesitas un correo en tu ficha. Pide a la Administración de tu centro que lo añada.";
    public const string FamilyBlockedMessage =
        "Por seguridad, para volver a entrar necesitas una invitación nueva. Pídela a la Administración de tu centro.";
    public const string CodeSentMessage =
        "Te hemos enviado un código a tu correo. Si no llega en unos minutos, mira el correo no deseado, pide otro o llama a tu centro.";
    public const string CodeNotSentMessage =
        "No hemos podido enviarte el código. Vuelve a intentarlo dentro de un minuto. Si sigue sin llegar, llama a tu centro.";
    public const string CodeTooSoonMessage =
        "Has pedido un código hace menos de un minuto. Espera un poco antes de pedir otro.";
    public const string CodeHourLimitMessage =
        "Has pedido demasiados códigos en la última hora. Espera un rato antes de pedir otro.";
    public const string CodeDayLimitMessage =
        "Has pedido demasiados códigos hoy. Vuelve a intentarlo mañana o llama a tu centro.";
    public const string WrongCodeMessage =
        "El código no es correcto. Usa el del último correo que te hemos enviado.";
    public const string DeadCodeMessage =
        "Este código ya no vale. Pide otro.";
    private const string CodeNoticeKey = "AccesoCodigoAviso";
    private const string CodeErrorKey = "AccesoCodigoError";

    [HttpGet]
    public async Task<IActionResult> CodigoCorreo(CancellationToken ct)
    {
        if (await ResolveEmailPendingAsync(ct) is null)
        {
            return PendingExpired();
        }
        Response.Headers.CacheControl = "no-store";
        if (TempData[CodeErrorKey] is string error)
        {
            ModelState.AddModelError(string.Empty, error);
        }
        return View(new CodigoCorreoViewModel(TempData[CodeNoticeKey] as string));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> CodigoCorreo(string? codigo, CancellationToken ct)
    {
        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        if (await ResolveEmailPendingAsync(ct) is not { } pending)
        {
            return PendingExpired();
        }

        var code = CleanCode(codigo);
        var result = pending.State.CodeId is { } codeId && code.Length > 0
            ? await familyCodes.VerifyAsync(pending.Account.UserName, AccountId.From(pending.Account.Id), pending.Scopes, codeId, code, ct)
            : AccessCodeCheck.NotLive;
        switch (result)
        {
            case AccessCodeCheck.Valid:
                return await CompleteAsync(pending, ct);
            case AccessCodeCheck.NotLive:
                return CodeStep(error: DeadCodeMessage);
        }

        await FailAsync(pending.Account, AccessEventType.SegundoFactorFallido, ct);
        if (result == AccessCodeCheck.Blocked)
        {
            logger.LogWarning("Código de acceso de familia: cuenta bloqueada hasta una invitación nueva.");
            PendingAccessCookie.Clear(Response);
            return FamilyCannotEnter(FamilyBlockedMessage);
        }
        if (await users.IsLockedOutAsync(pending.Account))
        {
            PendingAccessCookie.Clear(Response);
            return FamilyCannotEnter("Has superado los intentos permitidos: la cuenta queda bloqueada 15 minutos.");
        }

        // El código que agota sus 5 intentos no llega aquí: ese quinto fallo bloquea antes la cuenta.
        return CodeStep(error: WrongCodeMessage);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AccesoOptions.RateLimitPolicy)]
    public async Task<IActionResult> ReenviarCodigo(CancellationToken ct)
    {
        if (TooManyFailures())
        {
            return TooManyFailuresResult();
        }

        return await ResolveEmailPendingAsync(ct) is { } pending
            ? await SendFamilyCodeAsync(pending.Account, pending.Scopes, pending.State.ReturnUrl, ct)
            : PendingExpired();
    }

    private static bool IsFamilyOnly(IReadOnlyList<ActiveProfileScope> scopes) => scopes.All(scope => scope.Profile == SystemProfile.Familiar);

    /// <summary>El paso de familia solo vale mientras la cuenta siga siendo solo de familia, sin aplicación de códigos y con el código encendido.</summary>
    private async Task<Pending?> ResolveEmailPendingAsync(CancellationToken ct) =>
        familyAccess.Entry == FamilyEntry.EmailCode
        && await ResolvePendingAsync(ct, emailCode: true) is { Account.TwoFactorEnabled: false } pending && IsFamilyOnly(pending.Scopes)
            ? pending
            : null;

    /// <summary>Pide un código y deja a la persona en la pantalla donde escribirlo, también si ahora no se puede mandar otro (así reintenta sin repetir la contraseña).</summary>
    private async Task<IActionResult> SendFamilyCodeAsync(
        CredentialAccount account, IReadOnlyList<ActiveProfileScope> scopes, string? returnUrl, CancellationToken ct)
    {
        var send = await familyCodes.SendAsync(account.UserName, AccountId.From(account.Id), scopes, ct);
        switch (send.Outcome)
        {
            case AccessCodeSendOutcome.NotAvailable:
                return FamilyCannotEnter(FamilyClosedMessage);
            case AccessCodeSendOutcome.NoAddress:
                return FamilyCannotEnter(FamilyNoAddressMessage);
            case AccessCodeSendOutcome.Blocked:
                return FamilyCannotEnter(FamilyBlockedMessage);
        }

        // Sin código nuevo, este navegador conserva el que ya tenía (sigue valiendo si no ha caducado).
        var previous = pendingCookie.Read(Request) is { EmailCode: true } state && state.AccountId == account.Id ? state.CodeId : null;
        pendingCookie.Write(
            Response, account.Id, account.SecurityStamp ?? string.Empty, returnUrl, emailCode: true,
            codeId: send.Outcome == AccessCodeSendOutcome.SendFailed ? null : send.CodeId ?? previous);
        return send.Outcome switch
        {
            AccessCodeSendOutcome.Sent => CodeStep(notice: CodeSentMessage),
            AccessCodeSendOutcome.TooSoon => CodeStep(error: CodeTooSoonMessage),
            AccessCodeSendOutcome.HourLimit => CodeStep(error: CodeHourLimitMessage),
            AccessCodeSendOutcome.DayLimit => CodeStep(error: CodeDayLimitMessage),
            _ => CodeStep(error: CodeNotSentMessage),
        };
    }

    private RedirectToActionResult CodeStep(string? notice = null, string? error = null)
    {
        TempData[CodeNoticeKey] = notice;
        TempData[CodeErrorKey] = error;
        return RedirectToAction(nameof(CodigoCorreo));
    }

    private RedirectToActionResult FamilyCannotEnter(string message)
    {
        TempData[AvisoKey] = message;
        return RedirectToAction(nameof(Entrar));
    }
}
