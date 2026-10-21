using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Errors;
using ResidApp.Application.Intercambio;
using ResidApp.Application.UseCases;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// ADR 0010 (estudio de mercado R1): importar la estructura y los residentes desde un fichero de otra aplicación, y exportar la estructura
/// y el censo. Los pasos de importar (analizar, vista previa, importar) y el análisis de una plantilla de exportación los pide
/// wwwroot/js/intercambio.js con fetch, reenviando el fichero cada vez, y devuelven un fragmento de la página; el fichero no se guarda.
/// Traduce a AdministracionIntercambioApplicationService; la autorización no vive aquí.
/// </summary>
public sealed partial class AdministracionController
{
    /// <summary>El fichero más el resto del formulario.</summary>
    private const long MaxExchangeRequestBytes = ExchangeLimits.MaxBytes + 1024 * 1024;

    public IActionResult Importar()
    {
        if (ActiveProfileScopeCookie.Read(Request) is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Importar)) });
        }

        return View();
    }

    /// <summary>La plantilla vacía de ResidApp para rellenar e importar. No lleva datos.</summary>
    public IActionResult PlantillaImportacion(ExchangeEntity entidad)
    {
        var file = AdministracionIntercambioApplicationService.ImportTemplate(entidad);
        return File(file.Content, "text/csv", file.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExchangeRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxExchangeRequestBytes)]
    public async Task<IActionResult> ImportarAnalizar(ExchangeFormModel form, CancellationToken ct)
    {
        var (query, content, refused) = await ReadExchangeFileAsync(form, ct);
        if (refused is not null)
        {
            return refused;
        }

        var result = await intercambio.AnalyzeImportAsync(query!, form.Entidad, content!, form.Input(), ct);
        return result.Ok ? PartialView("_ImportarEmparejamiento", result.Value!) : ExchangeError(result.Error!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExchangeRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxExchangeRequestBytes)]
    public async Task<IActionResult> ImportarVistaPrevia(ExchangeFormModel form, CancellationToken ct)
    {
        var (query, content, refused) = await ReadExchangeFileAsync(form, ct);
        if (refused is not null)
        {
            return refused;
        }

        var result = await intercambio.PreviewImportAsync(query!, form.Entidad, content!, form.Input() ?? EmptyInput, ct);
        return result.Ok ? PartialView("_ImportarInforme", result.Value!) : ExchangeError(result.Error!);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExchangeRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxExchangeRequestBytes)]
    public async Task<IActionResult> ImportarEjecutar(ExchangeFormModel form, CancellationToken ct)
    {
        var (query, content, refused) = await ReadExchangeFileAsync(form, ct);
        if (refused is not null)
        {
            return refused;
        }

        var result = await intercambio.RunImportAsync(query!, form.Entidad, content!, form.Input() ?? EmptyInput, form.NombrePlantilla, ct);
        return result.Ok ? PartialView("_ImportarInforme", result.Value!) : ExchangeError(result.Error!);
    }

    /// <summary>plantillaGuardada es el nombre de la plantilla que se acaba de guardar, para el mensaje (no va en TempData porque
    /// intercambio.js sigue la redirección con fetch y la consumiría antes de navegar).</summary>
    public async Task<IActionResult> Exportar(string? plantillaGuardada, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Exportar)) });
        }

        var templates = await intercambio.ListExportTemplatesAsync(Query(activeScope), ct);
        if (!templates.Ok)
        {
            ModelState.AddModelError(string.Empty, templates.Error!.Message);
        }

        return View(new ExportViewModel(templates.Value ?? [],
            templates.Value?.Any(t => t.Name == plantillaGuardada) == true ? plantillaGuardada : null));
    }

    /// <summary>Descarga la estructura o el censo, con las columnas de ResidApp o con las de una plantilla del centro. Queda auditado.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(Exportar))]
    public async Task<IActionResult> ExportarDescargar(ExchangeEntity entidad, Guid? plantillaId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope");
        }

        var result = await intercambio.ExportAsync(Query(activeScope), entidad, plantillaId, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Exportar));
        }

        return File(result.Value!.Content, "text/csv", result.Value.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExchangeRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxExchangeRequestBytes)]
    public async Task<IActionResult> ExportarPlantillaAnalizar(ExchangeFormModel form, CancellationToken ct)
    {
        var (query, content, refused) = await ReadExchangeFileAsync(form, ct);
        if (refused is not null)
        {
            return refused;
        }

        var result = await intercambio.AnalyzeExportTemplateAsync(query!, form.Entidad, content!, form.Input(), ct);
        return result.Ok ? PartialView("_ExportarEmparejamiento", result.Value!) : ExchangeError(result.Error!);
    }

    /// <summary>Guarda la plantilla de exportación y vuelve a la pantalla de exportar, donde ya aparece (intercambio.js sigue la
    /// redirección).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxExchangeRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxExchangeRequestBytes)]
    public async Task<IActionResult> ExportarPlantillaGuardar(ExchangeFormModel form, CancellationToken ct)
    {
        var (query, content, refused) = await ReadExchangeFileAsync(form, ct);
        if (refused is not null)
        {
            return refused;
        }

        var result = await intercambio.SaveExportTemplateAsync(query!, form.Entidad, content!, form.Input() ?? EmptyInput, form.NombrePlantilla, ct);
        if (!result.Ok)
        {
            return ExchangeError(result.Error!);
        }

        return RedirectToAction(nameof(Exportar), new { plantillaGuardada = form.NombrePlantilla!.Trim() });
    }

    private static readonly ExchangeMappingInput EmptyInput = new([], null, []);

    /// <summary>El ámbito activo y el contenido del fichero; si falta alguno, la respuesta que lo explica (un fragmento con el aviso).</summary>
    private async Task<(AdministracionQuery? Query, byte[]? Content, IActionResult? Refused)> ReadExchangeFileAsync(
        ExchangeFormModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return (null, null, ExchangeNotice("Tu sesión no tiene un ámbito activo: vuelve a elegirlo y repite la operación.", StatusCodes.Status401Unauthorized));
        }

        if (form.Fichero is not { Length: > 0 } file)
        {
            return (null, null, ExchangeNotice("Elige un fichero.", StatusCodes.Status400BadRequest));
        }

        if (file.Length > ExchangeLimits.MaxBytes)
        {
            return (null, null, ExchangeNotice("El fichero es demasiado grande: como mucho 5 MB.", StatusCodes.Status400BadRequest));
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return (Query(activeScope), buffer.ToArray(), null);
    }

    private IActionResult ExchangeError(ApplicationFailure error) => error.Code switch
    {
        ApplicationFailureCode.AccessDenied => ExchangeNotice("Esto solo se puede hacer con un ámbito activo de Administración.", StatusCodes.Status403Forbidden),
        ApplicationFailureCode.InvalidInput => ExchangeNotice(error.Message, StatusCodes.Status400BadRequest),
        _ => ExchangeNotice("No se ha podido completar ahora mismo. Inténtalo de nuevo en unos minutos.", StatusCodes.Status503ServiceUnavailable),
    };

    private PartialViewResult ExchangeNotice(string message, int status)
    {
        var view = PartialView("_IntercambioAviso", message);
        view.StatusCode = status;
        return view;
    }
}
