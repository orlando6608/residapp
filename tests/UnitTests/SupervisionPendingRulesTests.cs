using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class SupervisionPendingRulesTests
{
    private static readonly DateOnly Day = new(2026, 11, 20);

    private static SupervisionEpisode Episode(DateOnly? due, TimeOnly? time, bool hasFollowUp = true) => new(
        Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente de prueba", UnitId.From(Guid.NewGuid()), "Unidad",
        ClinicalEventOrigin.EventoEnfermeria, DailyChangeClassification.Ordinario, ClinicalEventStatus.EnSeguimiento,
        DateTimeOffset.UtcNow, false, hasFollowUp, due, false, 0, 0, time);

    [Fact]
    public void SeguimientoConHora_VenceCuandoPasaLaHora_SinHoraAlDiaSiguiente()
    {
        var withTime = Episode(Day, new TimeOnly(18, 0));
        var withoutTime = Episode(Day, null);

        Assert.False(SupervisionPendingRules.IsOverdue(withTime, Day.ToDateTime(new TimeOnly(17, 59))));
        Assert.True(SupervisionPendingRules.IsOverdue(withTime, Day.ToDateTime(new TimeOnly(18, 1))));
        Assert.False(SupervisionPendingRules.IsOverdue(withoutTime, Day.ToDateTime(new TimeOnly(23, 59))));
        Assert.True(SupervisionPendingRules.IsOverdue(withoutTime, Day.AddDays(1).ToDateTime(new TimeOnly(0, 1))));
    }

    [Fact]
    public void SeguimientoVencido_EnLaLista_UsaLaMismaRegla()
    {
        var episode = Episode(Day, new TimeOnly(18, 0));

        Assert.False(SupervisionPendingRules.Matches(SupervisionPendingType.SeguimientoVencido, episode, Day.ToDateTime(new TimeOnly(9, 0))));
        Assert.True(SupervisionPendingRules.Matches(SupervisionPendingType.SeguimientoVencido, episode, Day.ToDateTime(new TimeOnly(19, 0))));
    }

    [Fact]
    public void SinSeguimientoOSinFecha_NoVenceNunca()
    {
        Assert.False(SupervisionPendingRules.IsOverdue(Episode(Day, new TimeOnly(1, 0), hasFollowUp: false), Day.AddYears(1).ToDateTime(TimeOnly.MinValue)));
        Assert.False(SupervisionPendingRules.IsOverdue(Episode(null, null), Day.AddYears(1).ToDateTime(TimeOnly.MinValue)));
    }
}
