using ResidApp.Web.Models;

namespace ResidApp.FunctionalTests;

/// <summary>ADM-16: las fechas que genera el formulario de planificación y las que se saltan.</summary>
public class PlanFormModelTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static PlanFormModel Form(string? skip, params DayOfWeek[] days) => new()
    {
        Desde = Monday, Hasta = Monday.AddDays(13), Dias = days.ToList(), Saltar = skip,
    };

    [Fact]
    public void Dates_SinFechasASaltar_SonLasDelRangoEnLosDiasElegidos()
    {
        var dates = Form(null, DayOfWeek.Monday, DayOfWeek.Friday).Dates();

        Assert.Equal([Monday, Monday.AddDays(4), Monday.AddDays(7), Monday.AddDays(11)], dates);
    }

    [Fact]
    public void Dates_ConFechasASaltar_LasQuitaAcepta_AmbosFormatosYSeparadores_YIgnoraLasQueNoCaenEnElRango()
    {
        var form = Form("12/10/2026, 2026-10-16;\n2030-01-01   3/10/2026", DayOfWeek.Monday, DayOfWeek.Friday);

        var dates = form.Dates();
        var (skipped, invalid) = form.SkippedDates();

        Assert.Equal([Monday, Monday.AddDays(4)], dates);
        Assert.Equal(4, skipped.Count);
        Assert.Empty(invalid);
        Assert.Equal(2, form.SkippedInRange());
    }

    [Fact]
    public void SkippedDates_ConTextoQueNoEsUnaFecha_LoDevuelveComoInvalido()
    {
        var (skipped, invalid) = Form("mañana 2026-10-12 2026-13-45", DayOfWeek.Monday).SkippedDates();

        Assert.Equal([new DateOnly(2026, 10, 12)], skipped);
        Assert.Equal(["mañana", "2026-13-45"], invalid);
    }

    [Fact]
    public void Dates_SiSeSaltaTodo_NoQuedaNinguna()
    {
        Assert.Empty(Form("2026-10-05 2026-10-12", DayOfWeek.Monday).Dates());
    }
}
