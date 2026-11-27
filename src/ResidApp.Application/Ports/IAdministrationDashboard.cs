using ResidApp.Domain.Appointments;
using ResidApp.Domain.Families;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>El periodo que se elige en un cuadro de mando. Nada se guarda: viaja en la dirección de la página.</summary>
public enum DashboardRange { SieteDias, TreintaDias, DoceMeses }

/// <summary>Un tramo de un gráfico, con sus dos días incluidos.</summary>
public sealed record DashboardBucket(DateOnly From, DateOnly To);

/// <summary>
/// El periodo de un cuadro de mando en la zona del centro y sus tramos: por días (7 días), por semanas de lunes a domingo (30 días) o por meses naturales
/// (12 meses); el primero y el último pueden quedar recortados. El periodo anterior, con el que se compara, dura lo mismo.
/// </summary>
public sealed record DashboardPeriod(DashboardRange Range, DateTimeOffset Now, TimeZoneInfo Zone, DateOnly Today, IReadOnlyList<DashboardBucket> Buckets)
{
    public DateOnly From => Buckets[0].From;

    public DateOnly PreviousFrom => Range switch
    {
        DashboardRange.SieteDias => From.AddDays(-7),
        DashboardRange.TreintaDias => From.AddDays(-30),
        _ => From.AddYears(-1),
    };

    public DateOnly PreviousTo => Range == DashboardRange.DoceMeses ? Today.AddYears(-1) : From.AddDays(-1);

    public static DashboardPeriod At(DashboardRange range, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var from = range switch
        {
            DashboardRange.SieteDias => today.AddDays(-6),
            DashboardRange.TreintaDias => today.AddDays(-29),
            _ => new DateOnly(today.Year, today.Month, 1).AddMonths(-11),
        };
        var buckets = new List<DashboardBucket>();
        for (var start = from; start <= today;)
        {
            var next = range switch
            {
                DashboardRange.SieteDias => start.AddDays(1),
                DashboardRange.TreintaDias => start.AddDays(7 - (((int)start.DayOfWeek + 6) % 7)),
                _ => new DateOnly(start.Year, start.Month, 1).AddMonths(1),
            };
            buckets.Add(new DashboardBucket(start, next.AddDays(-1) < today ? next.AddDays(-1) : today));
            start = next;
        }

        return new DashboardPeriod(range, now, zone, today, buckets);
    }

    /// <summary>El día, en la zona del centro, de un hecho guardado en UTC.</summary>
    public DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));
}

/// <summary>
/// Una unidad del ámbito. Residents son sus residentes activos; Places, sus plazas activas en habitaciones activas (0: no tiene plazas definidas); Occupied,
/// las ocupadas. ResidentsBefore y OccupiedBefore, lo mismo al empezar el periodo: la ocupación pasada se calcula con las plazas de hoy. Una unidad
/// inactiva (Active en falso) ya no tiene residentes ni plazas, pero pudo tenerlos entonces: cuenta en lo pasado y no se enseña.
/// </summary>
public sealed record DashboardUnit(
    Guid UnitId, string UnitName, string? BuildingName, int Residents, int Places, int Occupied, int ResidentsBefore, int OccupiedBefore,
    bool Active = true);

/// <summary>Las unidades del ámbito, por nombre. Los totales de plazas solo suman las unidades que las tienen.</summary>
public sealed record DashboardOccupancy(IReadOnlyList<DashboardUnit> Units)
{
    public IEnumerable<DashboardUnit> ActiveUnits => Units.Where(u => u.Active);

    public int Residents => Units.Sum(u => u.Residents);

    public int ResidentsBefore => Units.Sum(u => u.ResidentsBefore);

    public int Places => Units.Sum(u => u.Places);

    public int Occupied => Units.Sum(u => u.Occupied);

    public int OccupiedBefore => Units.Where(u => u.Places > 0).Sum(u => u.OccupiedBefore);

    /// <summary>El porcentaje entero de plazas ocupadas; null si no hay plazas. Los extremos solo se dicen cuando son ciertos: «100 %» es lleno y «0 %», vacío.</summary>
    public static int? Percent(int occupied, int places)
    {
        if (places == 0)
        {
            return null;
        }

        var percent = (int)Math.Round(occupied * 100.0 / places, MidpointRounding.AwayFromZero);
        return percent == 100 && occupied < places ? 99 : percent == 0 && occupied > 0 ? 1 : percent;
    }
}

