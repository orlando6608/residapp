using ResidApp.Application.Ports;
using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Medicina;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>DIR-08 a DIR-10: agregación de los indicadores de Dirección por unidad, en total y mes a mes.</summary>
public class SupervisionIndicatorRulesTests
{
    private static readonly UnitId PlantaUno = UnitId.From(Guid.NewGuid());
    private static readonly UnitId PlantaDos = UnitId.From(Guid.NewGuid());
    private static readonly SupervisionScopeInfo Scope = new("Centro", [(PlantaUno, "Planta 1"), (PlantaDos, "Planta 2")], false, []);

    private static IndicatorEpisodeFact Episode(
        UnitId unit, DateTime at, ClinicalEventOrigin origin = ClinicalEventOrigin.CambioAuxiliar,
        DailyChangeClassification classification = DailyChangeClassification.Ordinario,
        bool escalated = false, bool protocol = false, bool referred = false) =>
        new(unit, origin, classification, at, escalated, protocol, referred);

    private static SupervisionIndicatorFacts Facts(
        IReadOnlyList<IndicatorEpisodeFact>? episodes = null, IReadOnlyList<IndicatorClosureFact>? closures = null,
        IReadOnlyList<IndicatorIndicationFact>? indications = null, IReadOnlyList<IndicatorTransferFact>? transfers = null,
        IReadOnlyList<IndicatorFollowUpFact>? followUps = null) =>
        new(episodes ?? [], closures ?? [], indications ?? [], transfers ?? [], followUps ?? []);

