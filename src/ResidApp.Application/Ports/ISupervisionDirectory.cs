using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
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
