using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Accounts;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>El cuadro de mando de Administración: los tramos del periodo, en qué tramo cuenta cada hecho y los dos avisos nuevos del Inicio.</summary>
public class AdministrationDashboardTests
{
    private static readonly TimeZoneInfo Madrid = CenterTime.Madrid;

    // Miércoles 25 de noviembre de 2026, a mediodía en Madrid.
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 11, 0, 0, TimeSpan.Zero);

    private static DateOnly Day(int month, int day, int year = 2026) => new(year, month, day);

    private static DateTime Utc(int month, int day, int hour = 10, int year = 2026) => new(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);

    private static DashboardPeriod Period(DashboardRange range) => DashboardPeriod.At(range, Now, Madrid);

    [Fact]
    public void SieteDias_UnTramoPorDia_HastaHoy_YElPeriodoAnteriorDuraLoMismo()
    {
        var period = Period(DashboardRange.SieteDias);

        Assert.Equal(7, period.Buckets.Count);
        Assert.All(period.Buckets, b => Assert.Equal(b.From, b.To));
        Assert.Equal((Day(11, 19), Day(11, 25)), (period.From, period.Today));
        Assert.Equal((Day(11, 12), Day(11, 18)), (period.PreviousFrom, period.PreviousTo));
    }

    [Fact]
    public void TreintaDias_SemanasDeLunesADomingo_RecortadasEnLosExtremos_SinHuecos()
    {
        var period = Period(DashboardRange.TreintaDias);

        Assert.Equal(Day(10, 27), period.From);
        Assert.Equal(
            [new(Day(10, 27), Day(11, 1)), new(Day(11, 2), Day(11, 8)), new(Day(11, 9), Day(11, 15)), new(Day(11, 16), Day(11, 22)), new(Day(11, 23), Day(11, 25))],
            period.Buckets.ToArray());
        Assert.Equal(30, period.Buckets.Sum(b => b.To.DayNumber - b.From.DayNumber + 1));
        Assert.Equal((Day(9, 27), Day(10, 26)), (period.PreviousFrom, period.PreviousTo));
    }

    [Fact]
    public void DoceMeses_MesesNaturales_ElUltimoHastaHoy()
    {
        var period = Period(DashboardRange.DoceMeses);

        Assert.Equal(12, period.Buckets.Count);
        Assert.Equal(new DashboardBucket(Day(12, 1, 2025), Day(12, 31, 2025)), period.Buckets[0]);
        Assert.Equal(new DashboardBucket(Day(11, 1), Day(11, 25)), period.Buckets[^1]);
        Assert.Equal((Day(12, 1, 2024), Day(11, 25, 2025)), (period.PreviousFrom, period.PreviousTo));
    }

    [Fact]
    public void ElDiaEsElDeLaZonaDelCentro()
    {
        var lateAtNight = new DateTimeOffset(2026, 11, 24, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal(Day(11, 25), DashboardPeriod.At(DashboardRange.SieteDias, lateAtNight, Madrid).Today);
        Assert.Equal(Day(11, 24), DashboardPeriod.At(DashboardRange.SieteDias, lateAtNight, TimeZoneInfo.FindSystemTimeZoneById("Atlantic/Canary")).Today);
    }

    [Fact]
    public void Movimientos_CadaHechoEnSuDia_LaFechaDeIngresoMandaSobreElRegistro()
    {
        var period = Period(DashboardRange.SieteDias);
        var facts = new DashboardMovementFacts(
            [
                new(null, Utc(11, 25)),                                   // hoy
                new(null, Utc(11, 24, 23)),                               // en Madrid ya es el 25
                new(Utc(11, 20, 0), Utc(11, 25)),                         // importado: cuenta el 20
                new(null, Utc(11, 15)),                                   // periodo anterior
                new(Utc(1, 1, 0), Utc(11, 25)),                           // ingresó hace meses: no es un alta de ahora
            ],
            [
                new(ResidentDischargeReason.Fallecimiento, Utc(11, 25)),
                new(ResidentDischargeReason.Otro, Utc(11, 19)),
                new(ResidentDischargeReason.AltaVoluntaria, Utc(11, 12)),
                new(ResidentDischargeReason.TrasladoOtroCentro, Utc(11, 11)), // antes del periodo anterior
            ]);

        var movements = DashboardRules.Movements(facts, period);

        Assert.Equal([0, 1, 0, 0, 0, 0, 2], movements.Buckets.Select(b => b.Admissions));
        Assert.Equal([1, 0, 0, 0, 0, 0, 1], movements.Buckets.Select(b => b.Discharges.Total));
        Assert.Equal((3, 2), (movements.Admissions, movements.Discharges));
        Assert.Equal(2, movements.AdmissionsToday);
        Assert.Equal(new DashboardReasonCounts(1, 0, 0, 0), movements.DischargesToday);
        Assert.Equal((1, 1), (movements.AdmissionsPrevious, movements.DischargesPrevious));
    }

    [Fact]
    public void Autorizaciones_ElEstadoAlTerminarCadaTramo_ConElUltimoCambioDeEntonces()
    {
        var period = Period(DashboardRange.SieteDias);
        DashboardAuthorizationChange Change(int number, FamilyAuthorizationStatus status, DateTime at, DateOnly? until = null) => new(number, status, until, at);
        var links = new DashboardLinkFact[]
        {
            // Sin ningún cambio: pendiente desde que se vincula, el 22.
            new(Utc(11, 22), null, []),
            // Pendiente, activada el 21 con fin el 23: activa hasta el 23 incluido y caducada después.
            new(Utc(11, 1), null, [Change(1, FamilyAuthorizationStatus.Pendiente, Utc(11, 1)),
                Change(2, FamilyAuthorizationStatus.Activa, Utc(11, 21), Day(11, 23))]),
            // Suspendida el 24.
            new(Utc(11, 1), null, [Change(1, FamilyAuthorizationStatus.Activa, Utc(11, 1)), Change(2, FamilyAuthorizationStatus.Suspendida, Utc(11, 24))]),
            // Revocada: no cuenta desde que se revoca (el 20).
            new(Utc(11, 1), null, [Change(1, FamilyAuthorizationStatus.Activa, Utc(11, 1)), Change(2, FamilyAuthorizationStatus.Revocada, Utc(11, 20))]),
            // Desvinculado el 23: cuenta hasta el 22.
            new(Utc(11, 1), Utc(11, 23), [Change(1, FamilyAuthorizationStatus.Activa, Utc(11, 1))]),
        };

        var buckets = DashboardRules.Authorizations(links, period);

        (int, int, int, int) At(int index) => (buckets[index].Active, buckets[index].Pending, buckets[index].Expired, buckets[index].Suspended);
        Assert.Equal((3, 1, 0, 0), At(0));  // 19: las dos activas, la desvinculada y la pendiente del segundo vínculo
        Assert.Equal((2, 1, 0, 0), At(1));  // 20: se revoca una
        Assert.Equal((3, 0, 0, 0), At(2));  // 21: se activa la del segundo vínculo
        Assert.Equal((3, 1, 0, 0), At(3));  // 22: se vincula el primero
        Assert.Equal((2, 1, 0, 0), At(4));  // 23: último día válido; el quinto ya no está
        Assert.Equal((0, 1, 1, 1), At(5));  // 24: caducada y suspendida
        Assert.Equal((0, 1, 1, 1), At(6));  // hoy
    }

    [Fact]
    public void Citas_PorTramoYEstado()
    {
        var period = Period(DashboardRange.SieteDias);
        var buckets = DashboardRules.Appointments(
            [
                new(Utc(11, 25), Domain.Appointments.AppointmentStatus.Confirmada),
                new(Utc(11, 25), Domain.Appointments.AppointmentStatus.Cancelada),
                new(Utc(11, 19), Domain.Appointments.AppointmentStatus.Finalizada),
                new(Utc(11, 18), Domain.Appointments.AppointmentStatus.Finalizada), // fuera del periodo
            ], period);

        Assert.Equal((1, 0, 0), (buckets[0].Finished, buckets[0].Confirmed, buckets[0].Cancelled));
        Assert.Equal((0, 1, 1), (buckets[^1].Finished, buckets[^1].Confirmed, buckets[^1].Cancelled));
        Assert.Equal(3, buckets.Sum(b => b.Total));
    }

    [Fact]
    public void Ocupacion_LosTotalesDePlazasSoloSumanLasUnidadesQueLasTienen()
    {
        var occupancy = new DashboardOccupancy(
        [
            new(Guid.NewGuid(), "A", null, 9, 10, 8, 7, 6),
            new(Guid.NewGuid(), "B", null, 5, 0, 0, 5, 0),
        ]);

        Assert.Equal((14, 10, 8, 6), (occupancy.Residents, occupancy.Places, occupancy.Occupied, occupancy.OccupiedBefore));
        Assert.Equal(80, DashboardOccupancy.Percent(occupancy.Occupied, occupancy.Places));
        Assert.Equal(13, DashboardOccupancy.Percent(1, 8));
        Assert.Null(DashboardOccupancy.Percent(0, 0));
        // «100 %» solo si está lleno y «0 %» solo si está vacío.
        Assert.Equal((99, 100, 1, 0), (DashboardOccupancy.Percent(199, 200)!.Value, DashboardOccupancy.Percent(200, 200)!.Value,
            DashboardOccupancy.Percent(1, 300)!.Value, DashboardOccupancy.Percent(0, 300)!.Value));
    }

    private static ProfessionalAccountSummary Account(AccountStatus status, DateTimeOffset? invitation, bool revoked = false) => new(
        AccountId.From(Guid.NewGuid()), "cuenta", null, status,
        [new AccountProfileScope(Guid.NewGuid(), SystemProfile.Enfermeria, Now.AddDays(-30), "alguien", revoked ? Now.AddDays(-1) : null, null, [], [], [])],
        invitation);

    [Fact]
    public void InvitacionSinUsar_SoloDeUnaCuentaQueLaPuedaUsar_YCaducadaEnSuInstante()
    {
        var live = Account(AccountStatus.Active, Now.AddMinutes(1));
        var expired = Account(AccountStatus.Active, Now);
        var none = Account(AccountStatus.Active, null);
        var suspended = Account(AccountStatus.Suspended, Now.AddDays(1));
        var withoutProfile = Account(AccountStatus.Active, Now.AddDays(1), revoked: true);

        Assert.Equal([true, true, false, false, false],
            new[] { live, expired, none, suspended, withoutProfile }.Select(ProfessionalAccountAttention.HasOpenInvitation));
        Assert.Equal([false, true, false, false, false],
            new[] { live, expired, none, suspended, withoutProfile }.Select(a => ProfessionalAccountAttention.HasExpiredInvitation(a, Now)));
    }
}
