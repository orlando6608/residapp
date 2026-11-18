using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.UseCases;
using ResidApp.Shared;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Citas externas para el Auxiliar (PR 3; docs/producto/citas-externas-diseno.md): las de hoy y mañana de sus residentes asignados, solo con lo que necesita para preparar la salida (hora, qué preparar,
/// quién acompaña y cómo va). La autorización no vive aquí: el caso de uso comprueba el ámbito y el perfil, y el servidor no devuelve especialidad, lugar, tipo, nota ni nombre de familiar.
/// </summary>
public sealed partial class AuxiliarController
{
    public async Task<IActionResult> CitasExternas(CancellationToken ct)
    {
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return RedirectToAction("Select", "ProfileScope", new { returnUrl = Url.Action(nameof(CitasExternas)) });
        }

        var result = await externalAppointments.ListForAuxiliarAsync(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ct);
        if (!result.Ok)
        {
            TempData["Error"] = result.Error!.Message;
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }
}
