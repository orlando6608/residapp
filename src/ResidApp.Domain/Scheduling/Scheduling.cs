using ResidApp.Shared;

namespace ResidApp.Domain.Scheduling;

/// <summary>ADM-14 (script 0027): un turno del catálogo del centro. End igual o anterior a Start cruza la medianoche. Las horas no
/// cambian nunca (ADM-16: no reescribir lo histórico); para cambiarlas se crea otro turno.</summary>
public sealed record ShiftData(string Name, TimeOnly Start, TimeOnly End);

/// <summary>
/// Reglas de los turnos. El nombre lleva de 1 a 100 caracteres tras recortar; las horas son de minuto (la BD guarda TIME(0)).
/// Solo se comparan intervalos reales: un turno de la fecha D va de D + Start a D + End, y un día más si cruza la medianoche.
/// </summary>
public static class Shift
{
    public const int MaxNameLength = 100;
    public const string InvalidCode = "SHIFT_INVALID";

    public static ShiftData Validate(string? name, TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null)
        {
            throw new DomainValidationException(InvalidCode);
        }

        return new ShiftData(ValidateName(name), Truncate(start.Value), Truncate(end.Value));
    }

    public static string ValidateName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength
            ? throw new DomainValidationException(InvalidCode)
            : text;
    }

    public static bool CrossesMidnight(TimeOnly start, TimeOnly end) => end <= start;

    /// <summary>El intervalo [From, To) del turno en la fecha date (hora local de pared, sin zona).</summary>
    public static (DateTime From, DateTime To) Interval(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var from = date.ToDateTime(start);
        var to = date.ToDateTime(end);
        return (from, CrossesMidnight(start, end) ? to.AddDays(1) : to);
    }

    /// <summary>Dos turnos en fechas dadas se solapan si sus intervalos se cruzan. Que uno termine justo cuando empieza el otro
    /// no es solapamiento.</summary>
    public static bool Overlaps(DateOnly dateA, TimeOnly startA, TimeOnly endA, DateOnly dateB, TimeOnly startB, TimeOnly endB)
    {
        var a = Interval(dateA, startA, endA);
        var b = Interval(dateB, startB, endB);
        return a.From < b.To && b.From < a.To;
    }

    private static TimeOnly Truncate(TimeOnly time) => new(time.Hour, time.Minute);
}

/// <summary>ADM-14: reglas de los equipos de una unidad. El nombre lleva de 1 a 100 caracteres tras recortar.</summary>
public static class Team
{
    public const int MaxNameLength = 100;
    public const string InvalidCode = "TEAM_INVALID";

    public static string ValidateName(string? name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength
            ? throw new DomainValidationException(InvalidCode)
            : text;
    }
}
