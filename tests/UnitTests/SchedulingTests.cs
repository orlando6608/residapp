using ResidApp.Domain.Scheduling;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>ADM-14 (0027): turnos y equipos. Las reglas de solapamiento se usan en la planificación (fase 2).</summary>
public class SchedulingTests
{
    private static TimeOnly T(string time) => TimeOnly.Parse(time);

    private static readonly DateOnly Monday = new(2026, 10, 5);

    [Fact]
    public void Turno_SeRecortaYGuardaLasHorasDeMinuto()
    {
        var data = Shift.Validate("  Mañana ", new TimeOnly(7, 0, 45), new TimeOnly(15, 30, 10));

        Assert.Equal(new ShiftData("Mañana", new TimeOnly(7, 0), new TimeOnly(15, 30)), data);
    }

    [Theory]
    [InlineData(null, "07:00", "15:00")]
    [InlineData("  ", "07:00", "15:00")]
    [InlineData("Mañana", null, "15:00")]
    [InlineData("Mañana", "07:00", null)]
    public void Turno_ConDatosIncompletos_SeRechaza(string? name, string? start, string? end) =>
        Assert.Equal(Shift.InvalidCode, Assert.Throws<DomainValidationException>(() => Shift.Validate(
            name, start is null ? null : T(start), end is null ? null : T(end))).Message);

    [Fact]
    public void Turno_Y_Equipo_TienenLimiteDeNombre()
    {
        Assert.Equal(Shift.MaxNameLength, Shift.ValidateName(new string('n', Shift.MaxNameLength)).Length);
        Assert.Throws<DomainValidationException>(() => Shift.ValidateName(new string('n', Shift.MaxNameLength + 1)));
        Assert.Equal(Team.MaxNameLength, Team.ValidateName(new string('n', Team.MaxNameLength)).Length);
        Assert.Equal(Team.InvalidCode, Assert.Throws<DomainValidationException>(() => Team.ValidateName(" ")).Message);
    }

    [Theory]
    [InlineData("07:00", "15:00", false)]
    [InlineData("22:00", "06:00", true)]
    [InlineData("10:00", "10:00", true)]
    public void Turno_CruzaLaMedianoche_SiLaHoraDeFinNoEsPosterior(string start, string end, bool crosses) =>
        Assert.Equal(crosses, Shift.CrossesMidnight(T(start), T(end)));

    [Fact]
    public void Intervalo_UnTurnoNocturnoTerminaAlDiaSiguiente()
    {
        var (from, to) = Shift.Interval(Monday, T("22:00"), T("06:00"));

        Assert.Equal(new DateTime(2026, 10, 5, 22, 0, 0), from);
        Assert.Equal(new DateTime(2026, 10, 6, 6, 0, 0), to);
    }

    [Theory]
    [InlineData("07:00", "15:00", "14:00", "22:00", true)]
    [InlineData("07:00", "15:00", "15:00", "23:00", false)]
    [InlineData("07:00", "15:00", "23:00", "07:00", false)]
    [InlineData("07:00", "15:00", "09:00", "11:00", true)]
    [InlineData("07:00", "15:00", "07:00", "15:00", true)]
    public void Solapamiento_EnElMismoDia(string startA, string endA, string startB, string endB, bool overlaps) =>
        Assert.Equal(overlaps, Shift.Overlaps(Monday, T(startA), T(endA), Monday, T(startB), T(endB)));

    [Fact]
    public void Solapamiento_ElTurnoNocturnoDelDiaAnterior_PisaLaMananaDelSiguiente()
    {
        // Noche del lunes (22:00 – 06:00) y mañana del martes (05:00 – 13:00) se solapan una hora.
        Assert.True(Shift.Overlaps(Monday, T("22:00"), T("06:00"), Monday.AddDays(1), T("05:00"), T("13:00")));
        // Y la simetría: da igual el orden.
        Assert.True(Shift.Overlaps(Monday.AddDays(1), T("05:00"), T("13:00"), Monday, T("22:00"), T("06:00")));
        // Si la mañana empieza justo cuando termina la noche, no hay solapamiento.
        Assert.False(Shift.Overlaps(Monday, T("22:00"), T("06:00"), Monday.AddDays(1), T("06:00"), T("14:00")));
        // La noche del lunes no toca la mañana del lunes.
        Assert.False(Shift.Overlaps(Monday, T("22:00"), T("06:00"), Monday, T("06:00"), T("14:00")));
    }

    [Fact]
    public void Solapamiento_DosNocturnosDeDiasSeguidos_NoSeSolapan_PeroUnoDe24HorasSi()
    {
        Assert.False(Shift.Overlaps(Monday, T("22:00"), T("06:00"), Monday.AddDays(1), T("22:00"), T("06:00")));
        Assert.True(Shift.Overlaps(Monday, T("10:00"), T("10:00"), Monday.AddDays(1), T("09:00"), T("11:00")));
    }
}

