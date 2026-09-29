using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>ENF-12/MED-14: el formulario de la derivación. Accion distingue "ver la vista previa" (obligatoria)
/// de "firmar", que además exige la Huella que mostró esa vista previa.</summary>
public sealed class DerivarFormModel
{
    public const string VistaPrevia = "VistaPrevia";
    public const string Firmar = "Firmar";
    public const string Editar = "Editar";

    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Escribe el motivo de la derivación.")]
    [StringLength(ReferralReportInput.MaxReasonLength)]
    [Display(Name = "Motivo de la derivación")]
    public string? Motivo { get; set; }

    [StringLength(ReferralReportInput.MaxAdditionalInformationLength)]
    [Display(Name = "Información adicional para Urgencias (opcional)")]
    public string? InformacionAdicional { get; set; }

    public string? Huella { get; set; }

    public string? Accion { get; set; }
}

/// <summary>DER-06: un intento de llamada al contacto familiar. LlamadoEn llega de un datetime-local, en la
/// hora local del servidor, igual que se muestran las horas.</summary>
public sealed class IntentoLlamadaFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [StringLength(FamilyCallAttempt.MaxContactLength)]
    [Display(Name = "A quién se llama")]
    public string? Contacto { get; set; }

    [Display(Name = "Hora de la llamada")]
    public DateTime? LlamadoEn { get; set; }

    [Display(Name = "Resultado")]
    public FamilyCallResult? Resultado { get; set; }

    [StringLength(FamilyCallAttempt.MaxNoteLength)]
    [Display(Name = "Nota (opcional)")]
    public string? Nota { get; set; }

    public DateTimeOffset? LlamadoEnOffset =>
        LlamadoEn is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)) : null;
}

/// <summary>ENF-12/MED-14: la pantalla de derivación: el evento, el formulario y, si se pidió, la vista previa
/// del informe completo con su huella (Preview null mientras se edita).</summary>
public sealed record DerivarViewModel(PendingChangeDetail Event, DerivarFormModel Form, ReferralReportContent? Preview);

/// <summary>Textos de la derivación a Urgencias (ENF-12/ENF-14, MED-14/MED-16).</summary>
public static class ReferralDisplay
{
    public static string Label(FamilyCallResult result) => result switch
    {
        FamilyCallResult.Contactado => "Contactado",
        FamilyCallResult.NoContesta => "No contesta",
        FamilyCallResult.NumeroErroneo => "Número erróneo",
        _ => result.ToString(),
    };

    public const string ChangedMessage =
        "El informe ha cambiado desde la vista previa (otro profesional registró algo, u otra pestaña o pulsación tuya). " +
        "Revisa esta vista previa actualizada antes de firmar.";

    public const string InvalidMessage =
        "Revisa los datos: el motivo de la derivación es obligatorio y los textos no pueden superar su longitud máxima.";

    public const string CallInvalidMessage =
        "Para registrar la llamada indica a quién llamas, la hora (no puede ser futura) y el resultado.";

    public const string CloseMessage =
        "Con una derivación a Urgencias, al cerrar hay que preparar la actualización relevante para la familia y " +
        "antes tiene que constar al menos un intento de llamada.";

    public static string FileName(DateTimeOffset signedAt) =>
        $"informe-derivacion-{signedAt.ToLocalTime():yyyyMMdd-HHmm}.pdf";

    public static string Sex(DocumentedSexCode sex) => sex switch
    {
        DocumentedSexCode.Hombre => "Hombre",
        DocumentedSexCode.Mujer => "Mujer",
        DocumentedSexCode.OtraCategoriaDocumentada => "Otra categoría documentada",
        _ => "No consta",
    };
}

