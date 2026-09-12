using ResidApp.Domain.Baseline.Answers;
using ResidApp.Domain.Baseline.Catalogs;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>Cubre CognitionAreaAnswer como muestra representativa de las reglas cruzadas de las 9
/// respuestas de área (instrucciones-migracion-net10.md la señala como "la regla más intrincada del
/// catálogo"); no se replican aquí las 9 áreas completas.</summary>
public class CognitionAreaAnswerTests
{
    [Fact]
    public void EtiologyWithoutDementiaDocumented_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new CognitionAreaAnswer(
            CognitionCategoryCode.DeterioroCognitivoLeveDocumentado, EtiologyCode.EnfermedadAlzheimer, null,
            null, null, null, null));

        Assert.Equal("BASELINE_COGNITION_ETIOLOGY_INVALID", ex.Code);
    }

    [Fact]
    public void EtiologyOtra_WithoutFreeText_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new CognitionAreaAnswer(
            CognitionCategoryCode.DemenciaDocumentada, EtiologyCode.Otra, null,
            null, InformationSourceCode.HistoriaOInformeClinico, null, "2020-01-01"));

        Assert.Equal("BASELINE_OPEN_TEXT_REQUIRED", ex.Code);
    }

    [Fact]
    public void HasClinicalReference_WithoutSourceCode_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new CognitionAreaAnswer(
            CognitionCategoryCode.DemenciaDocumentada, EtiologyCode.EnfermedadAlzheimer, null,
            null, null, null, "2020-01-01"));

        Assert.Equal("BASELINE_COGNITION_SOURCE_INVALID", ex.Code);
    }

    [Fact]
    public void NoClinicalReference_ButSourceCodeProvidedAnyway_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new CognitionAreaAnswer(
            CognitionCategoryCode.SinDeterioroConocidoODocumentado, null, null,
            null, InformationSourceCode.HistoriaOInformeClinico, null, null));

        Assert.Equal("BASELINE_COGNITION_REFERENCE_WITHOUT_DATA", ex.Code);
    }

    [Fact]
    public void DementiaWithEtiologyAndCompleteReference_Succeeds()
    {
        var answer = new CognitionAreaAnswer(
            CognitionCategoryCode.DemenciaDocumentada, EtiologyCode.EnfermedadAlzheimer, null,
            null, InformationSourceCode.HistoriaOInformeClinico, null, "2020-01-01");

        Assert.Equal(EtiologyCode.EnfermedadAlzheimer, answer.EtiologyCode);
    }

    [Fact]
    public void NoDeteriorationAndNoReference_Succeeds()
    {
        var answer = new CognitionAreaAnswer(
            CognitionCategoryCode.SinDeterioroConocidoODocumentado, null, null, null, null, null, null);

        Assert.Null(answer.EtiologyCode);
    }
}