/// <summary>Un alta: la fecha de ingreso, si consta (solo la primera estancia, la que trae la importación), y cuándo se registró (UTC).</summary>
public sealed record DashboardAdmissionFact(DateTime? AdmissionDate, DateTime RegisteredAt);

/// <summary>Una baja, solo con su categoría: nunca el texto del motivo.</summary>
public sealed record DashboardDischargeFact(ResidentDischargeReason Reason, DateTime DischargedAt);

public sealed record DashboardMovementFacts(IReadOnlyList<DashboardAdmissionFact> Admissions, IReadOnlyList<DashboardDischargeFact> Discharges);

/// <summary>Las bajas por cada una de las cuatro categorías cerradas.</summary>
public sealed record DashboardReasonCounts(int Death, int Voluntary, int Transfer, int Other)
{
    public int Total => Death + Voluntary + Transfer + Other;

    public int Of(ResidentDischargeReason reason) => reason switch
    {
        ResidentDischargeReason.Fallecimiento => Death,
        ResidentDischargeReason.AltaVoluntaria => Voluntary,
        ResidentDischargeReason.TrasladoOtroCentro => Transfer,
        _ => Other,
    };

    public static DashboardReasonCounts From(IEnumerable<DashboardDischargeFact> discharges)
    {
        var list = discharges.ToList();
        int Count(ResidentDischargeReason reason) => list.Count(d => d.Reason == reason);
        return new(Count(ResidentDischargeReason.Fallecimiento), Count(ResidentDischargeReason.AltaVoluntaria),
            Count(ResidentDischargeReason.TrasladoOtroCentro), Count(ResidentDischargeReason.Otro));
    }
}

public sealed record DashboardMovementBucket(DashboardBucket Bucket, int Admissions, DashboardReasonCounts Discharges);

public sealed record DashboardMovements(
    IReadOnlyList<DashboardMovementBucket> Buckets, int AdmissionsToday, DashboardReasonCounts DischargesToday, int AdmissionsPrevious, int DischargesPrevious)
{
    public int Admissions => Buckets.Sum(b => b.Admissions);

    public int Discharges => Buckets.Sum(b => b.Discharges.Total);
}

/// <summary>Una cita con la familia: cuándo empieza (UTC) y cómo está.</summary>
public sealed record DashboardAppointmentFact(DateTime StartsAt, AppointmentStatus Status);

public sealed record DashboardAppointmentBucket(DashboardBucket Bucket, int Finished, int Confirmed, int Cancelled)
{
    public int Total => Finished + Confirmed + Cancelled;
}

/// <summary>Un cambio de la autorización de un vínculo; el vigente es el de número más alto.</summary>
public sealed record DashboardAuthorizationChange(int Number, FamilyAuthorizationStatus Status, DateOnly? ValidUntil, DateTime RecordedAt);

/// <summary>Un vínculo de un familiar con un residente, con todo su historial de autorización. Sin nombres.</summary>
public sealed record DashboardLinkFact(DateTime LinkedAt, DateTime? UnlinkedAt, IReadOnlyList<DashboardAuthorizationChange> Changes);

/// <summary>Cómo estaban las autorizaciones al final de un tramo. Las revocadas no cuentan.</summary>
public sealed record DashboardAuthorizationBucket(DashboardBucket Bucket, int Active, int Pending, int Expired, int Suspended)
{
    public int Total => Active + Pending + Expired + Suspended;
}

/// <summary>El reparto de los hechos por tramos: cada uno cuenta en su día de la zona del centro.</summary>
public static class DashboardRules
{
    public static DashboardMovements Movements(DashboardMovementFacts facts, DashboardPeriod period)
    {
        DateOnly AdmissionDay(DashboardAdmissionFact a) => a.AdmissionDate is { } date ? DateOnly.FromDateTime(date) : period.DayOf(a.RegisteredAt);
        var admissions = facts.Admissions.Select(AdmissionDay).ToList();
        var discharges = facts.Discharges.Select(d => (Day: period.DayOf(d.DischargedAt), Fact: d)).ToList();
        int AdmittedBetween(DateOnly from, DateOnly to) => admissions.Count(day => day >= from && day <= to);
        IEnumerable<DashboardDischargeFact> DischargedBetween(DateOnly from, DateOnly to) =>
            discharges.Where(d => d.Day >= from && d.Day <= to).Select(d => d.Fact);
        return new DashboardMovements(
            [.. period.Buckets.Select(b => new DashboardMovementBucket(b, AdmittedBetween(b.From, b.To), DashboardReasonCounts.From(DischargedBetween(b.From, b.To))))],
            AdmittedBetween(period.Today, period.Today),
            DashboardReasonCounts.From(DischargedBetween(period.Today, period.Today)),
            AdmittedBetween(period.PreviousFrom, period.PreviousTo),
            DischargedBetween(period.PreviousFrom, period.PreviousTo).Count());
    }