/// <summary>
/// DER-03: reúne los datos automáticos del informe de derivación a partir de los registros de origen, en el
/// orden del flujo: identificación y centro, basal (Barthel, cognición, comunicación y el resto de áreas),
/// observación de origen, valoraciones, constantes y oxigenoterapia, actuaciones y evolución. Nunca incluye
/// CFS (no existe en el producto) ni los contactos: ni los servicios contactados del protocolo ni el campo
/// "Comunicaciones" de la valoración de Enfermería, que son trazabilidad interna (DER-04). Las horas se
/// escriben en la hora local del servidor con un formato fijo, para que la huella no dependa de la cultura.
/// </summary>
public static class ReferralReportBuilder
{
    private const string NoData = "Sin datos registrados.";

    public static IReadOnlyList<ReferralReportSection> Build(
        PendingChangeDetail detail, CurrentBaselineSummary? baseline, ResidentIdentification identification) =>
    [
        Section("Identificación del residente y del centro",
        [
            $"Nombre: {identification.DisplayName}",
            $"Fecha de nacimiento: {identification.BirthDate:dd'/'MM'/'yyyy}",
            $"Sexo documentado: {ReferralDisplay.Sex(identification.DocumentedSex)}",
            $"Centro: {identification.CenterName}",
            $"Unidad: {identification.UnitName ?? "Sin unidad"}",
        ]),
        Section("Basal vigente", BaselineLines(baseline)),
        Section("Observación de origen", ObservationLines(detail)),
        Section("Valoraciones", AssessmentLines(detail)),
        Section("Constantes y oxigenoterapia", VitalLines(detail)),
        Section("Actuaciones", ActionLines(detail)),
        Section("Evolución", detail.UrgentProtocol is { } protocol
            ? protocol.Entries.Where(e => e.Type == UrgentProtocolEntryType.Evolucion).Select(e => $"{Time(e.RecordedAt)}: {e.Text}").ToList()
            : []),
    ];

    private static ReferralReportSection Section(string title, IReadOnlyList<string> lines) =>
        new(title, true, lines.Count == 0 ? [NoData] : lines);

    private static List<string> BaselineLines(CurrentBaselineSummary? baseline)
    {
        if (baseline is null)
        {
            return ["Sin basal vigente registrado."];
        }

        var lines = new List<string>
        {
            $"Versión {baseline.VersionNumber}, firmada el {Time(baseline.SignedAt)}.",
            $"Barthel total: {baseline.BarthelTotal} / 100",
        };
        // Cognición y comunicación primero (DER-03 las nombra); después el resto de áreas del basal.
        foreach (var area in baseline.Areas.OrderBy(a => a.AreaCode switch
                 {
                     Domain.Baseline.BaselineArea.Cognicion => 0,
                     Domain.Baseline.BaselineArea.Comunicacion => 1,
                     _ => 2,
                 }))
        {
            var summary = string.Join(" · ", AreaValues(area.Answer));
            var observation = area.Observation is null ? "" : $" (observación: {area.Observation})";
            lines.Add($"{BaselineAreaDisplay.Label(area.AreaCode)}: {(summary.Length == 0 ? "sin respuesta" : summary)}{observation}");
        }
        return lines;
    }

    /// <summary>Como BaselineAreaDisplay.Summarize (solo valores), pero desplegando las respuestas de varias
    /// opciones en vez de escribir el nombre del tipo de la lista, y separando en palabras los valores de los
    /// catálogos ("ComprensionFuncional" → "Comprension funcional"; los catálogos no tienen etiquetas con
    /// tildes).</summary>
    private static IEnumerable<string> AreaValues(Domain.Baseline.Answers.IBaselineAreaAnswer answer) =>
        answer.GetType().GetProperties()
            .Select(property => property.GetValue(answer))
            .SelectMany(value => value switch
            {
                null => [],
                string text => [text],
                System.Collections.IEnumerable items => items.Cast<object>(),
                _ => [value],
            })
            .Select(value => value is Enum ? Words(value.ToString()!) : value.ToString()!)
            .Where(value => value.Length > 0);

