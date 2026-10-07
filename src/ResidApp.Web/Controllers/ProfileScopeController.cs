using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Selección del ámbito activo (centro + perfil) de la cuenta autenticada, antes de operar (ADM-30: "si mi
/// cuenta tiene varios perfiles, debo seleccionar explícitamente cuál está activo"). Con un único ámbito
/// activo, se autoselecciona sin pedir confirmación. La selección solo se persiste en cookie como atajo de
/// UX: cada operación posterior revalida el par elegido contra SQL Server vía IAuthorizationEvidenceProvider.
/// </summary>
public sealed class ProfileScopeController(ListActiveProfileScopes listActiveProfileScopes, ClinicalAccessDeclarations declarations) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Select(string? returnUrl, CancellationToken ct)
    {
        var result = await listActiveProfileScopes.ExecuteAsync(ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new SelectProfileScopeViewModel { ReturnUrl = returnUrl });
        }

        var scopes = result.Value!;
        return scopes.Count == 1
            ? await SelectScopeAsync(scopes[0], returnUrl, ct)
            : View(ToViewModel(scopes, returnUrl));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(Guid profileScopeId, Guid centerId, string? returnUrl, CancellationToken ct)
    {
        var result = await listActiveProfileScopes.ExecuteAsync(ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(new SelectProfileScopeViewModel { ReturnUrl = returnUrl });
        }

        var scopes = result.Value!;
        var chosen = scopes.FirstOrDefault(s => s.ProfileScopeId == profileScopeId && s.CenterId.Value == centerId);
        if (chosen is null)
        {
            ModelState.AddModelError(string.Empty, "Ese ámbito ya no está disponible para tu cuenta.");
            return View(ToViewModel(scopes, returnUrl));
        }

        return await SelectScopeAsync(chosen, returnUrl, ct);
    }

    /// <summary>Cambiar de ámbito o de perfil termina las declaraciones de acceso clínico de la cuenta (CJ, 2026-10-06). Se terminan
    /// antes de escribir la cookie nueva, mientras la conexión aún lleva el ámbito anterior.</summary>
    private async Task<IActionResult> SelectScopeAsync(ActiveProfileScope scope, string? returnUrl, CancellationToken ct)
    {
        await declarations.EndAllAsync(ct);
        ClinicalAccessDeclarationCookie.Clear(Response);
        ActiveProfileScopeCookie.Write(Response, scope.ProfileScopeId, scope.CenterId.Value, scope.CenterName, scope.Profile);
        return returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");
    }

    private static SelectProfileScopeViewModel ToViewModel(IReadOnlyList<ActiveProfileScope> scopes, string? returnUrl) =>
        new()
        {
            Options = scopes
                .Select(s => new ProfileScopeOptionViewModel(s.ProfileScopeId, s.CenterId.Value, s.CenterName, SystemProfileDisplay.Label(s.Profile)))
                .ToList(),
            ReturnUrl = returnUrl,
        };
}
