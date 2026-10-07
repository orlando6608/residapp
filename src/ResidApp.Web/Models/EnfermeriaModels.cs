using System.ComponentModel.DataAnnotations;
using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Web.Models;

/// <summary>ENF-17/MED-19: filtro del estado basal en la lista de residentes.</summary>
public enum ResidentBaselineFilter
{
    [Display(Name = "Basal vigente")] Vigente,
    [Display(Name = "Basal pendiente")] Pendiente,
}

/// <summary>ENF-17/MED-19: buscar y filtrar la lista de residentes. Llega por GET (?q=&amp;basal=&amp;unidad=), así que el
/// filtro queda en la URL; los campos vacíos no filtran.</summary>
public sealed class ResidentListFilter
{
    [Display(Name = "Nombre")]
    public string? Q { get; set; }

    [Display(Name = "Estado basal")]
    public ResidentBaselineFilter? Basal { get; set; }

    [Display(Name = "Unidad")]
    public Guid? Unidad { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Q) && Basal is null && Unidad is null;
}

/// <summary>ENF-17/MED-19: la lista del ámbito (ya autorizada) filtrada en memoria. El nombre se busca sin distinguir
/// mayúsculas ni acentos («jose» encuentra «José»). Units son las unidades con residentes en el ámbito; el filtro de
/// unidad solo se ofrece si hay más de una.</summary>
public sealed record ResidentListViewModel(
    IReadOnlyList<ScopeResidentSummary> Residents, int TotalCount, ResidentListFilter Filter, IReadOnlyList<(Guid Id, string Name)> Units)
{
    public static ResidentListViewModel From(IReadOnlyList<ScopeResidentSummary> all, ResidentListFilter filter)
    {
        var name = filter.Q?.Trim();
        var shown = all
            .Where(r => string.IsNullOrEmpty(name) || System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                r.DisplayName, name, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0)
            .Where(r => filter.Basal is null || r.TieneBasalVigente == (filter.Basal == ResidentBaselineFilter.Vigente))
            .Where(r => filter.Unidad is null || r.UnitId.Value == filter.Unidad)
            .ToList();
        var units = all
            .GroupBy(r => r.UnitId.Value)
            .Select(g => (g.Key, g.First().UnitName ?? "Sin unidad"))
            .OrderBy(u => u.Item2)
            .ToList();
        return new(shown, all.Count, filter, units);
    }
}

/// <summary>ENF-02: filtrar la bandeja de cambios ordinarios. Llega por GET (?q=&amp;unidad=&amp;estado=); los campos
/// vacíos no filtran.</summary>
public sealed class PendingChangeFilter
{
    [Display(Name = "Residente")]
    public string? Q { get; set; }

    [Display(Name = "Unidad")]
    public Guid? Unidad { get; set; }

    [Display(Name = "Estado")]
    public ClinicalEventStatus? Estado { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Q) && Unidad is null && Estado is null;
}

/// <summary>ENF-02: la bandeja de ordinarios (ya autorizada) filtrada en memoria, con el nombre buscado sin distinguir
/// mayúsculas ni acentos. Units y Statuses son los que hay en la bandeja; la unidad solo se ofrece si hay más de una.</summary>
public sealed record PendingChangeListViewModel(
    IReadOnlyList<PendingChangeSummary> Items, int TotalCount, PendingChangeFilter Filter,
    IReadOnlyList<(Guid Id, string Name)> Units, IReadOnlyList<ClinicalEventStatus> Statuses)
{
    public static PendingChangeListViewModel From(IReadOnlyList<PendingChangeSummary> all, PendingChangeFilter filter)
    {
        var name = filter.Q?.Trim();
        var shown = all
            .Where(c => string.IsNullOrEmpty(name) || System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                c.ResidentDisplayName, name, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0)
            .Where(c => filter.Unidad is null || c.UnitId.Value == filter.Unidad)
            .Where(c => filter.Estado is null || c.Status == filter.Estado)
            .ToList();
        var units = all
            .GroupBy(c => c.UnitId.Value)
            .Select(g => (g.Key, g.First().UnitName ?? "Sin unidad"))
            .OrderBy(u => u.Item2)
            .ToList();
        var statuses = all.Select(c => c.Status).Distinct().OrderBy(ClinicalEventStatusDisplay.Label).ToList();
        return new(shown, all.Count, filter, units, statuses);
    }
}

