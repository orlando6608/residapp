using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Cableado de los otros dos casos de uso ya construidos del vertical Residente/Basal: firma de basal y
/// lectura auditada para Dirección Clínica. Aviso: ni este puerto ni el prototipo legado tienen todavía un
/// caso de uso para autorizar/crear el contenido de un borrador de basal (las 9 áreas más Barthel) — sin
/// un borrador previo sembrado directamente en base de datos, Sign siempre devolverá un fallo de
/// aplicación. No se ha improvisado ese sembrado a mano: replicaría en SQL una lógica de negocio (el
/// trigger maestro de validación de 7 comprobaciones) que todavía no está diseñada en C#.
/// </summary>
public sealed class BaselineController(ResidentBaselineApplicationService service) : Controller
{
    public IActionResult Sign() => View(new SignBaselineFormModel { OperacionId = Guid.NewGuid() });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sign(SignBaselineFormModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var command = new SignBaselineCommand(
            form.AmbitoPerfilId, CenterId.From(form.CentroId), ResidentId.From(form.ResidenteId),
            BaselineDraftId.From(form.BorradorId), form.RevisionBorradorEsperada, form.OperacionId);

        var result = await service.SignBaselineAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(form);
        }

        ViewBag.VersionNumber = result.Value!.VersionNumber;
        ViewBag.BaselineVersionId = result.Value.BaselineVersionId.Value;
        return View("Signed");
    }

    public IActionResult Direction() => View(new DirectionBaselineQueryModel { OperacionId = Guid.NewGuid() });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Direction(DirectionBaselineQueryModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var command = new ReadDirectionBaselineCommand(
            form.AmbitoPerfilId, CenterId.From(form.CentroId), ResidentId.From(form.ResidenteId),
            form.TipoRecurso, form.Proposito, form.OperacionId);

        var result = await service.ReadDirectionBaselineAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(form);
        }

        ViewBag.Headers = result.Value;
        return View("DirectionResult", form);
    }
}
