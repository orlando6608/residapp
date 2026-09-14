using Microsoft.AspNetCore.Mvc;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Baseline;
using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;
using ResidApp.Web.Models;
using ResidApp.Web.Security;

namespace ResidApp.Web.Controllers;

/// <summary>
/// Vertical Enfermería, grupo E2: ENF-19 (alta, ya cubierta por ResidentsController/Create — Enfermería ya
/// tiene permiso allí), ENF-20 (crear borrador y completar las nueve áreas), ENF-21 (Barthel) y ENF-22
/// (confirmación, que reutiliza sin cambios BaselineController/Sign ya existente). Traduce a
/// ResidentBaselineApplicationService (contenido del borrador) y EnfermeriaApplicationService (resolución
/// del residente del ámbito); las reglas de negocio no viven aquí.
/// </summary>
public sealed class EnfermeriaBasalController(ResidentBaselineApplicationService baselineService, EnfermeriaApplicationService enfermeriaService) : Controller
{
    public async Task<IActionResult> Draft(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }
        var (activeScope, resident) = resolved.Value;

        var draftResult = await baselineService.LoadBaselineDraftAsync(
            new LoadBaselineDraftCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), resident.ResidentId), ct);
        if (!draftResult.Ok)
        {
            TempData["Error"] = draftResult.Error!.Message;
        }

        ViewBag.CreateForm = new CreateBaselineDraftFormModel { ResidenteId = residenteId, OperacionId = Guid.NewGuid() };
        ViewBag.CancelForm = new CancelBaselineDraftFormModel { ResidenteId = residenteId };
        return View(new BaselineDraftHubViewModel(resident, draftResult.Ok ? draftResult.Value : null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearBorrador(CreateBaselineDraftFormModel createForm, CancellationToken ct)
    {
        var resolved = await ResolveAsync(createForm.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }
        if (!ModelState.IsValid || (createForm.FuenteInformacionComun == InformationSourceCode.Otra && string.IsNullOrWhiteSpace(createForm.FuenteInformacionComunOtroTexto)))
        {
            TempData["Error"] = "Revisa el motivo, la fuente y la fecha de la información antes de crear el borrador.";
            return RedirectToAction(nameof(Draft), new { residenteId = createForm.ResidenteId });
        }

        var activeScope = resolved.Value.Scope;
        var command = new CreateBaselineDraftCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), resolved.Value.Resident.ResidentId, createForm.Motivo!.Value,
            createForm.FuenteInformacionComun!.Value, createForm.FuenteInformacionComunOtroTexto, createForm.FechaInformacionComun!.Value, createForm.OperacionId);
        var result = await baselineService.CreateBaselineDraftAsync(command, ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? "Borrador creado. Completa ahora las nueve áreas y el índice de Barthel."
            : result.Error!.Message;
        return RedirectToAction(nameof(Draft), new { residenteId = createForm.ResidenteId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarBorrador(CancelBaselineDraftFormModel cancelForm, CancellationToken ct)
    {
        var resolved = await ResolveAsync(cancelForm.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Indica un motivo para cancelar el borrador.";
            return RedirectToAction(nameof(Draft), new { residenteId = cancelForm.ResidenteId });
        }

        var activeScope = resolved.Value.Scope;
        var result = await baselineService.CancelBaselineDraftAsync(
            new CancelBaselineDraftCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), resolved.Value.Resident.ResidentId, cancelForm.Motivo), ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok ? "Borrador cancelado." : result.Error!.Message;
        return RedirectToAction(nameof(Draft), new { residenteId = cancelForm.ResidenteId });
    }

    public async Task<IActionResult> Area(Guid residenteId, BaselineArea areaCode, CancellationToken ct)
    {
        var resolved = await ResolveAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }

        var draftResult = await baselineService.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(
            resolved.Value.Scope.ProfileScopeId, CenterId.From(resolved.Value.Scope.CenterId), resolved.Value.Resident.ResidentId), ct);
        if (!draftResult.Ok || draftResult.Value is null)
        {
            TempData["Error"] = "No tienes un borrador activo para este residente.";
            return RedirectToAction(nameof(Draft), new { residenteId });
        }

        var existing = draftResult.Value.Areas.FirstOrDefault(a => a.AreaCode == areaCode);
        var form = existing is null
            ? new BaselineAreaFormModel { ResidenteId = residenteId, AreaCode = areaCode }
            : FillFromAnswer(residenteId, areaCode, existing.Answer, existing.Observation);
        return View(form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Area(BaselineAreaFormModel form, CancellationToken ct)
    {
        var resolved = await ResolveAsync(form.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }

        IBaselineAreaAnswer answer;
        try
        {
            answer = BuildAnswer(form);
        }
        catch (DomainValidationException error)
        {
            TempData["Error"] = $"Revisa los datos del área: {error.Message}.";
            return View(form);
        }

        var activeScope = resolved.Value.Scope;
        var command = new SaveBaselineDraftAreaCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), resolved.Value.Resident.ResidentId, form.AreaCode, answer, form.Observacion);
        var result = await baselineService.SaveBaselineDraftAreaAsync(command, ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok
            ? $"Área «{BaselineAreaDisplay.Label(form.AreaCode)}» guardada."
            : result.Error!.Message;
        return result.Ok ? RedirectToAction(nameof(Draft), new { residenteId = form.ResidenteId }) : View(form);
    }

    public async Task<IActionResult> Barthel(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }

        var draftResult = await baselineService.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(
            resolved.Value.Scope.ProfileScopeId, CenterId.From(resolved.Value.Scope.CenterId), resolved.Value.Resident.ResidentId), ct);
        if (!draftResult.Ok || draftResult.Value is null)
        {
            TempData["Error"] = "No tienes un borrador activo para este residente.";
            return RedirectToAction(nameof(Draft), new { residenteId });
        }

        var barthel = draftResult.Value.Barthel;
        var form = new BarthelFormModel
        {
            ResidenteId = residenteId,
            FechaValoracion = barthel.AssessmentDate ?? DateOnly.FromDateTime(DateTime.Today),
            Opciones = barthel.Items.ToDictionary(i => i.ItemCode.ToCode(), i => i.SelectedOptionCode),
        };
        return View(form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Barthel(BarthelFormModel form, CancellationToken ct)
    {
        var resolved = await ResolveAsync(form.ResidenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }

        var items = new List<BarthelItem>();
        foreach (var item in Enum.GetValues<BarthelItemCode>())
        {
            if (!form.Opciones.TryGetValue(item.ToCode(), out var optionCode) || string.IsNullOrWhiteSpace(optionCode))
            {
                TempData["Error"] = "Elige una opción para cada uno de los diez ítems.";
                return View(form);
            }
            int score;
            try
            {
                score = BarthelCatalog.ScoreOf(item, optionCode);
            }
            catch (DomainValidationException)
            {
                TempData["Error"] = "Una de las opciones elegidas no es válida.";
                return View(form);
            }
            items.Add(new BarthelItem(item, optionCode, score));
        }

        var activeScope = resolved.Value.Scope;
        var command = new SaveBaselineDraftBarthelCommand(
            activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), resolved.Value.Resident.ResidentId, form.FechaValoracion!.Value, items);
        var result = await baselineService.SaveBaselineDraftBarthelAsync(command, ct);

        TempData[result.Ok ? "Mensaje" : "Error"] = result.Ok ? "Índice de Barthel guardado." : result.Error!.Message;
        return result.Ok ? RedirectToAction(nameof(Draft), new { residenteId = form.ResidenteId }) : View(form);
    }

    public async Task<IActionResult> Confirmar(Guid residenteId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(residenteId, ct);
        if (resolved is null)
        {
            return RedirectToAction(nameof(EnfermeriaController.Residentes), "Enfermeria");
        }

        var draftResult = await baselineService.LoadBaselineDraftAsync(new LoadBaselineDraftCommand(
            resolved.Value.Scope.ProfileScopeId, CenterId.From(resolved.Value.Scope.CenterId), resolved.Value.Resident.ResidentId), ct);
        if (!draftResult.Ok || draftResult.Value is null)
        {
            TempData["Error"] = "No tienes un borrador activo para este residente.";
            return RedirectToAction(nameof(Draft), new { residenteId });
        }

        ViewBag.Resident = resolved.Value.Resident;
        ViewBag.ActiveScope = resolved.Value.Scope;
        return View(draftResult.Value);
    }

    private async Task<(ActiveProfileScopeCookieValue Scope, ScopeResidentSummary Resident)?> ResolveAsync(Guid residenteId, CancellationToken ct)
    {
        if (residenteId == Guid.Empty)
        {
            return null;
        }
        var activeScope = ActiveProfileScopeCookie.Read(Request);
        if (activeScope is null)
        {
            return null;
        }
        var findResult = await enfermeriaService.FindScopeResidentAsync(
            new FindScopeResidentCommand(activeScope.ProfileScopeId, CenterId.From(activeScope.CenterId), ResidentId.From(residenteId)), ct);
        return !findResult.Ok || findResult.Value is null ? null : (activeScope, findResult.Value);
    }

    private static BaselineAreaFormModel FillFromAnswer(Guid residenteId, BaselineArea areaCode, IBaselineAreaAnswer answer, string? observation)
    {
        var form = new BaselineAreaFormModel { ResidenteId = residenteId, AreaCode = areaCode, Observacion = observation };
        switch (answer)
        {
            case MobilityAreaAnswer a:
                form.DisplacementModeCode = a.DisplacementModeCode;
                form.TechnicalAidCode = a.TechnicalAidCode;
                form.TechnicalAidOtherText = a.TechnicalAidOtherText;
                form.TransferCode = a.TransferCode;
                break;
            case FeedingAreaAnswer a:
                form.RouteCode = a.RouteCode;
                form.FoodTextureCode = a.FoodTextureCode;
                form.FoodTextureOtherText = a.FoodTextureOtherText;
                form.LiquidConsistencyCode = a.LiquidConsistencyCode;
                form.AssistanceCode = a.AssistanceCode;
                form.SwallowingPrecautionsCode = a.SwallowingPrecautionsCode;
                form.SwallowingPrecautionsText = a.SwallowingPrecautionsText;
                break;
            case ContinenceAreaAnswer a:
                form.UrinationCode = a.UrinationCode;
                form.BowelCode = a.BowelCode;
                form.ManagementCodes = a.ManagementCodes.ToList();
                form.ManagementOtherText = a.ManagementOtherText;
                break;
            case PersonalCareAreaAnswer a:
                form.PersonalCareAssistanceCode = a.PersonalCareAssistanceCode;
                form.BathingAssistanceCode = a.BathingAssistanceCode;
                break;
            case CognitionAreaAnswer a:
                form.CategoryCode = a.CategoryCode;
                form.EtiologyCode = a.EtiologyCode;
                form.EtiologyOtherText = a.EtiologyOtherText;
                form.GdsCode = a.GdsCode;
                form.ClinicalReferenceSourceCode = a.ClinicalReferenceSourceCode;
                form.ClinicalReferenceSourceOtherText = a.ClinicalReferenceSourceOtherText;
                form.ClinicalReferenceDate = a.ClinicalReferenceDate;
                break;
            case CommunicationAreaAnswer a:
                form.ComprehensionCode = a.ComprehensionCode;
                form.ExpressionCode = a.ExpressionCode;
                form.UsualFormsCodes = a.UsualFormsCodes.ToList();
                form.UsualFormOtherText = a.UsualFormOtherText;
                break;
            case BehaviorAreaAnswer a:
                form.StatusCode = a.StatusCode;
                form.PatternCodes = a.PatternCodes.ToList();
                form.PatternOtherText = a.PatternOtherText;
                break;
            case SleepAreaAnswer a:
                form.SleepPatternCodes = a.PatternCodes.ToList();
                break;
            case UsualAidsAreaAnswer a:
                form.AidCodes = a.AidCodes.ToList();
                form.OtherSupportProductText = a.OtherSupportProductText;
                form.OtherSupportText = a.OtherSupportText;
                break;
        }
        return form;
    }

    private static IBaselineAreaAnswer BuildAnswer(BaselineAreaFormModel form) => form.AreaCode switch
    {
        BaselineArea.Movilidad => new MobilityAreaAnswer(
            form.DisplacementModeCode ?? MobilityDisplacementCode.NoDocumentado, form.TechnicalAidCode ?? MobilityAidCode.NoDocumentado,
            form.TechnicalAidOtherText, form.TransferCode ?? MobilityTransferCode.NoDocumentado),
        BaselineArea.Alimentacion => new FeedingAreaAnswer(
            form.RouteCode ?? FeedingRouteCode.NoDocumentado, form.FoodTextureCode ?? FoodTextureCode.NoDocumentado, form.FoodTextureOtherText,
            form.LiquidConsistencyCode ?? LiquidConsistencyCode.NoDocumentado, form.AssistanceCode ?? FeedingAssistanceCode.NoDocumentado,
            form.SwallowingPrecautionsCode ?? SwallowingPrecautionsCode.NoDocumentado, form.SwallowingPrecautionsText),
        BaselineArea.Continencia => new ContinenceAreaAnswer(
            form.UrinationCode ?? ContinenceValueCode.NoDocumentado, form.BowelCode ?? ContinenceValueCode.NoDocumentado,
            form.ManagementCodes.Count == 0 ? [ContinenceManagementCode.NoDocumentado] : form.ManagementCodes, form.ManagementOtherText),
        BaselineArea.AseoHigiene => new PersonalCareAreaAnswer(
            form.PersonalCareAssistanceCode ?? PersonalCareCode.NoDocumentado, form.BathingAssistanceCode ?? BathingCode.NoDocumentado),
        BaselineArea.Cognicion => new CognitionAreaAnswer(
            form.CategoryCode ?? CognitionCategoryCode.SituacionNoDeterminada, form.EtiologyCode, form.EtiologyOtherText, form.GdsCode,
            form.ClinicalReferenceSourceCode, form.ClinicalReferenceSourceOtherText, form.ClinicalReferenceDate),
        BaselineArea.Comunicacion => new CommunicationAreaAnswer(
            form.ComprehensionCode ?? ComprehensionCode.NoDocumentado, form.ExpressionCode ?? ExpressionCode.NoDocumentado,
            form.UsualFormsCodes.Count == 0 ? [CommunicationFormCode.NoDocumentado] : form.UsualFormsCodes, form.UsualFormOtherText),
        BaselineArea.Conducta => new BehaviorAreaAnswer(
            form.StatusCode ?? BehaviorStatusCode.NoDocumentado,
            form.StatusCode == BehaviorStatusCode.PatronesConductualesHabituales ? form.PatternCodes : [], form.PatternOtherText),
        BaselineArea.Sueno => new SleepAreaAnswer(form.SleepPatternCodes.Count == 0 ? [SleepPatternCode.NoDocumentado] : form.SleepPatternCodes),
        BaselineArea.AyudasHabituales => new UsualAidsAreaAnswer(
            form.AidCodes.Count == 0 ? [UsualAidCode.NoDocumentado] : form.AidCodes, form.OtherSupportProductText, form.OtherSupportText),
        _ => throw new DomainValidationException("BASELINE_AREA_PAYLOAD_INVALID"),
    };
}