/// <summary>ENF-18: identidad mínima del residente del ámbito más el resumen del basal vigente (null si
/// todavía no tiene ninguno firmado). También es la ficha de Medicina (MED-20), donde CanManageBaseline dice si su
/// ámbito tiene permiso para crear o reevaluar el basal (historia 8); Enfermería ofrece el acceso siempre. OpenEvents son
/// los eventos abiertos del residente que ve el perfil (ENF-18/MED-20); null si no se pudieron cargar. EmergencyContacts
/// son los contactos urgentes designados por Administración (0022, 0044), null si no se pudieron cargar o no hay.</summary>
public sealed record EnfermeriaResidentDetailViewModel(
    ScopeResidentSummary Resident, CurrentBaselineSummary? Baseline, bool CanManageBaseline = false,
    IReadOnlyList<OpenEventSummary>? OpenEvents = null, IReadOnlyList<EmergencyContactSummary>? EmergencyContacts = null);

/// <summary>ENF-01: contadores de las bandejas ya construidas (ordinarios, prioritarios y seguimientos,
/// con cuántos de estos están vencidos), de las comunicaciones familiares pendientes de aprobación y de las
/// indicaciones de Medicina pendientes (con cuántas faltan por leer), de los protocolos urgentes activos y de los
/// escalados a Medicina que siguen abiertos.</summary>
public sealed record EnfermeriaInicioViewModel(
    int Ordinarios, int Prioritarios, int Seguimientos, int SeguimientosVencidos, int Comunicaciones,
    int Indicaciones, int IndicacionesSinLeer, int Protocolos, int Escalados);

/// <summary>ENF-04: detalle de un cambio recibido más el resumen del basal vigente del residente (null si
/// todavía no tiene ninguno firmado), igual que EnfermeriaResidentDetailViewModel. CorrectionWindow decide si
/// se ofrece corregir o rectificar la valoración (COR-01/COR-02).</summary>
public sealed record EnfermeriaChangeDetailViewModel(PendingChangeDetail Change, CurrentBaselineSummary? Baseline, TimeSpan CorrectionWindow);

/// <summary>ENF-02/ENF-03 "antigüedad": tiempo transcurrido desde el registro, en la unidad más grande que
/// tenga sentido (días, horas o minutos) — no hace falta más precisión para ordenar visualmente una
/// bandeja.</summary>
public static class ElapsedTimeDisplay
{
    public static string Since(DateTimeOffset occurredAt)
    {
        var elapsed = DateTimeOffset.UtcNow - occurredAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays} d";
        }
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min";
        }
        return $"{Math.Max(1, elapsed.Minutes)} min";
    }
}

/// <summary>ENF-16 "Registrar un evento observado por Enfermería", en su alcance mínimo: solo registrar y
/// guardar. Clasificacion reutiliza el mismo catálogo cerrado que AUX-10 (Ordinario/Prioritario); a
/// diferencia de RegistrarCambioFormModel (Auxiliar), aquí no hay motivo prioritario ni aviso directo: ese
/// ritual existe para que Auxiliar alerte a Enfermería antes de que exista una bandeja que lo recoja, y
/// aquí Enfermería ya es autora y destinataria a la vez.</summary>
public sealed class RegistrarEventoFormModel
{
    [Required]
    public Guid ResidenteId { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Indica qué observas.")]
    [StringLength(2000)]
    [Display(Name = "Observación")]
    public string Observacion { get; set; } = string.Empty;

    [Required(ErrorMessage = "Elige una clasificación.")]
    [Display(Name = "Clasificación inicial")]
    public DailyChangeClassification Clasificacion { get; set; } = DailyChangeClassification.Ordinario;

    [StringLength(2000)]
    [Display(Name = "Datos clínicos pertinentes")]
    public string? DatosClinicosPertinentes { get; set; }
}

/// <summary>Estado del evento en bandejas y detalle (ENF-02 a ENF-04). "En valoración" usa el amarillo de
/// aviso con texto oscuro (text-bg-warning), nunca texto blanco sobre amarillo.</summary>
public static class ClinicalEventStatusDisplay
{
    public static string Label(ClinicalEventStatus status) => status switch
    {
        ClinicalEventStatus.Pendiente => "Pendiente",
        ClinicalEventStatus.EnValoracion => "En valoración",
        ClinicalEventStatus.EnSeguimiento => "En seguimiento",
        ClinicalEventStatus.EscaladoMedicina => "Escalado a Medicina",
        ClinicalEventStatus.EnValoracionMedica => "En valoración médica",
        ClinicalEventStatus.ConIndicacionPendiente => "Con indicación pendiente",
        ClinicalEventStatus.EnSeguimientoMedico => "En seguimiento médico",
        ClinicalEventStatus.ProtocoloUrgente => "Protocolo urgente activo",
        ClinicalEventStatus.ProtocoloUrgenteMedico => "Protocolo urgente activo (Medicina)",
        ClinicalEventStatus.Cerrado => "Cerrado",
        _ => status.ToString(),
    };

