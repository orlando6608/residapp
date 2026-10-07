using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Authorization;
using ResidApp.Application.Errors;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Cableado de los otros dos casos de uso ya construidos del vertical Residente/Basal: firma de basal y
/// lectura auditada para Dirección Clínica. La firma ya no tiene pantalla propia: se firma desde
/// EnfermeriaBasal/Confirmar (Enfermería y Medicina), que envía aquí el borrador en campos ocultos; un fallo
/// vuelve a esa confirmación con el mensaje.
/// </summary>
public sealed class BaselineController(
    ResidentBaselineApplicationService service, DireccionApplicationService direccionService, ClinicalAccessDeclarations declarations) : Controller
{
    public IActionResult Sign() => BackToResidents();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(SignBaselineFormModel form, CancellationToken ct)
    {
        // Los campos los rellena la confirmación: si faltan, la petición no viene de ella.
        if (!ModelState.IsValid)
        {
            return BackToResidents();
        }

        var command = new SignBaselineCommand(
            form.AmbitoPerfilId!.Value, CenterId.From(form.CentroId!.Value), ResidentId.From(form.ResidenteId!.Value),
            BaselineDraftId.From(form.BorradorId!.Value), form.RevisionBorradorEsperada, form.OperacionId);

        var result = await service.SignBaselineAsync(command, ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction("Confirmar", "EnfermeriaBasal", new { residenteId = form.ResidenteId });
        }

        ViewBag.VersionNumber = result.Value!.VersionNumber;
        ViewBag.BaselineVersionId = result.Value.BaselineVersionId.Value;
        ViewBag.ResidenteId = form.ResidenteId;
        return View("Signed");
    }

    /// <summary>Con <paramref name="residenteId"/> (el enlace «Consultar otro recurso de este residente») la pantalla sabe si
    /// ya hay una declaración vigente para él y, entonces, no vuelve a pedir finalidad ni justificación.</summary>
    public async Task<IActionResult> Direction(Guid? residenteId, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Direction)) });
        }

        await ShowResidentsAsync(activeScope, ct);
        var form = new DirectionBaselineQueryModel { OperacionId = Guid.NewGuid(), ResidenteId = residenteId };
        ViewBag.Declaracion = residenteId is { } id ? await FindDeclarationAsync(activeScope, id, ct) : null;
        return View(form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Direction(DirectionBaselineQueryModel form, CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(Direction)) });
        }

        var declaration = form.ResidenteId is { } residentGuid ? await FindDeclarationAsync(activeScope, residentGuid, ct) : null;
        if (declaration is null)
        {
            // Sin declaración vigente para este residente hay que declarar finalidad y justificación (CJ, 2026-10-06).
            if (!EnumCode.TryParseCode<ClinicalDetailAccessPurpose>(form.Proposito ?? string.Empty, out _))
            {
                ModelState.AddModelError(nameof(form.Proposito), "Elige la finalidad del acceso.");
            }
            if (string.IsNullOrWhiteSpace(form.Justificacion))
            {
                ModelState.AddModelError(nameof(form.Justificacion), "Escribe una justificación breve del acceso.");
            }
        }

        if (!ModelState.IsValid)
        {
            await ShowResidentsAsync(activeScope, ct);
            ViewBag.Declaracion = declaration;
            return View(form);
        }

        var command = new ReadDirectionBaselineCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(form.ResidenteId!.Value),
            form.TipoRecurso, form.Proposito, form.OperacionId, form.Justificacion, declaration?.Id);

        var result = await service.ReadDirectionBaselineAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Code switch
            {
                ApplicationFailureCode.Conflict when declaration is not null =>
                    "La declaración de acceso de este residente ya no está vigente. Indica de nuevo la finalidad y la justificación.",
                ApplicationFailureCode.InvalidInput =>
                    "Revisa la justificación: es obligatoria y admite hasta 300 caracteres.",
                _ => result.Error.Message,
            });
            await ShowResidentsAsync(activeScope, ct);
            ViewBag.Declaracion = null;
            return View(form);
        }

        // La declaración nueva se identifica con el OperacionId de esta lectura: se recuerda para el resto de pantallas del residente.
        if (declaration is null)
        {
            ClinicalAccessDeclarationCookie.Write(Response, form.OperacionId);
        }
        ViewBag.Headers = result.Value!.Headers;
        ViewBag.Content = result.Value.Content;
        ViewBag.Timeline = result.Value.Timeline;
        ViewBag.ClosedEvents = result.Value.ClosedEvents;
        ViewBag.Declaracion = declaration ?? await FindDeclarationAsync(activeScope, form.ResidenteId!.Value, ct, form.OperacionId);
        return View("DirectionResult", form);
    }

    /// <summary>La declaración vigente para este residente, según la cookie (o el id indicado, recién creado); null si no hay.</summary>
    private async Task<ClinicalAccessDeclaration?> FindDeclarationAsync(
        ActiveProfileScopeCookieValue activeScope, Guid residentId, CancellationToken ct, Guid? declarationId = null)
    {
        var id = declarationId ?? ClinicalAccessDeclarationCookie.Read(Request);
        if (id is null)
        {
            return null;
        }
        var result = await declarations.FindActiveAsync(activeScope.ProfileScopeId, ResidentId.From(residentId), id.Value, ct);
        return result.Ok ? result.Value : null;
    }

    private async Task ShowResidentsAsync(ActiveProfileScopeCookieValue activeScope, CancellationToken ct)
    {
        ViewBag.AmbitoCentroNombre = activeScope.CenterName;
        ViewBag.AmbitoPerfilLabel = SystemProfileDisplay.Label(activeScope.Profile);
        var result = await direccionService.ListResidentsAsync(
            new SupervisionQuery(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
        }
        ViewBag.Residentes = result.Value ?? [];
    }

    private IActionResult BackToResidents() =>
        RedirectToAction("Residentes", BaselineModuleDisplay.ProfileController(Request));
}
