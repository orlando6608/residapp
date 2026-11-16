using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Reglas del apartado de enfermedades e intervenciones de la ficha de antecedentes de salud (fase 2; docs/producto/ficha-antecedentes-salud-diseno.md). Los nombres
/// de enfermedades de las pruebas son inventados.</summary>
public class HealthBackgroundConditionsTests
{
    [Fact]
    public void ConEntradas_ExigeAlMenosUnElemento_YLosOtrosEstadosNoAdmitenNinguno()
    {
        var item = new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", null);

        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.ConEntradas, [], null));
        // A diferencia de las alergias (que admiten intolerancias), «refiere que no hay» y «no consta» no admiten elementos.
        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.NoRefiere, [item], null));
        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.NoConsta, [item], null));

        Assert.Single(new ConditionRecordContent(HealthBackgroundState.ConEntradas, [item], null).Items);
        Assert.Empty(new ConditionRecordContent(HealthBackgroundState.NoRefiere, null, null).Items);
        Assert.Empty(new ConditionRecordContent(HealthBackgroundState.NoConsta, [], HealthBackgroundSource.FamiliaCuidadores).Items);
    }

    [Fact]
    public void ElAnioEsOpcional_NoPuedeSerFuturoNiAnteriorA1900()
    {
        Assert.Null(new ConditionItem(ConditionItemType.Intervencion, "Intervención Beta", null).Year);
        Assert.Equal(1999, new ConditionItem(ConditionItemType.Intervencion, "Intervención Beta", 1999).Year);
        Assert.Equal(ConditionItem.MinYear, new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", ConditionItem.MinYear).Year);
        Assert.Equal(DateTime.Now.Year, new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", DateTime.Now.Year).Year);

        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", DateTime.Now.Year + 1));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", ConditionItem.MinYear - 1));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", 0));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", -2001));
    }

    [Fact]
    public void ElNombreEsCortoYDeUnaSolaLinea()
    {
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, null, null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "   ", null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, new string('a', ConditionItem.MaxNameLength + 1), null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad\nAlfa", null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad\tAlfa", null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad" + (char)0x2028 + "Alfa", null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad" + (char)0x2029 + "Alfa", null));
        Assert.Throws<DomainValidationException>(() => new ConditionItem((ConditionItemType)99, "Enfermedad Alfa", null));

        Assert.Equal(ConditionItem.MaxNameLength, new ConditionItem(ConditionItemType.Enfermedad, new string('a', ConditionItem.MaxNameLength), null).Name.Length);
        Assert.Equal("Enfermedad Alfa", new ConditionItem(ConditionItemType.Enfermedad, "  Enfermedad Alfa  ", null).Name);
    }

    [Fact]
    public void NoSeAdmitenDuplicados_PeroMismoNombreConOtroAnioOOtroTipoNoLoEs()
    {
        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.ConEntradas,
            [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", 2010), new ConditionItem(ConditionItemType.Enfermedad, "ENFERMEDAD ALFA", 2010)], null));
        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.ConEntradas,
            [new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", null), new ConditionItem(ConditionItemType.Enfermedad, "enfermedad alfa", null)], null));

        var different = new ConditionRecordContent(HealthBackgroundState.ConEntradas,
        [
            new ConditionItem(ConditionItemType.Intervencion, "Procedimiento Beta", 2010),
            new ConditionItem(ConditionItemType.Intervencion, "Procedimiento Beta", 2015),
            new ConditionItem(ConditionItemType.Enfermedad, "Procedimiento Beta", 2010),
        ], null);
        Assert.Equal(3, different.Items.Count);
    }

    [Fact]
    public void LaListaTieneUnTopeTecnicoDeCien()
    {
        var items = Enumerable.Range(1, ConditionRecordContent.MaxItems + 1)
            .Select(i => new ConditionItem(ConditionItemType.Enfermedad, $"Enfermedad {i}", null)).ToList();

        Assert.Equal(100, ConditionRecordContent.MaxItems);
        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.ConEntradas, items, null));
        Assert.Equal(ConditionRecordContent.MaxItems,
            new ConditionRecordContent(HealthBackgroundState.ConEntradas, items.Take(ConditionRecordContent.MaxItems).ToList(), null).Items.Count);
    }

    [Fact]
    public void ElEstadoYLaFuenteTienenQueSerDeLaListaCerrada()
    {
        var item = new ConditionItem(ConditionItemType.Enfermedad, "Enfermedad Alfa", null);

        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent((HealthBackgroundState)99, [item], null));
        Assert.Throws<DomainValidationException>(() => new ConditionRecordContent(HealthBackgroundState.ConEntradas, [item], (HealthBackgroundSource)99));
    }
}