    public static string BadgeClass(ClinicalEventStatus status, bool prioritario) => status switch
    {
        ClinicalEventStatus.EnValoracion or ClinicalEventStatus.EnValoracionMedica => "text-bg-warning",
        ClinicalEventStatus.EnSeguimiento or ClinicalEventStatus.ConIndicacionPendiente or ClinicalEventStatus.EnSeguimientoMedico => "text-bg-info",
        ClinicalEventStatus.EscaladoMedicina => "text-bg-primary",
        ClinicalEventStatus.ProtocoloUrgente or ClinicalEventStatus.ProtocoloUrgenteMedico => "text-bg-danger",
        ClinicalEventStatus.Cerrado => "text-bg-success",
        _ => prioritario ? "text-bg-danger" : "text-bg-secondary",
    };
}

/// <summary>Textos del seguimiento (ENF-07B a ENF-09). El vencimiento se dice en texto ("Vencido"), no
/// solo con color.</summary>
public static class FollowUpDisplay
{
    public static string Label(FollowUpActionType type) => type switch
    {
        FollowUpActionType.Actuacion => "Actuación",
        FollowUpActionType.Reprogramacion => "Reprogramación",
        FollowUpActionType.Transferencia => "Transferencia de turno",
        FollowUpActionType.Recepcion => "Recepción confirmada",
        FollowUpActionType.Conservacion => "Conservado para la próxima revisión",
        _ => type.ToString(),
    };

    public static string Plan(DateOnly? dueDate, string? criterion) => (dueDate, criterion) switch
    {
        ({ } date, { } text) => $"Revisar el {date:dd/MM/yyyy} · {text}",
        ({ } date, null) => $"Revisar el {date:dd/MM/yyyy}",
        (null, { } text) => text,
        _ => string.Empty,
    };

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static bool IsOverdue(DateOnly? dueDate) => dueDate < Today;
}

/// <summary>ENF-07B "iniciar seguimiento": fecha prevista o criterio (al menos uno; lo decide el dominio,
/// FollowUpPlan) e indicaciones de continuidad. El equipo responsable es la Enfermería de la unidad.</summary>
public sealed class IniciarSeguimientoFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Display(Name = "Fecha prevista de revisión")]
    public DateOnly? FechaPrevista { get; set; }

    [StringLength(FollowUpPlan.MaxCriterionLength)]
    [Display(Name = "Criterio de revisión")]
    public string? Criterio { get; set; }

    [StringLength(FollowUpAction.MaxTextLength)]
    [Display(Name = "Indicaciones de continuidad")]
    public string? IndicacionesContinuidad { get; set; }
}

/// <summary>ENF-08/ENF-09: una acción sobre el seguimiento. Tipo lo fija cada formulario de la pantalla
/// (campo oculto); qué campos exige cada tipo lo decide el dominio (FollowUpAction).</summary>
public sealed class SeguimientoAccionFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Required]
    public FollowUpActionType Tipo { get; set; }

    [StringLength(FollowUpAction.MaxTextLength)]
    public string? Texto { get; set; }

    [Display(Name = "Nueva fecha prevista")]
    public DateOnly? FechaPrevista { get; set; }

    [StringLength(FollowUpPlan.MaxCriterionLength)]
    [Display(Name = "Nuevo criterio")]
    public string? Criterio { get; set; }

    [Display(Name = "Equipo entrante")]
    public Guid? EquipoEntranteId { get; set; }

    public Guid? TransferenciaId { get; set; }
}

