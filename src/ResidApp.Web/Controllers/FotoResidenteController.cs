using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Foto de identificación del residente (estudio de mercado R2, script 0059): la imagen para las listas y fichas de Auxiliar, Enfermería,
/// Medicina y Administración, y el formulario para ponerla, cambiarla o quitarla desde las fichas de Administración y de Enfermería. Traduce a
/// ResidentPhotoApplicationService; la autorización no vive aquí.
/// </summary>
public sealed class FotoResidenteController(ResidentPhotoApplicationService photos) : Controller
{
    // Sin el reductor del navegador llega la foto tal cual de la cámara: se acepta una petición grande para responder con un mensaje claro
    // en vez de un error, pero solo se lee si cabe en ResidentPhoto.MaxBytes.
    private const long MaxRequestBytes = 20 * 1024 * 1024;

    private const string TooLarge = "La foto tiene que ser una imagen JPEG de 1 MB como máximo. Si la eliges desde el móvil, la app la reduce sola.";

    /// <summary>La imagen. Sin ámbito, fuera de él o sin foto, 404 sin distinguir el motivo (y nunca una redirección, que el &lt;img&gt;
    /// no sabría mostrar). La dirección cambia con cada foto nueva (?v=), así que el navegador puede guardarla un día.</summary>
    [HttpGet]
    public async Task<IActionResult> Ver(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || residenteId == Guid.Empty)
        {
            return NotFound();
        }

        var result = await photos.ReadAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId), ct);
        if (!result.Ok || result.Value is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        return File(result.Value, "image/jpeg");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Guardar([Bind(Prefix = "Form")] ResidentPhotoFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || form.ResidenteId == Guid.Empty)
        {
            return RedirectToAction("Index", "Home");
        }

        string? error = null;
        if (form.Foto is null || form.Foto.Length == 0)
        {
            error = "Elige una foto.";
        }
        else if (!form.Consentimiento)
        {
            error = "Marca la casilla del consentimiento para guardar la foto.";
        }
        else if (form.Foto.Length > ResidentPhoto.MaxBytes)
        {
            error = TooLarge;
        }
        else
        {
            using var content = new MemoryStream((int)form.Foto.Length);
            await form.Foto.CopyToAsync(content, ct);
            var result = await photos.SetAsync(new SetResidentPhotoCommand(
                activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId), content.ToArray(), true), ct);
            if (result.Ok)
            {
                TempData["Mensaje"] = "Foto guardada.";
            }
            else
            {
                error = result.Error!.Code == ApplicationFailureCode.InvalidInput ? TooLarge : result.Error.Message;
            }
        }

        if (error is not null)
        {
            TempData["FotoError"] = error;
        }
        return BackToRecord(activeScope, form.ResidenteId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Quitar(Guid residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null || residenteId == Guid.Empty)
        {
            return RedirectToAction("Index", "Home");
        }

        var result = await photos.RemoveAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId), ct);
        if (result.Ok)
        {
            TempData["Mensaje"] = result.Value ? "Foto quitada." : "El residente ya no tenía foto.";
        }
        else
        {
            TempData["FotoError"] = result.Error!.Message;
        }
        return BackToRecord(activeScope, residenteId);
    }

    /// <summary>Vuelve a la ficha desde la que se envió el formulario, según el perfil activo; el servidor ya ha comprobado el permiso.</summary>
    private IActionResult BackToRecord(ActiveProfileScopeCookieValue activeScope, Guid residenteId) => activeScope.Profile switch
    {
        SystemProfile.Administracion => RedirectToAction("Residente", "Administracion", new { residenteId }),
        SystemProfile.Enfermeria => RedirectToAction("Residente", "Enfermeria", new { residenteId }),
        _ => RedirectToAction("Index", "Home"),
    };
}
