using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// DIR-11: un hito del proceso de un episodio, tal como sale de los registros: cuándo empezó (UTC), cuándo se cumplió (null si sigue sin hacer)
/// si el evento es prioritario (que fija el plazo) y el perfil responsable de cumplirlo. Sin texto clínico: solo el residente y la unidad, como la lista de pendientes.
/// </summary>
public sealed record MilestoneFact(
    ProcessMilestone Milestone, Guid EventId, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string UnitName,
    bool Priority, DateTime Start, DateTime? End, bool EpisodeClosed, SystemProfile Responsible);

/// <summary>Un hito medido, con su plazo y su estado en el momento de la consulta.</summary>
public sealed record MilestoneEntry(
    ProcessMilestone Milestone, Guid EventId, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string UnitName,
    bool Priority, DateTimeOffset Start, DateTimeOffset? End, TimeSpan Term, MilestoneStatus Status, bool EpisodeClosed,
    SystemProfile Responsible);

/// <summary>Medidos (m), fuera de plazo (n: hechos tarde o aún sin hacer con el plazo pasado) y a punto de vencer, de un hito.</summary>
public sealed record MilestoneCount(int Measured, int OutOfTerm, int DueSoon);

public sealed record MilestoneUnitCounts(UnitId UnitId, string UnitName, IReadOnlyDictionary<ProcessMilestone, MilestoneCount> Counts);

/// <summary>
/// DIR-11: la revisión de calidad de proceso de un periodo (From y To incluidos): por hito y unidad, nunca por profesional, y la lista de
/// episodios con algún hito fuera de plazo o a punto de vencer. Los plazos son los del centro (Deadlines).
/// </summary>
public sealed record ProcessQualityReport(
    DateOnly From, DateOnly To, IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline> Deadlines,
    IReadOnlyList<MilestoneUnitCounts> Units, IReadOnlyDictionary<ProcessMilestone, MilestoneCount> Total,
    IReadOnlyList<MilestoneEntry> Exceptions);

public static class ProcessQualityRules
{
    /// <summary>Mide cada hecho con el plazo de su hito. Un hito sin plazo (no se mide) no cuenta, y tampoco uno sin hacer de un episodio ya
    /// cerrado: ya no se puede actuar sobre él. Las unidades salen aunque no tengan nada.</summary>
    public static ProcessQualityReport Build(
        IReadOnlyList<MilestoneFact> facts, IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline> deadlines,
        IReadOnlyList<(UnitId Id, string Name)> units, DateOnly from, DateOnly to, DateTime now)
    {
        var entries = new List<MilestoneEntry>();
        foreach (var fact in facts)
        {
            if (fact.End is null && fact.EpisodeClosed)
            {
                continue;
            }

            if (!deadlines.TryGetValue(fact.Milestone, out var deadline) || deadline.For(fact.Priority) is not { } term)
            {
                continue;
            }

            entries.Add(new MilestoneEntry(
                fact.Milestone, fact.EventId, fact.ResidentId, fact.ResidentDisplayName, fact.UnitId, fact.UnitName, fact.Priority,
                new DateTimeOffset(fact.Start, TimeSpan.Zero), fact.End is { } end ? new DateTimeOffset(end, TimeSpan.Zero) : null, term,
                ProcessMilestoneRules.Evaluate(fact.Start, fact.End, term, now), fact.EpisodeClosed, fact.Responsible));
        }

        static IReadOnlyDictionary<ProcessMilestone, MilestoneCount> Count(IEnumerable<MilestoneEntry> items)
        {
            var list = items.ToList();
            return ProcessMilestoneRules.All.ToDictionary(m => m, m =>
            {
                var of = list.Where(e => e.Milestone == m).ToList();
                return new MilestoneCount(
                    of.Count, of.Count(e => ProcessMilestoneRules.IsOutOfTerm(e.Status)), of.Count(e => e.Status == MilestoneStatus.APuntoDeVencer));
            });
        }

        var exceptions = entries
            .Where(e => ProcessMilestoneRules.IsOutOfTerm(e.Status) || e.Status == MilestoneStatus.APuntoDeVencer)
            .OrderByDescending(e => e.Start)
            .ToList();
        return new ProcessQualityReport(
            from, to, deadlines,
            units.Select(u => new MilestoneUnitCounts(u.Id, u.Name, Count(entries.Where(e => e.UnitId == u.Id)))).ToList(),
            Count(entries), exceptions);
    }
}

/// <summary>DIR-11: de dónde salen los hitos del proceso de un ámbito. Lo implementan Dirección (todo su ámbito) y la bandeja de
/// Enfermería y Medicina (los eventos de su ámbito, con su perfil responsable).</summary>
public interface IMilestoneFactDirectory
{
    /// <summary>Los hitos que empezaron en [from, toExclusive) (UTC) en los eventos del ámbito, con cuándo se cumplieron y el perfil
    /// responsable. Los plazos y estados los pone ProcessQualityRules.</summary>
    Task<IReadOnlyList<MilestoneFact>> ListMilestoneFactsAsync(
        Guid profileScopeId, CenterId centerId, DateTime from, DateTime toExclusive, CancellationToken ct = default);
}