/// <summary>ENF-10 "escalar a Medicina": motivo obligatorio (lo decide el dominio, EscalationReason) y la
/// revisión del evento al abrir la pantalla.</summary>
public sealed class EscalarFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Required(ErrorMessage = "Escribe el motivo del escalado.")]
    [StringLength(EscalationReason.MaxLength)]
    [Display(Name = "Motivo del escalado")]
    public string? Motivo { get; set; }
}

/// <summary>ENF-10/MED-03: la información reunida de un evento que se envía a Medicina (observación
/// original, basal vigente, valoración con constantes y actuaciones, y seguimiento si lo hubo), todo de
/// solo lectura. La usa el parcial _InformacionReunida.</summary>
public sealed record InformacionReunidaViewModel(PendingChangeDetail Event, CurrentBaselineSummary? Baseline);

/// <summary>ENF-10: el formulario más el evento, cuya información reunida se muestra antes de escalar, y el
/// basal vigente del residente (null si no tiene).</summary>
public sealed record EscalarViewModel(PendingChangeDetail Event, CurrentBaselineSummary? Baseline, EscalarFormModel Form);

/// <summary>ENF-07B: el formulario más el evento del que cuelga.</summary>
public sealed record IniciarSeguimientoViewModel(PendingChangeDetail Event, IniciarSeguimientoFormModel Form);

/// <summary>ENF-08/ENF-09: el seguimiento del evento y, tras un error, lo escrito en el formulario que
/// falló (Form.Tipo) para no perderlo.</summary>
public sealed record SeguimientoViewModel(
    PendingChangeDetail Event, SeguimientoAccionFormModel? Form, IReadOnlyList<TransferTeam> Teams)
{
    public SeguimientoAccionFormModel FormFor(FollowUpActionType tipo) =>
        Form is { } form && form.Tipo == tipo
            ? form
            : new SeguimientoAccionFormModel { EventoId = Event.EventId, Revision = Event.Revision, Tipo = tipo };
}

/// <summary>ENF-11/MED-13 "activar protocolo urgente": solo una nota opcional, para no retrasar la
/// atención. Común a Enfermería y Medicina.</summary>
public sealed class ActivarProtocoloFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [StringLength(UrgentProtocolActivation.MaxNoteLength)]
    [Display(Name = "Nota de activación (opcional)")]
    public string? Nota { get; set; }
}

/// <summary>ENF-11/MED-13: un registro del protocolo. Tipo lo fija cada formulario de la pantalla (campo
/// oculto); qué campos exige cada tipo lo decide el dominio (UrgentProtocolEntry). ContactadoEn llega de un
/// datetime-local, en la hora local del servidor, igual que se muestran las horas.</summary>
public sealed class ProtocoloRegistroFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Required]
    public UrgentProtocolEntryType Tipo { get; set; }

    [StringLength(UrgentProtocolEntry.MaxTextLength)]
    public string? Texto { get; set; }

    [StringLength(UrgentProtocolEntry.MaxServiceLength)]
    [Display(Name = "Servicio contactado")]
    public string? Servicio { get; set; }

    [Display(Name = "Hora del contacto")]
    public DateTime? ContactadoEn { get; set; }

    public DateTimeOffset? ContactadoEnOffset =>
        ContactadoEn is { } value ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local)) : null;
}

/// <summary>ENF-11/MED-13: la activación del protocolo con el evento del que cuelga. Las vistas del protocolo
/// son parciales comunes a los dos perfiles (DER-01); sus formularios envían al controlador en curso.</summary>
public sealed record ActivarProtocoloViewModel(PendingChangeDetail Event, ActivarProtocoloFormModel Form);

/// <summary>ENF-11/MED-13: el protocolo activo del evento y, tras un error, lo escrito en el formulario que
/// falló (Form.Tipo, o CallForm si fue el intento de llamada tras derivar) para no perderlo.</summary>
public sealed record ProtocoloViewModel(PendingChangeDetail Event, ProtocoloRegistroFormModel? Form, IntentoLlamadaFormModel? CallForm = null)
{
    public ProtocoloRegistroFormModel FormFor(UrgentProtocolEntryType tipo) =>
        Form is { } form && form.Tipo == tipo
            ? form
            : new ProtocoloRegistroFormModel { EventoId = Event.EventId, Revision = Event.Revision, Tipo = tipo };

