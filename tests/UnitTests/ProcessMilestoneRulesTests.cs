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

    [Theory]
    [InlineData(89, MilestoneStatus.Pendiente)]
    [InlineData(90, MilestoneStatus.APuntoDeVencer)]   // guía de pruebas 2, prueba 13: con 2 minutos el aviso sale desde el minuto y medio
    [InlineData(120, MilestoneStatus.APuntoDeVencer)]  // en el límite aún no ha vencido
    [InlineData(121, MilestoneStatus.FueraDePlazo)]
    public void Evaluate_ConUnPlazoDeDosMinutos_AvisaDesdeLosNoventaSegundos(int seconds, MilestoneStatus expected)
    {
        var term = ProcessMilestoneRules.ValidateMinutes(2, null).Normal!.Value;

        Assert.Equal(expected, ProcessMilestoneRules.Evaluate(Start, null, term, Start.AddSeconds(seconds)));
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

/// <summary>CJ, 3.1 de medicina-alergias-y-plazos-cj: cada caso se mide con el plazo que valía cuando empezó.</summary>
public class ProcessMilestoneDeadlineAtTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static MilestoneDeadline D(int normalMinutes, int? priorityMinutes = 30) =>
        new(TimeSpan.FromMinutes(normalMinutes), priorityMinutes is { } p ? TimeSpan.FromMinutes(p) : null);

    private static MilestoneDeadlineChange Change(ProcessMilestone milestone, DateTimeOffset at, MilestoneDeadline previous, MilestoneDeadline @new) =>
        new(milestone, previous, @new, at);

    [Fact]
    public void SinCambios_ValeElPlazoDeHoy()
    {
        Assert.Equal(D(60), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0, D(60), []));
    }

    [Fact]
    public void UnCaso_ConUnCambioDespues_UsaElPlazoDeAntesDelCambio_YUnoPosterior_ElNuevo()
    {
        var changes = new[] { Change(ProcessMilestone.ValoracionMedica, T0, D(1440), D(720)) };

        Assert.Equal(D(1440), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddMinutes(-1), D(720), changes));
        Assert.Equal(D(720), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddMinutes(1), D(720), changes));
    }

    [Fact]
    public void UnCambioHechoJustoCuandoEmpezoElCaso_YaValeParaEseCaso()
    {
        var changes = new[] { Change(ProcessMilestone.ValoracionMedica, T0, D(1440), D(720)) };

        Assert.Equal(D(720), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0, D(720), changes));
    }

    [Fact]
    public void ConVariosCambios_MandaElUltimoAnteriorAlCaso_AunqueElDeHoyEsteMasAdelante()
    {
        var changes = new[]
        {
            Change(ProcessMilestone.ValoracionMedica, T0, D(1440), D(720)),
            Change(ProcessMilestone.ValoracionMedica, T0.AddDays(10), D(720), D(360)),
            Change(ProcessMilestone.ValoracionMedica, T0.AddDays(20), D(360), D(120)),
        };

        Assert.Equal(D(1440), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddDays(-5), D(120), changes));
        Assert.Equal(D(720), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddDays(5), D(120), changes));
        Assert.Equal(D(360), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddDays(15), D(120), changes));
        Assert.Equal(D(120), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddDays(25), D(120), changes));
    }

    [Fact]
    public void LosCambiosDeOtroHito_NoCuentan()
    {
        var changes = new[] { Change(ProcessMilestone.LlamadaFamilia, T0, D(60), D(10)) };

        Assert.Equal(D(1440), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddDays(-1), D(1440), changes));
        Assert.Equal(D(1440), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddDays(1), D(1440), changes));
    }

    [Fact]
    public void ConDosCambiosEnElMismoInstante_MandaElUltimoDeLaLista()
    {
        var changes = new[]
        {
            Change(ProcessMilestone.ValoracionMedica, T0, D(1440), D(720)),
            Change(ProcessMilestone.ValoracionMedica, T0, D(720), D(360)),
        };

        Assert.Equal(D(360), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddMinutes(1), D(360), changes));
        Assert.Equal(D(1440), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.ValoracionMedica, T0.AddMinutes(-1), D(360), changes));
    }

    [Fact]
    public void UnPlazoQueDejaDeMedirseYVuelve_NoMideLosCasosDeEntremedias_YSiLosDeAntesYDespues()
    {
        var off = new MilestoneDeadline(null, null);
        var changes = new[]
        {
            Change(ProcessMilestone.LlamadaFamilia, T0.AddDays(1), D(60), off),
            Change(ProcessMilestone.LlamadaFamilia, T0.AddDays(3), off, D(45)),
        };

        Assert.Equal(D(60), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.LlamadaFamilia, T0, D(45), changes));
        Assert.Equal(off, ProcessMilestoneRules.DeadlineAt(ProcessMilestone.LlamadaFamilia, T0.AddDays(2), D(45), changes));
        Assert.Equal(D(45), ProcessMilestoneRules.DeadlineAt(ProcessMilestone.LlamadaFamilia, T0.AddDays(4), D(45), changes));
    }

    [Fact]
    public void Build_UnHitoSinHacerSeMideConElPlazoDeCuandoEmpezo_AunqueHoyValgaOtro()
    {
        var unit = UnitId.From(Guid.NewGuid());
        var start = T0.UtcDateTime;
        var now = start.AddHours(3);
        MilestoneFact Pending(ProcessMilestone milestone, bool priority = false) =>
            new(milestone, Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "", unit, "Uno", priority, start, null, false, SystemProfile.Enfermeria);
        // Valoración de Enfermería: de 8 h a 1 h (hoy ya vencería) y Recepción de la transferencia: de 1 h a 8 h (hoy no vencería), ambas
        // cambiadas un día después de empezar el caso.
        var changes = new[]
        {
            Change(ProcessMilestone.ValoracionEnfermeria, T0.AddDays(1), D(480), D(60)),
            Change(ProcessMilestone.RecepcionTransferencia, T0.AddDays(1), D(60), D(480)),
        };
        var today = new Dictionary<ProcessMilestone, MilestoneDeadline>(ProcessMilestoneRules.Defaults)
        {
            [ProcessMilestone.ValoracionEnfermeria] = D(60),
            [ProcessMilestone.RecepcionTransferencia] = D(480),
        };
        var facts = new[] { Pending(ProcessMilestone.ValoracionEnfermeria), Pending(ProcessMilestone.RecepcionTransferencia) };

        var perCase = ProcessQualityRules.Build(facts, today, [(unit, "Uno")], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), now, changes);
        var todayOnly = ProcessQualityRules.Build(facts, today, [(unit, "Uno")], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), now);

        Assert.Equal(new MilestoneCount(1, 0, 0), perCase.Total[ProcessMilestone.ValoracionEnfermeria]);          // con 8 h, a las 3 h aún en plazo
        Assert.Equal(new MilestoneCount(1, 1, 0), todayOnly.Total[ProcessMilestone.ValoracionEnfermeria]);
        Assert.Equal(new MilestoneCount(1, 1, 0), perCase.Total[ProcessMilestone.RecepcionTransferencia]);        // con 1 h, a las 3 h vencido
        Assert.Equal(new MilestoneCount(1, 0, 0), todayOnly.Total[ProcessMilestone.RecepcionTransferencia]);
    }

    [Fact]
    public void Build_ElPlazoPrioritarioTambienSeMideConElDeCuandoEmpezo()
    {
        var unit = UnitId.From(Guid.NewGuid());
        var start = T0.UtcDateTime;
        // El plazo prioritario de la valoración médica pasó de 30 min a 10 h un día después; el normal no cambió.
        var changes = new[] { Change(ProcessMilestone.ValoracionMedica, T0.AddDays(1), D(1440, 30), D(1440, 600)) };
        var today = new Dictionary<ProcessMilestone, MilestoneDeadline>(ProcessMilestoneRules.Defaults) { [ProcessMilestone.ValoracionMedica] = D(1440, 600) };
        var facts = new[]
        {
            new MilestoneFact(ProcessMilestone.ValoracionMedica, Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "", unit, "Uno", true,
                start, start.AddHours(2), false, SystemProfile.Medicina),
        };

        var perCase = ProcessQualityRules.Build(facts, today, [(unit, "Uno")], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), start.AddDays(2), changes);

        Assert.Equal(new MilestoneCount(1, 1, 0), perCase.Total[ProcessMilestone.ValoracionMedica]);   // hecho a las 2 h con 30 min: tarde
    }

    [Fact]
    public void Build_MideCadaCasoConSuPlazo_YUnPlazoQueDejoDeMedirseNoQuitaLosCasosAnteriores()
    {
        var unit = UnitId.From(Guid.NewGuid());
        var start = T0.UtcDateTime;
        // El plazo de la valoración médica baja de 24 h a 1 h dos días después; el de la llamada a la familia deja de medirse ese mismo día.
        var changes = new[]
        {
            Change(ProcessMilestone.ValoracionMedica, T0.AddDays(2), ProcessMilestoneRules.Defaults[ProcessMilestone.ValoracionMedica], D(60)),
            Change(ProcessMilestone.LlamadaFamilia, T0.AddDays(2), ProcessMilestoneRules.Defaults[ProcessMilestone.LlamadaFamilia], new MilestoneDeadline(null, null)),
        };
        var today = new Dictionary<ProcessMilestone, MilestoneDeadline>(ProcessMilestoneRules.Defaults)
        {
            [ProcessMilestone.ValoracionMedica] = D(60),
            [ProcessMilestone.LlamadaFamilia] = new(null, null),
        };
        MilestoneFact Fact(ProcessMilestone milestone, DateTime at, DateTime? end) =>
            new(milestone, Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "", unit, "Uno", false, at, end, false, SystemProfile.Medicina);
        var facts = new[]
        {
            // Empezó antes del cambio y se hizo a las 5 h: con 24 h estaba en plazo (con el de hoy, 1 h, saldría fuera).
            Fact(ProcessMilestone.ValoracionMedica, start, start.AddHours(5)),
            // Empezó después del cambio y se hizo a las 5 h: con 1 h, fuera de plazo.
            Fact(ProcessMilestone.ValoracionMedica, start.AddDays(3), start.AddDays(3).AddHours(5)),
            // Llamada anterior al cambio: sigue midiéndose; la posterior ya no.
            Fact(ProcessMilestone.LlamadaFamilia, start, start.AddMinutes(10)),
            Fact(ProcessMilestone.LlamadaFamilia, start.AddDays(3), start.AddDays(3).AddMinutes(10)),
        };
        var now = start.AddDays(5);
        var units = new[] { (unit, "Uno") };
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 8);

        var perCase = ProcessQualityRules.Build(facts, today, units, from, to, now, changes);
        var todayOnly = ProcessQualityRules.Build(facts, today, units, from, to, now);

        Assert.Equal(new MilestoneCount(2, 1, 0), perCase.Total[ProcessMilestone.ValoracionMedica]);
        Assert.Equal(new MilestoneCount(2, 2, 0), todayOnly.Total[ProcessMilestone.ValoracionMedica]);
        Assert.Equal(new MilestoneCount(1, 0, 0), perCase.Total[ProcessMilestone.LlamadaFamilia]);
        Assert.Equal(new MilestoneCount(0, 0, 0), todayOnly.Total[ProcessMilestone.LlamadaFamilia]);
        Assert.Equal(today, perCase.Deadlines);   // la tabla enseña los plazos de hoy
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