/// <summary>ADM-15/17 (0027): fechas de un lote, justificación y detección de solapamientos (funciones puras).</summary>
public class SchedulePlanTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);
    private static TimeOnly T(string time) => TimeOnly.Parse(time);

    private static PlannedShift Planned(Guid team, string teamName, Guid shift, string shiftName, string start, string end, DateOnly date) =>
        new(team, teamName, shift, shiftName, T(start), T(end), date);

    [Fact]
    public void Fechas_SeOrdenan_YSeAceptanDeHoyAUnAnoVista()
    {
        var dates = SchedulePlan.ValidateDates([Today.AddDays(3), Today, Today.AddDays(366)], Today);

        Assert.Equal([Today, Today.AddDays(3), Today.AddDays(366)], dates);
    }

    [Fact]
    public void Fechas_Invalidas_SeRechazan()
    {
        var invalid = new IEnumerable<DateOnly>?[]
        {
            null,
            [],
            [Today.AddDays(-1)],
            [Today.AddDays(367)],
            [Today, Today],
            Enumerable.Range(0, 368).Select(i => Today.AddDays(i)),
        };

        Assert.All(invalid, dates => Assert.Equal(SchedulePlan.InvalidCode, Assert.Throws<DomainValidationException>(
            () => SchedulePlan.ValidateDates(dates, Today)).Message));
        Assert.Equal(SchedulePlan.MaxDates, SchedulePlan.ValidateDates(Enumerable.Range(0, 367).Select(i => Today.AddDays(i)), Today).Count);
        Assert.Equal(63, SchedulePlan.ValidateDates(Enumerable.Range(0, 63).Select(i => Today.AddDays(i)), Today).Count);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Refuerzo  ", "Refuerzo")]
    public void Justificacion_SeRecortaOQuedaVacia(string? text, string? expected) =>
        Assert.Equal(expected, SchedulePlan.ValidateJustification(text));

    [Fact]
    public void Justificacion_TieneLimiteDeLongitud()
    {
        Assert.Equal(500, SchedulePlan.ValidateJustification(new string('x', 500))!.Length);
        Assert.Throws<DomainValidationException>(() => SchedulePlan.ValidateJustification(new string('x', 501)));
    }

    [Fact]
    public void Conflictos_MismoEquipo_PersonaEnDosEquipos_YSinSolapamiento()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), morning = Guid.NewGuid(), afternoon = Guid.NewGuid(), night = Guid.NewGuid();
        var ana = (Guid.NewGuid(), "Ana");
        var members = new Dictionary<Guid, IReadOnlyList<(Guid AccountId, string Name)>>
        {
            [a] = [ana, (Guid.NewGuid(), "Luis")],
            [b] = [ana],
            [c] = [(Guid.NewGuid(), "Eva")],
        };
        var existing = new[]
        {
            Planned(a, "A", morning, "Mañana", "07:00", "15:00", Today),
            Planned(c, "C", morning, "Mañana", "07:00", "15:00", Today),
            Planned(a, "A", night, "Noche", "22:00", "06:00", Today.AddDays(-1)),
        };
        var proposed = new[]
        {
            Planned(a, "A", afternoon, "Tarde", "14:00", "22:00", Today),
            Planned(b, "B", afternoon, "Tarde", "14:00", "22:00", Today),
            Planned(b, "B", morning, "Mañana", "07:00", "15:00", Today),
        };

        var conflicts = SchedulePlan.FindConflicts(proposed, existing, members);

        Assert.Contains(conflicts, k => k.Kind == ScheduleConflictKind.SameTeam && k.TeamName == "A" && k.OtherShiftName == "Mañana");
        Assert.Contains(conflicts, k => k.Kind == ScheduleConflictKind.SamePerson && k.TeamName == "B" && k.OtherTeamName == "A" && k.PersonName == "Ana");
        Assert.DoesNotContain(conflicts, k => k.OtherTeamName == "C");
        // La noche del día anterior termina a las 06:00 y no pisa ni la mañana ni la tarde.
        Assert.DoesNotContain(conflicts, k => k.OtherShiftName == "Noche");
        // Sin duplicados: el mismo par se cuenta una vez.
        Assert.Equal(conflicts.Count, conflicts.Distinct().Count());
    }

    [Fact]
    public void Conflictos_ElMismoEquipoYTurnoEnLaMismaFecha_EsUnDuplicado_NoUnConflicto()
    {
        Guid a = Guid.NewGuid(), morning = Guid.NewGuid();
        var one = Planned(a, "A", morning, "Mañana", "07:00", "15:00", Today);

        Assert.Empty(SchedulePlan.FindConflicts([one], [one], new Dictionary<Guid, IReadOnlyList<(Guid, string)>>()));
    }
}