    public IntentoLlamadaFormModel Call => CallForm ?? new IntentoLlamadaFormModel { EventoId = Event.EventId, Revision = Event.Revision };
}

/// <summary>Textos del protocolo urgente (ENF-11/MED-13).</summary>
public static class UrgentProtocolDisplay
{
    public static string Label(UrgentProtocolEntryType type) => type switch
    {
        UrgentProtocolEntryType.Actuacion => "Actuación",
        UrgentProtocolEntryType.Evolucion => "Evolución",
        UrgentProtocolEntryType.Contacto => "Contacto con un servicio",
        _ => type.ToString(),
    };

    public static string Profile(SystemProfile profile) => profile == SystemProfile.Medicina ? "Medicina" : "Enfermería";

    public static string SavedMessage(UrgentProtocolEntryType type) => type switch
    {
        UrgentProtocolEntryType.Actuacion => "Actuación registrada.",
        UrgentProtocolEntryType.Evolucion => "Evolución registrada.",
        _ => "Contacto registrado.",
    };

    public static string InvalidMessage(UrgentProtocolEntryType type) => type == UrgentProtocolEntryType.Contacto
        ? "Para registrar un contacto indica el servicio y la hora del contacto, que no puede ser futura."
        : "Escribe el texto antes de registrarlo.";
}

/// <summary>Textos de la comunicación familiar (ENF-14/ENF-15).</summary>
public static class FamilyCommunicationDisplay
{
    public static string Label(FamilyCommunicationDecision decision) => decision switch
    {
        FamilyCommunicationDecision.NoComunicar => "No comunicar",
        FamilyCommunicationDecision.Preparar => "Preparar comunicación",
        _ => decision.ToString(),
    };

    public static string Label(FamilyCommunicationType type) => type switch
    {
        FamilyCommunicationType.Ordinaria => "Ordinaria",
        FamilyCommunicationType.Relevante => "Relevante",
        _ => type.ToString(),
    };
}

/// <summary>ENF-07A "cerrar el evento" con la decisión de comunicación familiar (ENF-14/ENF-15). Sin
/// decisión preseleccionada: debe ser explícita. El tipo y el texto solo se envían si se prepara la
/// comunicación; el dominio (FamilyCommunicationChoice) es quien decide si la combinación es válida.
/// Revision es la del evento al abrir la pantalla y OperacionId hace el cierre idempotente.</summary>
public sealed class CerrarFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [Required]
    public Guid OperacionId { get; set; }

    [Required(ErrorMessage = "Decide si se comunica a la familia.")]
    [Display(Name = "Comunicación familiar")]
    public FamilyCommunicationDecision? Comunicacion { get; set; }

    [Display(Name = "Tipo de comunicación")]
    public FamilyCommunicationType? TipoComunicacion { get; set; }

    [StringLength(FamilyCommunicationChoice.MaxTextLength)]
    [Display(Name = "Texto para la familia")]
    public string? TextoComunicacion { get; set; }

    /// <summary>Con una derivación a Urgencias la decisión no es libre (DER-06): la actualización relevante
    /// para la familia es obligatoria, así que llega ya elegida; el texto lo escribe el profesional.</summary>
    public static CerrarFormModel For(PendingChangeDetail detail) => new()
    {
        EventoId = detail.EventId, Revision = detail.Revision, OperacionId = Guid.NewGuid(),
        Comunicacion = detail.Referral is null ? null : FamilyCommunicationDecision.Preparar,
        TipoComunicacion = detail.Referral is null ? null : FamilyCommunicationType.Relevante,
    };
}

/// <summary>ENF-07A: el formulario de cierre más el evento, cuyo resumen de valoración y actuaciones se
/// revisa antes de cerrar.</summary>
public sealed record CerrarViewModel(PendingChangeDetail Event, CerrarFormModel Form);

/// <summary>ENF-05 "Valoración de Enfermería": hallazgos, valoración, actuaciones, comunicaciones,
/// resultado y constantes opcionales. Revision es la del evento al abrir la pantalla (concurrencia
/// optimista). Las comprobaciones de formato se repiten en dominio (VitalSigns), que es quien decide.</summary>
public sealed class ValoracionFormModel : ConstantesFormModel
{
    [Required]
    public Guid EventoId { get; set; }

