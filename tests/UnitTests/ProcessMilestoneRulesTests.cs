using ResidApp.Application.Ports;
using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>DIR-11: plazos de los hitos del proceso (CJ, 2026-10-07) y su medición.</summary>
public class ProcessMilestoneRulesTests
{
    private static readonly DateTime Start = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_SonLosPlazosDeCJ()
    {
        var d = ProcessMilestoneRules.Defaults;

        Assert.Equal(ProcessMilestoneRules.All.Count, d.Count);
        Assert.Equal((TimeSpan.FromHours(8), TimeSpan.FromMinutes(30)), (d[ProcessMilestone.ValoracionEnfermeria].Normal!.Value, d[ProcessMilestone.ValoracionEnfermeria].Priority!.Value));
        Assert.Equal(TimeSpan.FromHours(24), d[ProcessMilestone.ValoracionMedica].Normal);
        Assert.Equal(TimeSpan.FromMinutes(30), d[ProcessMilestone.InformeDerivacion].Normal);
        Assert.Equal(TimeSpan.FromHours(1), d[ProcessMilestone.LlamadaFamilia].Normal);
        Assert.Equal(TimeSpan.FromMinutes(30), d[ProcessMilestone.LlamadaFamilia].For(priority: true));
        Assert.Equal(TimeSpan.FromHours(8), d[ProcessMilestone.RealizacionIndicacion].For(priority: false));
    }

