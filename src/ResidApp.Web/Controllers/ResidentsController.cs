using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Models;

namespace ResidApp.Web.Controllers;

/// <summary>Primera pantalla real del vertical Residente/Basal: alta de residente. Traduce el formulario
/// directamente a CreateResidentCommand; la autorización y las reglas de negocio viven en
/// ResidentBaselineApplicationService, no aquí.</summary>
public sealed class ResidentsController(ResidentBaselineApplicationService service) : Controller
{
    public IActionResult Create() => View(new CreateResidentFormModel { OperacionId = Guid.NewGuid() });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateResidentFormModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var command = new CreateResidentCommand(
            form.AmbitoPerfilId, CenterId.From(form.CentroId), UnitId.From(form.UnidadId), form.NombreVisible,
            form.FechaNacimiento!.Value, form.SexoDocumentadoCodigo, form.ReferenciaInterna,
            EdificioId: null, PlantaId: null, HabitacionId: null, PlazaId: null, form.OperacionId);

        var result = await service.CreateResidentAsync(command, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error!.Message);
            return View(form);
        }

        TempData["ResidentId"] = result.Value!.ResidentId.Value.ToString();
        return RedirectToAction(nameof(Confirmation));
    }

    public IActionResult Confirmation()
    {
        ViewBag.ResidentId = TempData["ResidentId"];
        return View();
    }
}