    [Required]
    public int Revision { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Hallazgos")]
    public string? Hallazgos { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Valoración")]
    public string? Valoracion { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Actuaciones")]
    public string? Actuaciones { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Comunicaciones")]
    public string? Comunicaciones { get; set; }

    [StringLength(NursingAssessmentContent.MaxTextLength)]
    [Display(Name = "Resultado")]
    public string? Resultado { get; set; }

    public static ValoracionFormModel From(PendingChangeDetail detail)
    {
        var form = new ValoracionFormModel { EventoId = detail.EventId, Revision = detail.Revision };
        if (detail.Assessment is not { Content: var content })
        {
            return form;
        }
        form.Hallazgos = content.Findings;
        form.Valoracion = content.Assessment;
        form.Actuaciones = content.Actions;
        form.Comunicaciones = content.Communications;
        form.Resultado = content.Outcome;
        form.FillVitals(content.Vitals);
        return form;
    }
}

/// <summary>Constantes opcionales de una valoración (ENF-05, MED-05), comunes a Enfermería y Medicina y
/// pintadas por el parcial _ConstantesFormulario. Las comprobaciones de formato se repiten en dominio
/// (VitalSigns), que es quien decide.</summary>
public abstract class ConstantesFormModel
{
    [Range(typeof(decimal), "0.1", "99.9", ParseLimitsInInvariantCulture = true, ErrorMessage = "Indica una temperatura válida.")]
    [Display(Name = "Temperatura (°C)")]
    public decimal? TemperaturaCelsius { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "PA sistólica (mmHg)")]
    public int? TensionSistolica { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "PA diastólica (mmHg)")]
    public int? TensionDiastolica { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Frecuencia cardíaca (lpm)")]
    public int? FrecuenciaCardiaca { get; set; }

    [Range(1, 999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Frecuencia respiratoria (rpm)")]
    public int? FrecuenciaRespiratoria { get; set; }

    [Range(0, 100, ErrorMessage = "La saturación va de 0 a 100 %.")]
    [Display(Name = "Saturación de O₂ (%)")]
    public int? SaturacionO2 { get; set; }

    [Display(Name = "Aire ambiente u oxigenoterapia")]
    public RespiratorySupportCode? SoporteRespiratorio { get; set; }

    [Range(typeof(decimal), "0.1", "99.9", ParseLimitsInInvariantCulture = true, ErrorMessage = "Indica un flujo válido.")]
    [Display(Name = "Flujo de O₂ (L/min)")]
    public decimal? FlujoO2 { get; set; }

    [Range(1, 9999, ErrorMessage = "Indica un valor positivo.")]
    [Display(Name = "Glucemia (mg/dL)")]
    public int? Glucemia { get; set; }

    [StringLength(100)]
    [Display(Name = "Otra constante: nombre")]
    public string? OtraConstanteNombre { get; set; }

    [StringLength(50)]
    [Display(Name = "Valor")]
    public string? OtraConstanteValor { get; set; }

    [StringLength(30)]
    [Display(Name = "Unidad")]
    public string? OtraConstanteUnidad { get; set; }

    protected void FillVitals(VitalSigns vitals)
    {
        TemperaturaCelsius = vitals.TemperatureCelsius;
        TensionSistolica = vitals.SystolicMmHg;
        TensionDiastolica = vitals.DiastolicMmHg;
        FrecuenciaCardiaca = vitals.HeartRateBpm;
        FrecuenciaRespiratoria = vitals.RespiratoryRateRpm;
        SaturacionO2 = vitals.OxygenSaturationPct;
        SoporteRespiratorio = vitals.RespiratorySupport;
        FlujoO2 = vitals.OxygenFlowLpm;
        Glucemia = vitals.GlucoseMgDl;
        OtraConstanteNombre = vitals.OtherName;
        OtraConstanteValor = vitals.OtherValue;
        OtraConstanteUnidad = vitals.OtherUnit;
    }
}

/// <summary>ENF-05: el formulario más el evento del que cuelga (residente, origen y observación original,
/// que se muestra como referencia y nunca se edita).</summary>
public sealed record ValoracionViewModel(PendingChangeDetail Event, ValoracionFormModel Form);

/// <summary>Resumen en una línea de las constantes registradas, solo las que tienen valor.</summary>
public static class VitalSignsDisplay
{
    public static string Summary(VitalSigns v)
    {
        var parts = new List<string>();
        if (v.TemperatureCelsius is not null) parts.Add($"T {v.TemperatureCelsius:0.0} °C");
        if (v.SystolicMmHg is not null) parts.Add($"PA {v.SystolicMmHg}/{v.DiastolicMmHg} mmHg");
        if (v.HeartRateBpm is not null) parts.Add($"FC {v.HeartRateBpm} lpm");
        if (v.RespiratoryRateRpm is not null) parts.Add($"FR {v.RespiratoryRateRpm} rpm");
        if (v.OxygenSaturationPct is not null) parts.Add($"SpO₂ {v.OxygenSaturationPct} %");
        if (v.RespiratorySupport is not null)
        {
            parts.Add(v.RespiratorySupport == RespiratorySupportCode.Oxigenoterapia
                ? v.OxygenFlowLpm is null ? "oxigenoterapia" : $"oxigenoterapia a {v.OxygenFlowLpm:0.#} L/min"
                : "aire ambiente");
        }
        if (v.GlucoseMgDl is not null) parts.Add($"glucemia {v.GlucoseMgDl} mg/dL");
        if (v.OtherName is not null) parts.Add($"{v.OtherName} {v.OtherValue}{(v.OtherUnit is null ? "" : " " + v.OtherUnit)}");
        return string.Join(" · ", parts);
    }
}


/// <summary>Textos de los rangos de referencia de constantes y de sus avisos (ENF-05). El aviso nombra la
/// constante, el valor y el rango en texto: no depende solo del color.</summary>
public static class VitalSignRangeDisplay
{
    public static string Label(VitalSignCode code) => code switch
    {
        VitalSignCode.Temperatura => "Temperatura",
        VitalSignCode.TensionSistolica => "PA sistólica",
        VitalSignCode.TensionDiastolica => "PA diastólica",
        VitalSignCode.FrecuenciaCardiaca => "Frecuencia cardíaca",
        VitalSignCode.FrecuenciaRespiratoria => "Frecuencia respiratoria",
        VitalSignCode.SaturacionO2 => "Saturación de O₂",
        VitalSignCode.Glucemia => "Glucemia",
        _ => code.ToString(),
    };

    public static string Unit(VitalSignCode code) => code switch
    {
        VitalSignCode.Temperatura => "°C",
        VitalSignCode.TensionSistolica or VitalSignCode.TensionDiastolica => "mmHg",
        VitalSignCode.FrecuenciaCardiaca => "lpm",
        VitalSignCode.FrecuenciaRespiratoria => "rpm",
        VitalSignCode.SaturacionO2 => "%",
        VitalSignCode.Glucemia => "mg/dL",
        _ => string.Empty,
    };

    public static string Range(VitalSignRange range) => (range.Min, range.Max) switch
    {
        ({ } min, { } max) => $"{min:0.#}–{max:0.#} {Unit(range.Code)}",
        ({ } min, null) => $"≥ {min:0.#} {Unit(range.Code)}",
        (null, { } max) => $"≤ {max:0.#} {Unit(range.Code)}",
        _ => string.Empty,
    };

    public static string Alert(VitalSignAlert alert) =>
        $"{Label(alert.Code)} {alert.Value:0.#} {Unit(alert.Code)}" +
        $"{(alert.OxygenFlowLpm is { } flow ? $" con oxígeno a {flow:0.#} L/min" : string.Empty)}: " +
        $"{(alert.Deviation == VitalSignDeviation.PorDebajo ? "por debajo" : "por encima")} del rango de referencia ({Range(alert.Range)})";

    /// <summary>Texto de ayuda bajo el campo del formulario, o null si el centro no tiene rango para esa constante.</summary>
    public static string? Hint(IReadOnlyList<VitalSignRange> ranges, VitalSignCode code) =>
        ranges.FirstOrDefault(r => r.Code == code) is { } range ? $"Referencia del centro: {Range(range)}" : null;
}
