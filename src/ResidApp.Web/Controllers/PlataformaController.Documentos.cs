using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Sección «Documentos» de Plataforma: documentos de trabajo (diseños, guías, especificaciones) pasados a HTML. Solo los lee quien tiene ahora un ámbito activo de
/// Plataforma (se comprueba contra la base de datos, no por la cookie del ámbito). Ningún documento lleva datos de residentes ni de personal.
/// </summary>
public sealed partial class PlataformaController
{
    private const string NoAccess = "No tienes acceso a esta pantalla.";

    public async Task<IActionResult> Documentos(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Documentos)) });
        }

        var access = await service.CheckDocumentsAccessAsync(Query(activeScope), ct);
        if (!access.Ok)
        {
            ModelState.AddModelError(string.Empty, NoAccess);
            return View(new PlatformDocumentsViewModel([]));
        }

        return View(new PlatformDocumentsViewModel(documents.All));
    }

    /// <summary>La página de un documento, tal cual está generada. El identificador se busca en el catálogo: de la petición no se forma ninguna ruta de fichero.</summary>
    public async Task<IActionResult> Documento(string? id, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Documento), new { id }) });
        }

        var access = await service.CheckDocumentsAccessAsync(Query(activeScope), ct);
        if (!access.Ok)
        {
            return RedirectToAction(nameof(Documentos));
        }

        var document = documents.Find(id);
        return document is null
            ? NotFound()
            : PhysicalFile(documents.PagePath(document), "text/html; charset=utf-8");
    }
}
