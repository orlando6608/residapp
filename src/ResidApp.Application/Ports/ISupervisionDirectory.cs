using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>DIR-01 a DIR-04: un episodio abierto visto desde la supervisión operativa de Dirección Clínica. Solo lleva
/// identificación (residente, unidad), estado, fechas y contadores: ningún campo de texto clínico, así que ninguna
/// pantalla de supervisión puede mostrarlo. FollowUpDue es la fecha prevista vigente del seguimiento (la de la última
/// reprogramación, si la hay); IndicationsPending cuenta las indicaciones sin realizar ni marcar como no realizadas, e
/// IndicationsNotDone las marcadas como no realizadas (con incidencia).</summary>
public sealed record SupervisionEpisode(
    Guid EventId, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string UnitName, ClinicalEventOrigin Origin,
    DailyChangeClassification Classification, ClinicalEventStatus Status, DateTimeOffset ReceivedAt, bool Escalated,
    bool HasFollowUp, DateOnly? FollowUpDue, bool HasUrgentProtocol, int IndicationsPending, int IndicationsNotDone);

/// <summary>DIR-04: hito del episodio, solo con su tipo, el perfil que lo hizo y la hora (sin texto).</summary>
public enum SupervisionMilestoneKind
{
    Registrado,
    ValoracionIniciada,
    ValoracionMedicaIniciada,
    Escalado,
    IndicacionEmitida,
    SeguimientoIniciado,
    ProtocoloUrgenteActivado,
    InformeDerivacionFirmado,
}

public sealed record SupervisionMilestone(SupervisionMilestoneKind Kind, SystemProfile? Profile, DateTimeOffset At);

/// <summary>DIR-04: el episodio con sus hitos, del más antiguo al más reciente.</summary>
public sealed record SupervisionEpisodeDetail(SupervisionEpisode Episode, IReadOnlyList<SupervisionMilestone> Milestones);

/// <summary>DIR-17: lo que el ámbito activo de Dirección puede supervisar. Permissions son los códigos vigentes.</summary>
public sealed record SupervisionScopeInfo(
    string CenterName, IReadOnlyList<(UnitId Id, string Name)> Units, bool RestrictedToResidents, IReadOnlyList<string> Permissions);

/// <summary>DIR-01 a DIR-04 y DIR-17: lectura de supervisión. Cada método comprueba en la propia consulta que el ámbito es
/// un ámbito activo de Dirección Clínica del centro y que el episodio cae en sus unidades (y en sus residentes, si el
/// ámbito los restringe): fuera de eso devuelve vacío o null, sin distinguir si existe.</summary>
public interface ISupervisionDirectory
{
    Task<IReadOnlyList<SupervisionEpisode>> ListOpenEpisodesAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    Task<SupervisionEpisodeDetail?> FindEpisodeAsync(Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default);

    Task<SupervisionScopeInfo?> FindScopeAsync(Guid profileScopeId, CenterId centerId, CancellationToken ct = default);

    /// <summary>DIR-08 a DIR-10: hechos del ámbito con fecha en [from, toExclusive), abiertos o cerrados. Los límites son
    /// UTC, como las fechas guardadas (ver SupervisionIndicatorRules.UtcBounds).</summary>
    Task<SupervisionIndicatorFacts> ListIndicatorFactsAsync(
        Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default);
}

/// <summary>DIR-08 a DIR-10: hechos de los que salen los indicadores agregados. Solo llevan la unidad, códigos y la fecha
/// (UTC, como se guarda): ni texto, ni residente, ni cuenta, así que ningún indicador puede bajar a una persona
/// (DIR-09, sin ranking individual). Un episodio cuenta en el periodo por su registro; los indicadores de escalado,
/// protocolo y derivación dicen qué le pasó después, hasta hoy.</summary>
public sealed record IndicatorEpisodeFact(
    UnitId UnitId, ClinicalEventOrigin Origin, DailyChangeClassification Classification, DateTime ReceivedAt,
    bool Escalated, bool UrgentProtocol, bool Referred);

public sealed record IndicatorClosureFact(UnitId UnitId, DateTime ClosedAt);

/// <summary>Una indicación médica emitida, con su estado de hoy.</summary>
public sealed record IndicatorIndicationFact(UnitId UnitId, DateTime IssuedAt, MedicalIndicationStatus Status);

/// <summary>Una transferencia de seguimiento (de Enfermería o de Medicina) y si se confirmó su recepción.</summary>
public sealed record IndicatorTransferFact(UnitId UnitId, SystemProfile Profile, DateTime At, bool Received);

/// <summary>Una reprogramación del plan de un seguimiento: cuándo se registró (UTC) y la fecha prevista nueva (null si el plan ya no tiene fecha).</summary>
public sealed record IndicatorReschedule(DateTime At, DateOnly? Due);