    private static string Words(string pascalCase) =>
        string.Concat(pascalCase.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

    private static List<string> ObservationLines(PendingChangeDetail detail)
    {
        var lines = new List<string>
        {
            $"{(detail.Origin == ClinicalEventOrigin.EventoEnfermeria ? "Observada" : "Registrada")} por " +
            $"{SystemProfileDisplay.Label(detail.AuthorProfile)} el {Time(detail.OccurredAt)}.",
        };
        if (detail.Observation is not null)
        {
            lines.Add(detail.Observation);
        }
        if (detail.ClinicalData is not null)
        {
            lines.Add($"Datos clínicos pertinentes: {detail.ClinicalData}");
        }
        if (detail.TemperatureCelsius is { } temperature)
        {
            lines.Add($"Temperatura: {temperature:0.0} °C");
        }
        foreach (var area in detail.Areas)
        {
            var parts = area.Options.Select(DailyChangeAreaOptionDisplay.Label).ToList();
            if (area.FreeText is not null)
            {
                parts.Add(area.FreeText);
            }
            lines.Add($"{DailyChangeAreaDisplay.Label(area.AreaCode)}: {string.Join(" · ", parts)}");
        }
        if (detail.Escalation is { } escalation)
        {
            lines.Add($"Motivo del escalado a Medicina: {escalation.Reason}");
        }
        return lines;
    }

    private static List<string> AssessmentLines(PendingChangeDetail detail)
    {
        var lines = new List<string>();
        if (detail.Assessment?.Content is { } nursing)
        {
            AddIf(lines, "Enfermería, hallazgos", nursing.Findings);
            AddIf(lines, "Enfermería, valoración", nursing.Assessment);
            AddIf(lines, "Enfermería, resultado", nursing.Outcome);
        }
        if (detail.Medical.Assessment?.Content is { } medical)
        {
            AddIf(lines, "Medicina, hallazgos y exploración", medical.FindingsAndExamination);
            AddIf(lines, "Medicina, valoración", medical.Assessment);
        }
        return lines;
    }

    private static List<string> VitalLines(PendingChangeDetail detail)
    {
        var lines = new List<string>();
        if (detail.Assessment?.Content.Vitals is { IsEmpty: false } nursing)
        {
            lines.Add($"Enfermería: {VitalSignsDisplay.Summary(nursing)}");
        }
        if (detail.Medical.Assessment?.Content.Vitals is { IsEmpty: false } medical)
        {
            lines.Add($"Medicina: {VitalSignsDisplay.Summary(medical)}");
        }
        return lines;
    }

    private static List<string> ActionLines(PendingChangeDetail detail)
    {
        var lines = new List<string>();
        AddIf(lines, "Enfermería", detail.Assessment?.Content.Actions);
        AddIf(lines, "Medicina", detail.Medical.Assessment?.Content.Actions);
        foreach (var action in detail.FollowUp?.Actions.Where(a => a.Type == FollowUpActionType.Actuacion) ?? [])
        {
            lines.Add($"{Time(action.RecordedAt)} (seguimiento de Enfermería): {action.Text}");
        }
        foreach (var action in detail.Medical.FollowUp?.Tracking.Actions.Where(a => a.Type == FollowUpActionType.Actuacion) ?? [])
        {
            lines.Add($"{Time(action.RecordedAt)} (seguimiento médico): {action.Text}");
        }
        if (detail.UrgentProtocol is { } protocol)
        {
            lines.Add($"{Time(protocol.ActivatedAt)}: protocolo urgente activado por {UrgentProtocolDisplay.Profile(protocol.Profile)}" +
                (protocol.ActivationNote is null ? "." : $" ({protocol.ActivationNote})."));
            lines.AddRange(protocol.Entries
                .Where(e => e.Type == UrgentProtocolEntryType.Actuacion)
                .Select(e => $"{Time(e.RecordedAt)}: {e.Text}"));
        }
        return lines;
    }

    private static void AddIf(List<string> lines, string label, string? text)
    {
        if (text is not null)
        {
            lines.Add($"{label}: {text}");
        }
    }

    private static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("dd'/'MM'/'yyyy HH':'mm");
}
