using System.Globalization;
using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Residents;

namespace ResidApp.Web.Models;

/// <summary>Una cifra grande de un cuadro de mando. Con <see cref="Value"/> null no se pudo calcular: se dice, nunca se pinta un cero.</summary>
public sealed record StatCard(string Title, string? Value, string Label, IReadOnlyList<string> Details)
{
    public static StatCard Missing(string title) => new(title, null, string.Empty, []);
}

/// <summary>Una fila de un gráfico de barras: sus valores, uno por serie, y el texto que los dice. Scale es el 100 % de la fila si no es el del gráfico
/// (las plazas de una unidad); 0, la fila no lleva barra.</summary>
public sealed record ChartRow(string Label, IReadOnlyList<int> Values, string Text, int? Scale = null, bool IsTotal = false);

public sealed record ChartTableRow(string Label, IReadOnlyList<string> Cells);

/// <summary>La tabla con los mismos datos que su gráfico.</summary>
public sealed record ChartTable(string Caption, string CategoryHeader, IReadOnlyList<string> Headers, IReadOnlyList<ChartTableRow> Rows);

/// <summary>Un trozo de barra, en porcentajes de la fila. Lane es la línea que ocupa (las series agrupadas van una debajo de otra).</summary>
public sealed record BarSegment(int Series, double Left, double Width, int Lane);

/// <summary>Un gráfico de barras horizontales que dibuja el servidor (_GraficoBarras). Apiladas, las series se suman en una barra; si no, van agrupadas.</summary>
public sealed record BarChart(IReadOnlyList<string> Series, bool Stacked, IReadOnlyList<ChartRow> Rows, ChartTable Table)
{
    public const int LaneHeight = 12;

    public bool Empty => Rows.All(r => r.Values.Sum() == 0 && r.Scale is null);

    public int Lanes => Stacked ? 1 : Series.Count;

    private int CommonScale => Rows.Count == 0 ? 0 : Rows.Max(r => Stacked ? r.Values.Sum() : r.Values.Max());

    /// <summary>La escala de la fila; 0 si no lleva barra.</summary>
    public int ScaleOf(ChartRow row) => row.Scale ?? CommonScale;

    public IReadOnlyList<BarSegment> Segments(ChartRow row)
    {
        var scale = ScaleOf(row);
        var segments = new List<BarSegment>();
        var left = 0.0;
        for (var series = 0; series < row.Values.Count && scale > 0; series++)
        {
            var width = Math.Min(100.0, row.Values[series] * 100.0 / scale);
            if (width <= 0)
            {
                continue;
            }

            segments.Add(new BarSegment(series, Stacked ? left : 0, width, Stacked ? 0 : series));
            left += width;
        }

        return segments;
    }

    /// <summary>Un porcentaje para un atributo SVG: siempre con punto decimal, sea cual sea el idioma.</summary>
    public static string Svg(double percent) => percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

/// <summary>La rosquilla de la ocupación actual (_GraficoRosquilla).</summary>
public sealed record DonutChart(int Occupied, int Places)
{
    public int Percent => DashboardOccupancy.Percent(Occupied, Places) ?? 0;

    public string Text => $"{Percent} % ({Occupied} de {Places} {(Places == 1 ? "plaza" : "plazas")})";
}

/// <summary>El cuadro de mando de Administración. Un bloque null no se pudo leer. Un Centro de Día no tiene habitaciones ni plazas (ADR 0017): no se le
/// habla de ocupación.</summary>
public sealed record AdministrationPanelViewModel(
    DashboardPeriod Period, DateTime GeneratedAt, DashboardOccupancy? Occupancy, DashboardMovements? Movements,
    IReadOnlyList<DashboardAppointmentBucket>? Appointments, IReadOnlyList<DashboardAuthorizationBucket>? Authorizations,
    AdministrationOverview? Overview, AdministrationCommunicationsSummary? Communications, bool DayCenter = false);

/// <summary>Cómo se escribe el cuadro de mando: las cifras con su variación y los datos de cada gráfico.</summary>
public static class AdministrationPanelDisplay
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    private static readonly ResidentDischargeReason[] Reasons = Enum.GetValues<ResidentDischargeReason>();