/// <summary>Un seguimiento (de Enfermería o de Medicina) que estuvo abierto en algún momento del periodo o antes de su fin: desde
/// StartedAt hasta EndedAt (null si sigue abierto), con su fecha prevista inicial y sus reprogramaciones. EndedAt es lo primero que
/// ocurrió del cierre del episodio, el escalado a Medicina o la activación del protocolo urgente tras iniciarse el seguimiento: no se
/// guarda cuándo un seguimiento deja de estar abierto por otras vías.</summary>
public sealed record IndicatorFollowUpFact(
    UnitId UnitId, DateTime StartedAt, DateTime? EndedAt, DateOnly? InitialDue, IReadOnlyList<IndicatorReschedule> Reschedules);

public sealed record SupervisionIndicatorFacts(
    IReadOnlyList<IndicatorEpisodeFact> Episodes, IReadOnlyList<IndicatorClosureFact> Closures,
    IReadOnlyList<IndicatorIndicationFact> Indications, IReadOnlyList<IndicatorTransferFact> Transfers,
    IReadOnlyList<IndicatorFollowUpFact> FollowUps);

/// <summary>DIR-08/DIR-09: recuentos de un periodo. Registered es el denominador de Escalated, UrgentProtocols y Referrals;
/// IndicationsIssued, el de los estados de las indicaciones; cada recuento de transferencias, el de sus recepciones.
/// Closed cuenta los cierres del periodo, aunque el episodio se registrara antes, y no tiene denominador. FollowUpsOpen son los
/// seguimientos abiertos en algún momento del periodo (ver SupervisionIndicatorRules.WasOverdue), el denominador de
/// FollowUpsOverdue, los que tuvieron su fecha prevista vencida en algún día del periodo.</summary>
public sealed record SupervisionIndicatorCounts(
    int Registered, int FromAuxiliar, int FromNursing, int FromMedicine, int Priority, int Closed,
    int Escalated, int UrgentProtocols, int Referrals,
    int IndicationsIssued, int IndicationsRead, int IndicationsDone, int IndicationsNotDone, int IndicationsUnresolved,
    int NursingTransfers, int NursingTransfersReceived, int MedicalTransfers, int MedicalTransfersReceived,
    int FollowUpsOpen = 0, int FollowUpsOverdue = 0);

public sealed record SupervisionUnitIndicators(UnitId UnitId, string UnitName, SupervisionIndicatorCounts Counts);

/// <summary>DIR-10: un mes natural, recortado al periodo (From y To incluidos).</summary>
public sealed record SupervisionMonthIndicators(DateOnly From, DateOnly To, SupervisionIndicatorCounts Counts);

/// <summary>DIR-08 a DIR-10 y DIR-16: los indicadores del periodo (From y To incluidos), por unidad del ámbito (también
/// las que no tienen nada), en total y mes a mes.</summary>
public sealed record SupervisionIndicators(
    string CenterName, DateOnly From, DateOnly To, IReadOnlyList<SupervisionUnitIndicators> Units,
    SupervisionIndicatorCounts Total, IReadOnlyList<SupervisionMonthIndicators> Months);

public static class SupervisionIndicatorRules
{
    /// <summary>Periodo más largo que se puede pedir, en días.</summary>
    public const int MaxPeriodDays = 366;

