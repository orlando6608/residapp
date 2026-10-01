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

    /// <summary>DIR-08 a DIR-10: hechos del ámbito con fecha en [from, toExclusive), abiertos o cerrados.</summary>
    Task<SupervisionIndicatorFacts> ListIndicatorFactsAsync(
        Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default);
}

/// <summary>DIR-08 a DIR-10: hechos de los que salen los indicadores agregados. Solo llevan la unidad, códigos y la fecha
/// (hora local del servidor): ni texto, ni residente, ni cuenta, así que ningún indicador puede bajar a una persona
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

public sealed record SupervisionIndicatorFacts(
    IReadOnlyList<IndicatorEpisodeFact> Episodes, IReadOnlyList<IndicatorClosureFact> Closures,
    IReadOnlyList<IndicatorIndicationFact> Indications, IReadOnlyList<IndicatorTransferFact> Transfers);

/// <summary>DIR-08/DIR-09: recuentos de un periodo. Registered es el denominador de Escalated, UrgentProtocols y Referrals;
/// IndicationsIssued, el de los estados de las indicaciones; cada recuento de transferencias, el de sus recepciones.
/// Closed cuenta los cierres del periodo, aunque el episodio se registrara antes, y no tiene denominador.</summary>
public sealed record SupervisionIndicatorCounts(
    int Registered, int FromAuxiliar, int FromNursing, int FromMedicine, int Priority, int Closed,
    int Escalated, int UrgentProtocols, int Referrals,
    int IndicationsIssued, int IndicationsRead, int IndicationsDone, int IndicationsNotDone, int IndicationsUnresolved,
    int NursingTransfers, int NursingTransfersReceived, int MedicalTransfers, int MedicalTransfersReceived);

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

    public static SupervisionIndicators Aggregate(
        SupervisionIndicatorFacts facts, SupervisionScopeInfo scope, DateOnly from, DateOnly to)
    {
        var units = scope.Units.Select(u => new SupervisionUnitIndicators(u.Id, u.Name, Count(facts, from, to, u.Id))).ToList();
        var months = new List<SupervisionMonthIndicators>();
        for (var start = from; start <= to; start = new DateOnly(start.Year, start.Month, 1).AddMonths(1))
        {
            var monthEnd = new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
            var end = monthEnd < to ? monthEnd : to;
            months.Add(new SupervisionMonthIndicators(start, end, Count(facts, start, end, null)));
        }

        return new SupervisionIndicators(scope.CenterName, from, to, units, Count(facts, from, to, null), months);
    }

    private static SupervisionIndicatorCounts Count(SupervisionIndicatorFacts facts, DateOnly from, DateOnly to, UnitId? unit)
    {
        bool In(UnitId unitId, DateTime at)
        {
            var day = DateOnly.FromDateTime(at);
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
            nursing.Count, nursing.Count(t => t.Received), medical.Count, medical.Count(t => t.Received));
    }
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
