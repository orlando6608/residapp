using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Reglas del apartado de medicación habitual de la ficha de antecedentes de salud (fase 3; docs/producto/ficha-antecedentes-salud-diseno.md). Los nombres de fármacos
/// de las pruebas son inventados.</summary>
public class HealthBackgroundMedicationTests
{
    [Fact]
    public void ConEntradas_ExigeAlMenosUnMedicamento_YLosOtrosEstadosNoAdmitenNinguno()
    {
        var item = new MedicationItem("Fármaco Alfa", "1 g", "cada 8 h");

        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent(HealthBackgroundState.ConEntradas, [], null));
        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent(HealthBackgroundState.NoRefiere, [item], null));
        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent(HealthBackgroundState.NoConsta, [item], null));

        Assert.Single(new MedicationRecordContent(HealthBackgroundState.ConEntradas, [item], HealthBackgroundSource.RecetaElectronica).Items);
        Assert.Empty(new MedicationRecordContent(HealthBackgroundState.NoRefiere, null, null).Items);
        Assert.Empty(new MedicationRecordContent(HealthBackgroundState.NoConsta, [], null).Items);
    }

    [Fact]
    public void NombreDosisYPautaSonObligatorios_PeroNoConstaEsUnValorValido()
    {
        Assert.Throws<DomainValidationException>(() => new MedicationItem(null, "1 g", "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", null, "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "1 g", null));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "   ", "cada 8 h"));

        var unknown = new MedicationItem("Fármaco Alfa", "no consta", "no consta");
        Assert.Equal("no consta", unknown.Dose);
        Assert.Equal("no consta", unknown.Schedule);
        // «Según pauta», «si precisa» y las pautas por hora son pautas válidas.
        Assert.Equal("si precisa", new MedicationItem("Fármaco Beta", "10 mg", "si precisa").Schedule);
        Assert.Equal("en la cena", new MedicationItem("Fármaco Beta", "10 mg", "en la cena").Schedule);
        Assert.Equal("a las 22:00", new MedicationItem("Fármaco Beta", "10 mg", "a las 22:00").Schedule);
    }

    [Fact]
    public void LaDosisYLaPautaLlevanAlMenosUnaLetraOUnNumero_ParaQueUnGuionNoTapeElHueco()
    {
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "-", "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "1 g", "?"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "...", "..."));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("-", "1 g", "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem(".", "1 g", "cada 8 h"));
        // Una dosis o una pauta solo con dígitos es válida (por ejemplo, «500»).
        Assert.Equal("500", new MedicationItem("Fármaco Alfa", "500", "12").Dose);
    }

    [Fact]
    public void LosTextosSonCortosYDeUnaSolaLinea()
    {
        Assert.Throws<DomainValidationException>(() => new MedicationItem(new string('a', MedicationItem.MaxNameLength + 1), "1 g", "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", new string('1', MedicationItem.MaxDoseLength + 1), "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "1 g", new string('a', MedicationItem.MaxScheduleLength + 1)));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco\nAlfa", "1 g", "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "1\tg", "cada 8 h"));
        Assert.Throws<DomainValidationException>(() => new MedicationItem("Fármaco Alfa", "1 g", "cada" + (char)0x2028 + "8 h"));

        // Los máximos exactos se aceptan.
        var max = new MedicationItem(new string('a', MedicationItem.MaxNameLength), new string('1', MedicationItem.MaxDoseLength), new string('a', MedicationItem.MaxScheduleLength));
        Assert.Equal(MedicationItem.MaxNameLength, max.Name.Length);
        Assert.Equal(MedicationItem.MaxDoseLength, max.Dose.Length);
        Assert.Equal(MedicationItem.MaxScheduleLength, max.Schedule.Length);
    }

    [Fact]
    public void NoSeAdmitenDuplicados_PeroElMismoFarmacoConOtraDosisOPautaNoLoEs()
    {
        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent(HealthBackgroundState.ConEntradas,
            [new MedicationItem("Fármaco Alfa", "10 u", "por la mañana"), new MedicationItem("FÁRMACO ALFA", "10 U", "POR LA MAÑANA")], null));

        // Una insulina, por ejemplo, puede constar dos veces con distinta dosis o pauta.
        var two = new MedicationRecordContent(HealthBackgroundState.ConEntradas,
            [new MedicationItem("Fármaco Alfa", "10 u", "por la mañana"), new MedicationItem("Fármaco Alfa", "6 u", "por la noche")], null);
        Assert.Equal(2, two.Items.Count);
    }

    [Fact]
    public void LaListaTieneUnTopeTecnicoDeSesenta_YLaFuenteTieneQueSerDeLaListaCerrada()
    {
        var items = Enumerable.Range(1, MedicationRecordContent.MaxItems + 1).Select(i => new MedicationItem($"Fármaco {i}", "1 g", "cada 8 h")).ToList();

        Assert.Equal(60, MedicationRecordContent.MaxItems);
        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent(HealthBackgroundState.ConEntradas, items, null));
        Assert.Equal(MedicationRecordContent.MaxItems,
            new MedicationRecordContent(HealthBackgroundState.ConEntradas, items.Take(MedicationRecordContent.MaxItems).ToList(), null).Items.Count);
        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent(HealthBackgroundState.ConEntradas, items.Take(1).ToList(), (HealthBackgroundSource)99));
        Assert.Throws<DomainValidationException>(() => new MedicationRecordContent((HealthBackgroundState)99, items.Take(1).ToList(), null));
    }
}