    /// <summary>Límites UTC [From, ToExclusive) de los días from a to (incluidos) de la zona horaria zone.</summary>
    public static (DateTime From, DateTime ToExclusive) UtcBounds(DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        (TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone),
         TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));

    /// <summary>Cada hecho (en UTC) cuenta en su día de la zona horaria zone, la misma en la que se eligió el periodo.</summary>
    public static SupervisionIndicators Aggregate(
        SupervisionIndicatorFacts facts, SupervisionScopeInfo scope, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var units = scope.Units.Select(u => new SupervisionUnitIndicators(u.Id, u.Name, Count(facts, from, to, u.Id, zone))).ToList();
        var months = new List<SupervisionMonthIndicators>();
        for (var start = from; start <= to; start = new DateOnly(start.Year, start.Month, 1).AddMonths(1))
        {
            var monthEnd = new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            var end = monthEnd < to ? monthEnd : to;
            months.Add(new SupervisionMonthIndicators(start, end, Count(facts, start, end, null, zone)));
        }

        return new SupervisionIndicators(scope.CenterName, from, to, units, Count(facts, from, to, null, zone), months);
    }

    private static SupervisionIndicatorCounts Count(
        SupervisionIndicatorFacts facts, DateOnly from, DateOnly to, UnitId? unit, TimeZoneInfo zone)
    {
        bool In(UnitId unitId, DateTime at)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(at, zone));
            return (unit is null || unitId == unit) && day >= from && day <= to;
        }

        var episodes = facts.Episodes.Where(e => In(e.UnitId, e.ReceivedAt)).ToList();
        var indications = facts.Indications.Where(i => In(i.UnitId, i.IssuedAt)).ToList();
        var transfers = facts.Transfers.Where(t => In(t.UnitId, t.At)).ToList();
        var nursing = transfers.Where(t => t.Profile == SystemProfile.Enfermeria).ToList();
        var medical = transfers.Where(t => t.Profile == SystemProfile.Medicina).ToList();
        return new SupervisionIndicatorCounts(
            episodes.Count,
            episodes.Count(e => e.Origin == ClinicalEventOrigin.CambioAuxiliar),
            episodes.Count(e => e.Origin == ClinicalEventOrigin.EventoEnfermeria),
            episodes.Count(e => e.Origin == ClinicalEventOrigin.EventoMedicina),
            episodes.Count(e => e.Classification == DailyChangeClassification.Prioritario),
            facts.Closures.Count(c => In(c.UnitId, c.ClosedAt)),
            episodes.Count(e => e.Escalated), episodes.Count(e => e.UrgentProtocol), episodes.Count(e => e.Referred),
            indications.Count,
            indications.Count(i => i.Status != MedicalIndicationStatus.PendienteLectura),
            indications.Count(i => i.Status == MedicalIndicationStatus.Realizada),
            indications.Count(i => i.Status == MedicalIndicationStatus.NoRealizada),
            indications.Count(i => i.Status is MedicalIndicationStatus.PendienteLectura or MedicalIndicationStatus.Leida),
            nursing.Count, nursing.Count(t => t.Received), medical.Count, medical.Count(t => t.Received),
            facts.FollowUps.Count(f => (unit is null || f.UnitId == unit) && WasOpen(f, from, to, zone)),
            facts.FollowUps.Count(f => (unit is null || f.UnitId == unit) && WasOverdue(f, from, to, zone)));
    }

    /// <summary>Si el seguimiento estuvo abierto algún día de [from, to]: desde el día en que se inició hasta el día en que terminó (ese día incluido).</summary>
    public static bool WasOpen(IndicatorFollowUpFact followUp, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var (start, end) = OpenDays(followUp, zone);
        return start <= to && (end is null || end >= from);
    }

    /// <summary>Si el seguimiento tuvo su fecha prevista vencida algún día de [from, to]: un día d cuenta si el seguimiento estaba abierto
    /// y el plan vigente al empezar ese día (el de la última reprogramación registrada en un día anterior) tenía una fecha anterior a d.
    /// Así, un seguimiento que se reprograma el mismo día en que vence ya cuenta como vencido ese día; una reprogramación a una fecha
    /// sin fecha, o un plan sin fecha, no vence nunca. Los días son los de la zona horaria zone, la misma del periodo.</summary>
    public static bool WasOverdue(IndicatorFollowUpFact followUp, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var (start, end) = OpenDays(followUp, zone);
        var windowFrom = start > from ? start : from;
        var windowTo = end is { } e && e < to ? e : to;
        var reschedules = followUp.Reschedules
            .Select(r => (Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.At, zone)), r.At, r.Due))
            .OrderBy(r => r.At)
            .ToList();

        // El plan i rige los días d con r_i < d <= r_(i+1) (el plan inicial, los días hasta la primera reprogramación incluida).
        var segmentFrom = DateOnly.MinValue;
        var due = followUp.InitialDue;
        for (var i = 0; i <= reschedules.Count; i++)
        {
            var segmentTo = i < reschedules.Count ? reschedules[i].Day : DateOnly.MaxValue;
            if (due is { } d && d != DateOnly.MaxValue)
            {
                var lo = new[] { segmentFrom, d.AddDays(1), windowFrom }.Max();
                var hi = new[] { segmentTo, windowTo }.Min();
                if (lo <= hi)
                {
                    return true;
                }
            }

            if (i < reschedules.Count)
            {
                segmentFrom = reschedules[i].Day.AddDays(1);
                due = reschedules[i].Due;
            }
        }

        return false;
    }

    private static (DateOnly Start, DateOnly? End) OpenDays(IndicatorFollowUpFact followUp, TimeZoneInfo zone) =>
        (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(followUp.StartedAt, zone)),
         followUp.EndedAt is { } ended ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ended, zone)) : null);
}

/// <summary>DIR-03: tipos de pendiente por los que se filtra la lista. Un episodio puede cumplir varios.</summary>
public enum SupervisionPendingType
{
    SinValorar,
    Escalado,
    Seguimiento,
    SeguimientoVencido,
    IndicacionPendiente,
    IndicacionConIncidencia,
    ProtocoloUrgente,
}

public static class SupervisionPendingRules
{
    /// <summary>Vencido: seguimiento abierto con fecha prevista ya pasada (la misma regla que las bandejas).</summary>
    public static bool IsOverdue(SupervisionEpisode episode, DateOnly today) =>
        episode.HasFollowUp && episode.FollowUpDue < today;

    public static bool Matches(SupervisionPendingType type, SupervisionEpisode episode, DateOnly today) => type switch
    {
        SupervisionPendingType.SinValorar => episode.Status == ClinicalEventStatus.Pendiente,
        SupervisionPendingType.Escalado => episode.Escalated,
        SupervisionPendingType.Seguimiento => episode.HasFollowUp,
        SupervisionPendingType.SeguimientoVencido => IsOverdue(episode, today),
        SupervisionPendingType.IndicacionPendiente => episode.IndicationsPending > 0,
        SupervisionPendingType.IndicacionConIncidencia => episode.IndicationsNotDone > 0,
        SupervisionPendingType.ProtocoloUrgente => episode.HasUrgentProtocol,
        _ => false,
    };
}
