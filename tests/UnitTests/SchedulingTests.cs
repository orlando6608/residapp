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
