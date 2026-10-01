using ResidApp.Shared;

namespace ResidApp.Domain.Scheduling;

/// <summary>Un turno planificado de un equipo en una fecha (ya guardado o por guardar), con las horas de su turno.</summary>
public sealed record PlannedShift(Guid TeamId, string TeamName, Guid ShiftId, string ShiftName, TimeOnly Start, TimeOnly End, DateOnly Date);

/// <summary>ADM-17: SameTeam es el mismo equipo en dos turnos que se solapan; SamePerson, una persona que está en dos equipos con
/// turnos que se solapan.</summary>
public enum ScheduleConflictKind
{
    SameTeam,
    SamePerson,
}

/// <summary>ADM-17: un solapamiento entre lo que se quiere planificar (Team/Shift/Date) y otro turno ya planificado. PersonName solo
/// va en SamePerson.</summary>
public sealed record ScheduleConflict(
    ScheduleConflictKind Kind, DateOnly Date, string TeamName, string ShiftName, string OtherTeamName, string OtherShiftName, string? PersonName);

/// <summary>
/// ADM-14/15/17 (script 0027): reglas de la planificación puntual. Un lote son de 1 a 62 fechas distintas, de hoy a un año vista. Los
/// conflictos se calculan con los intervalos reales de los turnos (cruce de medianoche incluido). Que el servidor avise no impide
/// planificar: Administración decide y deja una justificación (de 1 a 500 caracteres).
/// </summary>
public static class SchedulePlan
{
    public const int MaxDates = 62;
    public const int MaxHorizonDays = 366;
    public const int MaxJustificationLength = 500;
    public const string InvalidCode = "SCHEDULE_INVALID";
    public const string ConflictsCode = "SCHEDULE_CONFLICTS";

    public static IReadOnlyList<DateOnly> ValidateDates(IEnumerable<DateOnly>? dates, DateOnly today)
    {
        var list = dates?.ToList() ?? [];
        if (list.Count is 0 or > MaxDates || list.Distinct().Count() != list.Count
            || list.Any(d => d < today || d > today.AddDays(MaxHorizonDays)))
        {
            throw new DomainValidationException(InvalidCode);
        }

        return list.Order().ToList();
    }

    /// <summary>La justificación recortada, o null si no se dio; más de 500 caracteres es inválido.</summary>
    public static string? ValidateJustification(string? justification)
    {
        var text = justification?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > MaxJustificationLength ? throw new DomainValidationException(InvalidCode) : text;
    }

    /// <summary>
    /// Los conflictos de lo que se quiere planificar (proposed) con lo ya planificado (existing). membersByTeam son los miembros
    /// vigentes de cada equipo. Un mismo par de turnos se cuenta una vez; el mismo equipo y turno en la misma fecha no es un
    /// conflicto (es un duplicado y se trata aparte).
    /// </summary>
    public static IReadOnlyList<ScheduleConflict> FindConflicts(
        IReadOnlyList<PlannedShift> proposed, IReadOnlyList<PlannedShift> existing,
        IReadOnlyDictionary<Guid, IReadOnlyList<(Guid AccountId, string Name)>> membersByTeam)
    {
        var conflicts = new List<ScheduleConflict>();
        foreach (var p in proposed)
        {
            foreach (var e in existing)
            {
                if (e.TeamId == p.TeamId && e.ShiftId == p.ShiftId && e.Date == p.Date)
                {
                    continue;
                }

                if (!Shift.Overlaps(p.Date, p.Start, p.End, e.Date, e.Start, e.End))
                {
                    continue;
                }

                if (e.TeamId == p.TeamId)
                {
                    conflicts.Add(new ScheduleConflict(ScheduleConflictKind.SameTeam, p.Date, p.TeamName, p.ShiftName, e.TeamName, e.ShiftName, null));
                    continue;
                }

                var mine = membersByTeam.TryGetValue(p.TeamId, out var a) ? a : [];
                var theirs = membersByTeam.TryGetValue(e.TeamId, out var b) ? b : [];
                foreach (var person in mine.Where(m => theirs.Any(t => t.AccountId == m.AccountId)))
                {
                    conflicts.Add(new ScheduleConflict(
                        ScheduleConflictKind.SamePerson, p.Date, p.TeamName, p.ShiftName, e.TeamName, e.ShiftName, person.Name));
                }
            }
        }

        return conflicts
            .DistinctBy(c => (c.Kind, c.Date, c.TeamName, c.ShiftName, c.OtherTeamName, c.OtherShiftName, c.PersonName))
            .OrderBy(c => c.Date).ThenBy(c => c.Kind).ThenBy(c => c.OtherTeamName, StringComparer.Ordinal).ThenBy(c => c.PersonName, StringComparer.Ordinal)
            .ToList();
    }
}