    [Fact]
    public void Periodo_IncluyeElDiaHastaEntero_YDejaFueraLoDeAntesYDespues()
    {
        var facts = Facts(episodes:
        [
            Episode(PlantaUno, new DateTime(2026, 8, 31, 23, 59, 0)),
            Episode(PlantaUno, new DateTime(2026, 9, 1, 0, 0, 0)),
            Episode(PlantaUno, new DateTime(2026, 9, 30, 23, 59, 59)),
            Episode(PlantaUno, new DateTime(2026, 10, 1, 0, 0, 0)),
        ]);

        var result = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), TimeZoneInfo.Utc);

        Assert.Equal(2, result.Total.Registered);
    }

    [Fact]
    public void Unidades_SinDatosSalenACero_YElTotalEsLaSumaDeLasUnidades()
    {
        var facts = Facts(
            episodes:
            [
                Episode(PlantaUno, new DateTime(2026, 9, 2), ClinicalEventOrigin.CambioAuxiliar, DailyChangeClassification.Prioritario, escalated: true),
                Episode(PlantaUno, new DateTime(2026, 9, 3), ClinicalEventOrigin.EventoEnfermeria, protocol: true, referred: true),
                Episode(PlantaUno, new DateTime(2026, 9, 4), ClinicalEventOrigin.EventoMedicina),
            ],
            closures: [new(PlantaUno, new DateTime(2026, 9, 5)), new(PlantaUno, new DateTime(2026, 8, 5))]);

        var result = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), TimeZoneInfo.Utc);

        Assert.Equal(["Planta 1", "Planta 2"], result.Units.Select(u => u.UnitName));
        var uno = result.Units[0].Counts;
        Assert.Equal((3, 1, 1, 1, 1, 1), (uno.Registered, uno.FromAuxiliar, uno.FromNursing, uno.FromMedicine, uno.Priority, uno.Closed));
        Assert.Equal((1, 1, 1), (uno.Escalated, uno.UrgentProtocols, uno.Referrals));
        Assert.Equal(0, result.Units[1].Counts.Registered);
        Assert.Equal(uno, result.Total);
    }

    [Fact]
    public void Indicaciones_YTransferencias_SeCuentanSobreSuDenominador()
    {
        var at = new DateTime(2026, 9, 10);
        var facts = Facts(
            indications:
            [
                new(PlantaUno, at, MedicalIndicationStatus.PendienteLectura),
                new(PlantaUno, at, MedicalIndicationStatus.Leida),
                new(PlantaUno, at, MedicalIndicationStatus.Realizada),
                new(PlantaDos, at, MedicalIndicationStatus.NoRealizada),
            ],
            transfers:
            [
                new(PlantaUno, SystemProfile.Enfermeria, at, true),
                new(PlantaUno, SystemProfile.Enfermeria, at, false),
                new(PlantaDos, SystemProfile.Medicina, at, false),
            ]);

        var total = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), TimeZoneInfo.Utc).Total;

        Assert.Equal((4, 3, 1, 1, 2), (total.IndicationsIssued, total.IndicationsRead, total.IndicationsDone, total.IndicationsNotDone, total.IndicationsUnresolved));
        Assert.Equal((2, 1, 1, 0), (total.NursingTransfers, total.NursingTransfersReceived, total.MedicalTransfers, total.MedicalTransfersReceived));
    }

    [Fact]
    public void Meses_SeRecortanAlPeriodo_YCadaHechoCaeEnSuMes()
    {
        var facts = Facts(episodes:
        [
            Episode(PlantaUno, new DateTime(2026, 8, 20)),
            Episode(PlantaDos, new DateTime(2026, 9, 15)),
            Episode(PlantaUno, new DateTime(2026, 10, 1)),
        ]);

        var result = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2026, 8, 16), new DateOnly(2026, 10, 1), TimeZoneInfo.Utc);

        Assert.Equal(
            [
                (new DateOnly(2026, 8, 16), new DateOnly(2026, 8, 31), 1),
                (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 1),
                (new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), 1),
            ],
            result.Months.Select(m => (m.From, m.To, m.Counts.Registered)));
        Assert.Equal(3, result.Total.Registered);
    }

    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void HechoEntreLas0YLas2DeEspaña_CuentaEnSuDiaYSuMesLocales_NoEnElDiaUtc()
    {
        // 30/09 22:30 UTC = 01/10 00:30 en Madrid (verano); 31/12 23:30 UTC = 01/01 00:30 (invierno).
        var facts = Facts(
            episodes: [Episode(PlantaUno, new DateTime(2026, 9, 30, 22, 30, 0)), Episode(PlantaUno, new DateTime(2026, 12, 31, 23, 30, 0))],
            closures: [new(PlantaUno, new DateTime(2026, 9, 30, 21, 59, 0))]);

        var october = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), Madrid);
        var september = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), Madrid);
        var january = SupervisionIndicatorRules.Aggregate(facts, Scope, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), Madrid);

        Assert.Equal((1, 0), (october.Total.Registered, october.Total.Closed));
        Assert.Equal((0, 1), (september.Total.Registered, september.Total.Closed));
        Assert.Equal(1, january.Total.Registered);
    }

    private static IndicatorFollowUpFact FollowUp(
        DateTime start, DateTime? end, DateOnly? initialDue, params IndicatorReschedule[] reschedules) =>
        new(PlantaUno, start, end, initialDue, reschedules);

    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    [Theory]
    [InlineData(10, null, 1, 30, true)]    // vence el 10 y sigue abierto: vencido del 11 en adelante
    [InlineData(10, 10, 1, 30, false)]     // se cierra el mismo día en que vence: ese día todavía no estaba vencido
    [InlineData(10, 11, 1, 30, true)]      // se cierra el día siguiente: el día del cierre ya estaba vencido
    [InlineData(10, null, 1, 10, false)]   // el periodo acaba el día de la fecha prevista
    [InlineData(10, null, 11, 30, true)]
    [InlineData(40, null, 1, 30, false)]   // vence después del periodo (el 10 de octubre)
    public void Vencido_SigueLaFechaPrevistaYElCierre(int dueDay, int? endDay, int fromDay, int toDay, bool expected)
    {
        DateOnly Day(int d) => new DateOnly(2026, 9, 1).AddDays(d - 1);
        var followUp = FollowUp(new DateTime(2026, 9, 1, 8, 0, 0), endDay is { } e ? Day(e).ToDateTime(new TimeOnly(12, 0)) : null, Day(dueDay));

        Assert.Equal(expected, SupervisionIndicatorRules.WasOverdue(followUp, Day(fromDay), Day(toDay), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Vencido_ConReprogramaciones_UsaElPlanVigenteAlEmpezarCadaDia()
    {
        // Vence el 10, se reprograma el 15 al 20 y el 25 al 28.
        var followUp = FollowUp(new DateTime(2026, 9, 1), null, new DateOnly(2026, 9, 10),
            new IndicatorReschedule(new DateTime(2026, 9, 15, 9, 0, 0), new DateOnly(2026, 9, 20)),
            new IndicatorReschedule(new DateTime(2026, 9, 25, 9, 0, 0), new DateOnly(2026, 9, 28)));
        bool Overdue(int from, int to) =>
            SupervisionIndicatorRules.WasOverdue(followUp, new DateOnly(2026, 9, from), new DateOnly(2026, 9, to), TimeZoneInfo.Utc);

        Assert.True(Overdue(1, 14));    // antes de reprogramar, vencido del 11 al 15 (el 15 empieza con el plan inicial)
        Assert.False(Overdue(1, 10));
        Assert.False(Overdue(16, 20));  // con el plan al 20 no vence hasta el 21
        Assert.True(Overdue(21, 25));   // vencido del 21 al 25 (el 25 empieza con el plan al 20)
        Assert.False(Overdue(26, 28));  // con el plan al 28 no vence hasta el 29
        Assert.True(Overdue(29, 30));
    }

    [Fact]
    public void Vencido_ReprogramarElDiaSiguienteAlVencimientoYaCuentaComoVencido()
    {
        var followUp = FollowUp(new DateTime(2026, 9, 1), null, new DateOnly(2026, 9, 10),
            new IndicatorReschedule(new DateTime(2026, 9, 11, 12, 0, 0), new DateOnly(2026, 9, 30)));

        Assert.True(SupervisionIndicatorRules.WasOverdue(followUp, new DateOnly(2026, 9, 11), new DateOnly(2026, 9, 11), TimeZoneInfo.Utc));
        Assert.False(SupervisionIndicatorRules.WasOverdue(followUp, new DateOnly(2026, 9, 12), Sep30, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Vencido_SinFechaNoVenceNunca_YUnaReprogramacionSinFechaLoDejaDeVencer()
    {
        var withoutDate = FollowUp(new DateTime(2026, 9, 1), null, null);
        var droppedDate = FollowUp(new DateTime(2026, 9, 1), null, new DateOnly(2026, 9, 1),
            new IndicatorReschedule(new DateTime(2026, 9, 5, 9, 0, 0), null));

        Assert.False(SupervisionIndicatorRules.WasOverdue(withoutDate, Sep1, Sep30, TimeZoneInfo.Utc));
        Assert.True(SupervisionIndicatorRules.WasOverdue(droppedDate, Sep1, new DateOnly(2026, 9, 5), TimeZoneInfo.Utc));
        Assert.False(SupervisionIndicatorRules.WasOverdue(droppedDate, new DateOnly(2026, 9, 6), Sep30, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Abierto_VaDesdeElDiaDeInicioHastaElDiaDeTerminarAmbosIncluidos()
    {
        var followUp = FollowUp(new DateTime(2026, 9, 10, 8, 0, 0), new DateTime(2026, 9, 12, 20, 0, 0), null);

        Assert.True(SupervisionIndicatorRules.WasOpen(followUp, new DateOnly(2026, 9, 12), new DateOnly(2026, 9, 20), TimeZoneInfo.Utc));
        Assert.True(SupervisionIndicatorRules.WasOpen(followUp, Sep1, new DateOnly(2026, 9, 10), TimeZoneInfo.Utc));
        Assert.False(SupervisionIndicatorRules.WasOpen(followUp, new DateOnly(2026, 9, 13), Sep30, TimeZoneInfo.Utc));
        Assert.False(SupervisionIndicatorRules.WasOpen(followUp, Sep1, new DateOnly(2026, 9, 9), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Vencido_CuentaLosDiasEnLaZonaHorariaDelCentro()
    {
        // Vence el 14 y se reprograma a las 00:30 locales del 15 (22:30 UTC del 14): en Madrid el día 15 empieza todavía con el plan inicial.
        var followUp = FollowUp(new DateTime(2026, 9, 1), null, new DateOnly(2026, 9, 14),
            new IndicatorReschedule(new DateTime(2026, 9, 14, 22, 30, 0), new DateOnly(2026, 9, 30)));
        var day15 = new DateOnly(2026, 9, 15);

        Assert.True(SupervisionIndicatorRules.WasOverdue(followUp, day15, day15, Madrid));
        Assert.False(SupervisionIndicatorRules.WasOverdue(followUp, day15, day15, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Seguimientos_SeCuentanPorUnidad_EnTotal_YMesAMes()
    {
        var facts = Facts(followUps:
        [
            FollowUp(new DateTime(2026, 9, 1), null, new DateOnly(2026, 9, 10)),                            // vencido en septiembre y en octubre
            new(PlantaDos, new DateTime(2026, 9, 1), new DateTime(2026, 9, 5), new DateOnly(2026, 9, 20), []),    // terminó antes de vencer
            FollowUp(new DateTime(2026, 10, 20), null, new DateOnly(2026, 11, 30)),                        // abierto en octubre sin vencer
        ]);

        var result = SupervisionIndicatorRules.Aggregate(facts, Scope, Sep1, new DateOnly(2026, 10, 31), TimeZoneInfo.Utc);

        Assert.Equal((3, 1), (result.Total.FollowUpsOpen, result.Total.FollowUpsOverdue));
        Assert.Equal((2, 1), (result.Units[0].Counts.FollowUpsOpen, result.Units[0].Counts.FollowUpsOverdue));
        Assert.Equal((1, 0), (result.Units[1].Counts.FollowUpsOpen, result.Units[1].Counts.FollowUpsOverdue));
        Assert.Equal([(2, 1), (2, 1)], result.Months.Select(m => (m.Counts.FollowUpsOpen, m.Counts.FollowUpsOverdue)));
    }

    [Fact]
    public void LimitesUtc_SonLaMedianocheLocalDelPrimerDiaYDelDiaSiguienteAlUltimo()
    {
        // Del 25/10 (verano, UTC+2) al 25/10, día del cambio a horario de invierno, que dura 25 horas.
        var (from, toExclusive) = SupervisionIndicatorRules.UtcBounds(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25), Madrid);

        Assert.Equal(new DateTime(2026, 10, 24, 22, 0, 0), from);
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0), toExclusive);
    }
}
