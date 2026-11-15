using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Reglas del apartado de alergias de la ficha de antecedentes de salud (docs/producto/ficha-antecedentes-salud-diseno.md). Los nombres de fármacos
/// de las pruebas son inventados.</summary>
public class HealthBackgroundTests
{
    [Fact]
    public void ConEntradas_ExigeAlMenosUnaAlergia_YLasIntoleranciasSolasNoBastan()
    {
        Assert.Throws<DomainValidationException>(() => new AllergyRecordContent(HealthBackgroundState.ConEntradas, [], null));
        Assert.Throws<DomainValidationException>(() => new AllergyRecordContent(
            HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Alfa", "náuseas")], null));

        var content = new AllergyRecordContent(HealthBackgroundState.ConEntradas, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", null)], null);

        Assert.Single(content.Items);
    }

    [Theory]
    [InlineData(HealthBackgroundState.NoRefiere)]
    [InlineData(HealthBackgroundState.NoConsta)]
    public void NoRefiereYNoConsta_NoAdmitenAlergias_PeroSiIntolerancias(HealthBackgroundState state)
    {
        Assert.Throws<DomainValidationException>(() => new AllergyRecordContent(state, [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", null)], null));

        var empty = new AllergyRecordContent(state, null, null);
        var withIntolerance = new AllergyRecordContent(state, [new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Alfa", "picor")], HealthBackgroundSource.Residente);

        Assert.Empty(empty.Items);
        Assert.Single(withIntolerance.Items);
    }

    [Fact]
    public void LaIntoleranciaExigeSuReaccion_YLaAlergiaLaAdmiteOpcional()
    {
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Alfa", null));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Alfa", "   "));

        Assert.Null(new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", "  ").Reaction);
        Assert.Equal("urticaria", new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", " urticaria ").Reaction);
    }

    [Fact]
    public void ElTextoEsCortoYDeUnaSolaLinea_YSinDuplicados()
    {
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, " ", null));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, new string('a', AllergyItem.MaxNameLength + 1), null));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", new string('a', AllergyItem.MaxReactionLength + 1)));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, "Fármaco\nBeta", null));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", "urticaria\r\nsegún su hija"));
        // Tabulador interior y separadores de línea y de párrafo Unicode (U+2028, U+2029), que char.IsControl no cubre.
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, "Fármaco\tBeta", null));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, "Fármaco" + (char)0x2028 + "Beta", null));
        Assert.Throws<DomainValidationException>(() => new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", "urticaria" + (char)0x2029 + "según su hija"));
        // Los máximos exactos se aceptan.
        Assert.Equal(AllergyItem.MaxNameLength, new AllergyItem(AllergyItemType.Alergia, new string('a', AllergyItem.MaxNameLength), null).Name.Length);
        Assert.Equal(AllergyItem.MaxReactionLength,
            new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", new string('a', AllergyItem.MaxReactionLength)).Reaction!.Length);
        Assert.Throws<DomainValidationException>(() => new AllergyRecordContent(HealthBackgroundState.ConEntradas,
            [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", null), new AllergyItem(AllergyItemType.Alergia, "FÁRMACO BETA", "urticaria")], null));

        // Una alergia y una intolerancia al mismo fármaco no son un duplicado.
        var both = new AllergyRecordContent(HealthBackgroundState.ConEntradas,
            [new AllergyItem(AllergyItemType.Alergia, "Fármaco Beta", null), new AllergyItem(AllergyItemType.Intolerancia, "Fármaco Beta", "náuseas")], null);
        Assert.Equal(2, both.Items.Count);
    }

    [Fact]
    public void LaListaTieneUnTope()
    {
        var items = Enumerable.Range(1, AllergyRecordContent.MaxItems + 1)
            .Select(i => new AllergyItem(AllergyItemType.Alergia, $"Fármaco {i}", null)).ToList();

        Assert.Throws<DomainValidationException>(() => new AllergyRecordContent(HealthBackgroundState.ConEntradas, items, null));
        Assert.Equal(AllergyRecordContent.MaxItems, new AllergyRecordContent(HealthBackgroundState.ConEntradas, items.Take(AllergyRecordContent.MaxItems).ToList(), null).Items.Count);
    }
}