    public static DashboardRange Range(string? code) => code switch
    {
        "7d" => DashboardRange.SieteDias,
        "12m" => DashboardRange.DoceMeses,
        _ => DashboardRange.TreintaDias,
    };

    public static string Code(DashboardRange range) => range switch
    {
        DashboardRange.SieteDias => "7d",
        DashboardRange.DoceMeses => "12m",
        _ => "30d",
    };

    public static string Name(DashboardRange range) => range switch
    {
        DashboardRange.SieteDias => "7 días",
        DashboardRange.DoceMeses => "12 meses",
        _ => "30 días",
    };

    public static string BucketHeader(DashboardRange range) => range switch
    {
        DashboardRange.SieteDias => "Día",
        DashboardRange.DoceMeses => "Mes",
        _ => "Semana",
    };

    public static string BucketLabel(DashboardRange range, DashboardBucket bucket) => range switch
    {
        DashboardRange.SieteDias => bucket.From.ToString("ddd dd/MM", Spanish),
        DashboardRange.DoceMeses => bucket.From.ToString("MMMM yyyy", Spanish),
        _ => bucket.From == bucket.To ? bucket.From.ToString("dd/MM", Spanish)
            : $"{bucket.From.ToString("dd/MM", Spanish)} a {bucket.To.ToString("dd/MM", Spanish)}",
    };

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Spanish);

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;

    /// <summary>Si al empezar el periodo no había ningún residente registrado (un centro que empieza o importa su censo), no se compara: la diferencia
    /// diría que han entrado todos, aunque las altas, que cuentan por su fecha de ingreso, digan que no (decisión de Orlando, 2026-11-27).</summary>
    public const string NothingToCompare = "Sin datos anteriores para comparar";

    private static string Variation(int now, int before, DateOnly since) => (now - before) switch
    {
        _ when before == 0 => NothingToCompare,
        0 => $"Igual que al empezar el {Date(since)}",
        > 0 and var up => $"{up} más que al empezar el {Date(since)}",
        var down => $"{-down} menos que al empezar el {Date(since)}",
    };

    public static IReadOnlyList<StatCard> Cards(AdministrationPanelViewModel model)
    {
        List<StatCard> cards = [ResidentsCard(model), AdmissionsCard(model), DischargesCard(model), AppointmentsCard(model), CommunicationsCard(model)];
        if (!model.DayCenter)
        {
            cards.Insert(1, OccupancyCard(model));
        }

        return cards;
    }

    /// <summary>Las bajas por categoría, solo las que tienen alguna: «Fallecimiento: 1 · Otro motivo: 2».</summary>
    private static string Breakdown(DashboardReasonCounts counts) =>
        string.Join(" · ", Reasons.Where(r => counts.Of(r) > 0).Select(r => $"{EnumDisplay.Label(r)}: {counts.Of(r)}"));

    private static StatCard ResidentsCard(AdministrationPanelViewModel model)
    {
        const string title = "Residentes activos";
        return model.Occupancy is not { } occupancy ? StatCard.Missing(title)
            : new(title, occupancy.Residents.ToString(Spanish), Plural(occupancy.Residents, "residente en tu ámbito", "residentes en tu ámbito"),
                [Variation(occupancy.Residents, occupancy.ResidentsBefore, model.Period.From)]);
    }

    private static StatCard OccupancyCard(AdministrationPanelViewModel model)
    {
        const string title = "Ocupación";
        if (model.Occupancy is not { } occupancy)
        {
            return StatCard.Missing(title);
        }

        if (DashboardOccupancy.Percent(occupancy.Occupied, occupancy.Places) is not { } percent)
        {
            return new(title, "Sin plazas", "No hay plazas definidas en tus unidades", []);
        }

        var points = percent - DashboardOccupancy.Percent(occupancy.OccupiedBefore, occupancy.Places)!.Value;
        var since = Date(model.Period.From);
        var details = new List<string>
        {
            occupancy.ResidentsBefore == 0 ? NothingToCompare
                : points == 0 ? $"Igual que al empezar el {since}, con las plazas de hoy"
                : $"{Math.Abs(points)} {Plural(Math.Abs(points), "punto", "puntos")} {(points > 0 ? "más" : "menos")} que al empezar el {since}, con las plazas de hoy",
        };
        var unplaced = occupancy.Units.Where(u => u.Places > 0).Sum(u => u.Residents - u.Occupied);
        if (unplaced > 0)
        {
            details.Add($"{unplaced} {Plural(unplaced, "residente sin plaza asignada", "residentes sin plaza asignada")}");
        }

        var outside = occupancy.Units.Where(u => u.Places == 0).Sum(u => u.Residents);
        if (outside > 0)
        {
            details.Add($"{outside} {Plural(outside, "residente en una unidad sin plazas definidas", "residentes en unidades sin plazas definidas")}, fuera del porcentaje");
        }

        return new(title, $"{percent} %", $"{occupancy.Occupied} de {occupancy.Places} {Plural(occupancy.Places, "plaza ocupada", "plazas ocupadas")}", details);
    }

    private static StatCard AdmissionsCard(AdministrationPanelViewModel model)
    {
        const string title = "Altas";
        return model.Movements is not { } movements ? StatCard.Missing(title)
            : new(title, movements.Admissions.ToString(Spanish), Plural(movements.Admissions, "alta en el periodo", "altas en el periodo"),
                [$"Hoy: {movements.AdmissionsToday}", $"Periodo anterior: {movements.AdmissionsPrevious}"]);
    }

    private static StatCard DischargesCard(AdministrationPanelViewModel model)
    {
        const string title = "Bajas";
        if (model.Movements is not { } movements)
        {
            return StatCard.Missing(title);
        }

        var today = movements.DischargesToday;
        return new(title, movements.Discharges.ToString(Spanish), Plural(movements.Discharges, "baja en el periodo", "bajas en el periodo"),
            [today.Total == 0 ? "Hoy: 0" : $"Hoy: {today.Total} ({Breakdown(today)})", $"Periodo anterior: {movements.DischargesPrevious}"]);
    }

    private static StatCard AppointmentsCard(AdministrationPanelViewModel model)
    {
        const string title = "Citas con la familia";
        return model.Overview is not { } overview ? StatCard.Missing(title)
            : new(title, overview.AppointmentsToday.ToString(Spanish), Plural(overview.AppointmentsToday, "confirmada hoy", "confirmadas hoy"),
                [$"En los próximos {AdministrationOverviewRules.ComingAppointmentDays} días: {overview.AppointmentsComing}"]);
    }

    private static StatCard CommunicationsCard(AdministrationPanelViewModel model)
    {
        const string title = "Comunicados a la familia";
        return model.Communications is not { } communications ? StatCard.Missing(title)
            : new(title, communications.Scheduled.ToString(Spanish), Plural(communications.Scheduled, "programado", "programados"),
                [$"Publicados en los últimos {AdministrationCommunicationsSummary.RecentDays} días: {communications.PublishedRecently}"]);
    }

    /// <summary>Por unidad y, si las unidades tienen edificio, con el total de las del ámbito en cada uno. Siempre en orden de nombre.</summary>
    public static BarChart OccupancyByUnit(DashboardOccupancy occupancy)
    {
        ChartRow Row(string label, int residents, int places, int occupied, bool isTotal = false) => places == 0
            ? new(label, [0], $"{residents} {Plural(residents, "residente", "residentes")} (sin plazas definidas)", 0, isTotal)
            : new(label, [occupied],
                $"{occupied} de {places} {Plural(places, "plaza", "plazas")} ({DashboardOccupancy.Percent(occupied, places)} %) · {residents} {Plural(residents, "residente", "residentes")}",
                places, isTotal);
        ChartTableRow TableRow(string label, int residents, int places, int occupied) => new(label,
        [
            residents.ToString(Spanish),
            places == 0 ? "Sin definir" : places.ToString(Spanish),
            places == 0 ? "No consta" : occupied.ToString(Spanish),
            DashboardOccupancy.Percent(occupied, places) is { } percent ? $"{percent} %" : "No consta",
        ]);

        var rows = new List<ChartRow>();
        var table = new List<ChartTableRow>();
        var buildings = occupancy.ActiveUnits.GroupBy(u => u.BuildingName)
            .OrderBy(g => g.Key is null).ThenBy(g => g.Key, StringComparer.Create(Spanish, false)).ToList();
        foreach (var building in buildings)
        {
            if (building.Key is { } name)
            {
                var places = building.Sum(u => u.Places);
                var residents = building.Sum(u => u.Residents);
                var occupied = building.Sum(u => u.Occupied);
                // El total es solo de las unidades del ámbito: puede no ser el edificio entero.
                var label = $"{name}: total de tus unidades";
                rows.Add(Row(label, residents, places, occupied, isTotal: true));
                table.Add(TableRow(label, residents, places, occupied));
            }

            foreach (var unit in building.OrderBy(u => u.UnitName, StringComparer.Create(Spanish, false)))
            {
                rows.Add(Row(unit.UnitName, unit.Residents, unit.Places, unit.Occupied));
                table.Add(TableRow(unit.UnitName, unit.Residents, unit.Places, unit.Occupied));
            }
        }

        return new(["Plazas ocupadas"], false, rows,
            new("Ocupación por unidad y edificio", "Unidad", ["Residentes", "Plazas", "Ocupadas", "Ocupación"], table));
    }

    public static BarChart Movements(DashboardPeriod period, DashboardMovements movements)
    {
        // El motivo va también en el texto de la fila: la tabla no se ve en el móvil ni en el papel.
        var rows = movements.Buckets.Select(b => new ChartRow(
            BucketLabel(period.Range, b.Bucket), [b.Admissions, b.Discharges.Total],
            $"Altas {b.Admissions} · Bajas {b.Discharges.Total}" + (b.Discharges.Total > 0 ? $" ({Breakdown(b.Discharges)})" : ""))).ToList();
        var table = movements.Buckets.Select(b => new ChartTableRow(BucketLabel(period.Range, b.Bucket),
            [b.Admissions.ToString(Spanish), b.Discharges.Total.ToString(Spanish), .. Reasons.Select(r => b.Discharges.Of(r).ToString(Spanish))])).ToList();
        return new(["Altas", "Bajas"], false, rows,
            new("Altas y bajas", BucketHeader(period.Range), ["Altas", "Bajas", .. Reasons.Select(r => $"Bajas: {EnumDisplay.Label(r)}")], table));
    }

    public static BarChart Appointments(DashboardPeriod period, IReadOnlyList<DashboardAppointmentBucket> buckets)
    {
        string[] series = ["Finalizadas", "Confirmadas", "Canceladas"];
        return Stacked(period, "Citas con la familia", series,
            buckets.Select(b => (b.Bucket, new[] { b.Finished, b.Confirmed, b.Cancelled })));
    }

    public static BarChart Authorizations(DashboardPeriod period, IReadOnlyList<DashboardAuthorizationBucket> buckets)
    {
        string[] series = ["Activas", "Pendientes", "Caducadas", "Suspendidas"];
        return Stacked(period, "Autorizaciones de familiares", series,
            buckets.Select(b => (b.Bucket, new[] { b.Active, b.Pending, b.Expired, b.Suspended })));
    }

    private static BarChart Stacked(DashboardPeriod period, string caption, string[] series, IEnumerable<(DashboardBucket Bucket, int[] Values)> buckets)
    {
        var list = buckets.ToList();
        var rows = list.Select(b => new ChartRow(BucketLabel(period.Range, b.Bucket), b.Values,
            string.Join(" · ", series.Select((name, i) => $"{name} {b.Values[i]}")))).ToList();
        var table = list.Select(b => new ChartTableRow(BucketLabel(period.Range, b.Bucket), [.. b.Values.Select(v => v.ToString(Spanish))])).ToList();
        return new(series, true, rows, new(caption, BucketHeader(period.Range), series, table));
    }
}
