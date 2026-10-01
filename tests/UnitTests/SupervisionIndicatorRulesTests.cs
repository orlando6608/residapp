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
        IReadOnlyList<IndicatorIndicationFact>? indications = null, IReadOnlyList<IndicatorTransferFact>? transfers = null) =>
        new(episodes ?? [], closures ?? [], indications ?? [], transfers ?? []);

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

    [Fact]
    public void LimitesUtc_SonLaMedianocheLocalDelPrimerDiaYDelDiaSiguienteAlUltimo()
    {
        // Del 25/10 (verano, UTC+2) al 25/10, día del cambio a horario de invierno, que dura 25 horas.
        var (from, toExclusive) = SupervisionIndicatorRules.UtcBounds(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25), Madrid);

        Assert.Equal(new DateTime(2026, 10, 24, 22, 0, 0), from);
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0), toExclusive);
    }
}