    public static IReadOnlyList<DashboardAppointmentBucket> Appointments(IReadOnlyList<DashboardAppointmentFact> facts, DashboardPeriod period)
    {
        var days = facts.Select(f => (Day: period.DayOf(f.StartsAt), f.Status)).ToList();
        return [.. period.Buckets.Select(b =>
        {
            int Count(AppointmentStatus status) => days.Count(d => d.Day >= b.From && d.Day <= b.To && d.Status == status);
            return new DashboardAppointmentBucket(b, Count(AppointmentStatus.Finalizada), Count(AppointmentStatus.Confirmada), Count(AppointmentStatus.Cancelada));
        })];
    }

    /// <summary>El estado de cada vínculo al terminar cada tramo (en el que incluye hoy, el de ahora, igual que en el Inicio): vinculado entonces, con su
    /// último cambio registrado hasta ese momento (sin ninguno, pendiente) y caducada si su «válida hasta» ya había pasado ese día.</summary>
    public static IReadOnlyList<DashboardAuthorizationBucket> Authorizations(IReadOnlyList<DashboardLinkFact> links, DashboardPeriod period) =>
        [.. period.Buckets.Select(bucket =>
        {
            var current = bucket.To >= period.Today;
            var end = current ? DateTime.MaxValue : CenterTime.StartOfDayUtc(bucket.To.AddDays(1), period.Zone);
            var states = links
                .Where(link => link.LinkedAt < end && (current ? link.UnlinkedAt is null : link.UnlinkedAt is null || link.UnlinkedAt >= end))
                .Select(link => link.Changes.Where(c => c.RecordedAt < end).MaxBy(c => c.Number) is { } change
                    ? FamilyAuthorizationRules.Effective(change.Status, change.ValidUntil, bucket.To)
                    : FamilyAuthorizationStatus.Pendiente)
                .ToList();
            int Count(FamilyAuthorizationStatus status) => states.Count(s => s == status);
            return new DashboardAuthorizationBucket(bucket, Count(FamilyAuthorizationStatus.Activa), Count(FamilyAuthorizationStatus.Pendiente),
                Count(FamilyAuthorizationStatus.Caducada), Count(FamilyAuthorizationStatus.Suspendida));
        })];
}

/// <summary>
/// Lecturas del cuadro de mando de Administración, calculadas al abrirlo (no se guarda ninguna cifra). Cada una comprueba que quien mira es Administración
/// del centro. Residentes, plazas, altas, bajas y autorizaciones son de las unidades de su ámbito; las citas, de todo el centro, como su agenda. No leen
/// nombres de residentes ni de familiares, ni texto libre, ni nada clínico.
/// </summary>
public interface IAdministrationDashboardReader
{
    Task<DashboardOccupancy> ReadOccupancyAsync(AccountAdministrationAccess access, DateTime beforeUtc, CancellationToken ct = default);

    /// <summary>Las altas y bajas desde <paramref name="fromDate"/> (por su fecha de ingreso) o desde <paramref name="fromUtc"/> (por cuándo se registraron).</summary>
    Task<DashboardMovementFacts> ReadMovementsAsync(AccountAdministrationAccess access, DateOnly fromDate, DateTime fromUtc, CancellationToken ct = default);

    Task<IReadOnlyList<DashboardAppointmentFact>> ReadAppointmentsAsync(
        AccountAdministrationAccess access, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default);

    /// <summary>Los vínculos, vigentes o ya desvinculados, de los residentes que hoy están en el ámbito.</summary>
    Task<IReadOnlyList<DashboardLinkFact>> ReadAuthorizationsAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}