    [Theory]
    [InlineData(60, true, MilestoneStatus.EnPlazo)]        // hecho justo en el plazo
    [InlineData(61, true, MilestoneStatus.HechoFueraDePlazo)]
    [InlineData(10, true, MilestoneStatus.EnPlazo)]
    [InlineData(10, false, MilestoneStatus.Pendiente)]     // sin hacer, a los 10 min de 1 h
    [InlineData(45, false, MilestoneStatus.APuntoDeVencer)] // quedan 15 min = el último cuarto
    [InlineData(44, false, MilestoneStatus.Pendiente)]
    [InlineData(60, false, MilestoneStatus.APuntoDeVencer)] // en el límite aún no ha vencido
    [InlineData(61, false, MilestoneStatus.FueraDePlazo)]
    public void Evaluate_ClasificaElHitoSegunElPlazo(int minutes, bool done, MilestoneStatus expected)
    {
        var term = TimeSpan.FromHours(1);
        var at = Start.AddMinutes(minutes);

        // Hecho: el instante de cumplimiento es at y "ahora" es mucho después; sin hacer: "ahora" es at.
        var status = done
            ? ProcessMilestoneRules.Evaluate(Start, at, term, Start.AddDays(3))
            : ProcessMilestoneRules.Evaluate(Start, null, term, at);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void IsOutOfTerm_SoloLosHechosTardeYLosSinHacerVencidos()
    {
        Assert.True(ProcessMilestoneRules.IsOutOfTerm(MilestoneStatus.HechoFueraDePlazo));
        Assert.True(ProcessMilestoneRules.IsOutOfTerm(MilestoneStatus.FueraDePlazo));
        Assert.False(ProcessMilestoneRules.IsOutOfTerm(MilestoneStatus.APuntoDeVencer));
        Assert.False(ProcessMilestoneRules.IsOutOfTerm(MilestoneStatus.EnPlazo));
        Assert.False(ProcessMilestoneRules.IsOutOfTerm(MilestoneStatus.Pendiente));
    }

    private static MilestoneFact Fact(
        ProcessMilestone milestone, UnitId unit, DateTime start, DateTime? end, bool priority = false, bool closed = false) =>
        new(milestone, Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente", unit, "Unidad", priority, start, end, closed, SystemProfile.Enfermeria);

    [Fact]
    public void Build_CuentaPorHitoYUnidad_UsaElPlazoPrioritario_YNoMideLoQueNoSePuedeHacerOQueNoTienePlazo()
    {
        var one = UnitId.From(Guid.NewGuid());
        var two = UnitId.From(Guid.NewGuid());
        var now = Start.AddHours(10);
        var deadlines = new Dictionary<ProcessMilestone, MilestoneDeadline>(ProcessMilestoneRules.Defaults)
        {
            [ProcessMilestone.LlamadaFamilia] = new(null, null),   // «no» se mide
        };
        var facts = new[]
        {
            // Valoración de Enfermería (8 h; 30 min si es prioritario), "ahora" = inicio + 10 h:
            Fact(ProcessMilestone.ValoracionEnfermeria, one, Start, Start.AddHours(1)),                    // en plazo
            Fact(ProcessMilestone.ValoracionEnfermeria, one, Start, Start.AddHours(1), priority: true),    // hecho a la hora: tarde para un prioritario
            Fact(ProcessMilestone.ValoracionEnfermeria, one, Start, null),                                 // sin hacer, vencido
            Fact(ProcessMilestone.ValoracionEnfermeria, one, Start, null, closed: true),                   // sin hacer pero cerrado: no se mide
            Fact(ProcessMilestone.ValoracionEnfermeria, two, Start.AddHours(3), null),                     // sin hacer, vence en 1 h: a punto
            Fact(ProcessMilestone.LlamadaFamilia, two, Start, null),                                       // sin plazo: no se mide
        };

        var report = ProcessQualityRules.Build(facts, deadlines, [(one, "Uno"), (two, "Dos"), (UnitId.From(Guid.NewGuid()), "Vacía")],
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), now);

        Assert.Equal(new MilestoneCount(4, 2, 1), report.Total[ProcessMilestone.ValoracionEnfermeria]);
        Assert.Equal(new MilestoneCount(3, 2, 0), report.Units[0].Counts[ProcessMilestone.ValoracionEnfermeria]);
        Assert.Equal(new MilestoneCount(1, 0, 1), report.Units[1].Counts[ProcessMilestone.ValoracionEnfermeria]);
        Assert.Equal(new MilestoneCount(0, 0, 0), report.Units[2].Counts[ProcessMilestone.ValoracionEnfermeria]);
        Assert.Equal(new MilestoneCount(0, 0, 0), report.Total[ProcessMilestone.LlamadaFamilia]);
        // Las excepciones son los fuera de plazo y el que está a punto de vencer, lo más reciente primero.
        Assert.Equal(3, report.Exceptions.Count);
        Assert.Equal(Start.AddHours(3), report.Exceptions[0].Start.UtcDateTime);
        Assert.Contains(report.Exceptions, e => e.Status == MilestoneStatus.APuntoDeVencer);
        Assert.Equal(ProcessMilestoneRules.All.Count, report.Total.Count);
    }
}

public class ProcessMilestoneDeadlineValidationTests
{
    [Fact]
    public void ValidateMinutes_AdmiteDeUnMinutoAUnaSemana_YVacioEsNoSeMide()
    {
        Assert.Equal(new MilestoneDeadline(TimeSpan.FromMinutes(1), TimeSpan.FromDays(7)), ProcessMilestoneRules.ValidateMinutes(1, 10080));
        Assert.Equal(new MilestoneDeadline(null, TimeSpan.FromMinutes(30)), ProcessMilestoneRules.ValidateMinutes(null, 30));
        Assert.Null(ProcessMilestoneRules.ValidateMinutes(null, null).For(priority: false));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(-5, 30)]
    [InlineData(30, 10081)]
    public void ValidateMinutes_RechazaPlazosImposibles(int normal, int priority)
    {
        var error = Assert.Throws<DomainValidationException>(() => ProcessMilestoneRules.ValidateMinutes(normal, priority));

        Assert.Equal("PROCESS_DEADLINES_INVALID", error.Message);
    }

    [Fact]
    public void ToMinutes_EsLaInversa()
    {
        Assert.Equal(480, ProcessMilestoneRules.ToMinutes(TimeSpan.FromHours(8)));
        Assert.Null(ProcessMilestoneRules.ToMinutes(null));
    }
}
