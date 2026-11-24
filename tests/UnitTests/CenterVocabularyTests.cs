using ResidApp.Domain.Platform;

namespace ResidApp.UnitTests;

public class CenterVocabularyTests
{
    [Fact]
    public void SinTipo_ElVocabularioEsElDeUnaResidencia()
    {
        Assert.Equal("la residencia", CenterVocabulary.TheService);
        Assert.Equal("La residencia", CenterVocabulary.TheServiceStart);
        Assert.Equal("fuera de la residencia", CenterVocabulary.OutsideTheService);
        Assert.Equal("Unidad (y habitación o plaza)", CenterVocabulary.UnitAndPlaceHeading);
        Assert.Contains("habitación o plaza", CenterVocabulary.TransferChanges);
    }

    [Fact]
    public void EnUnCentroDeDia_NoSeHablaDeResidenciaNiDeHabitaciones()
    {
        using var _ = CurrentCenterService.Use(CenterServiceType.CentroDeDia);

        Assert.Equal("el centro de día", CenterVocabulary.TheService);
        Assert.Equal("El centro de día", CenterVocabulary.TheServiceStart);
        Assert.Equal("fuera del centro de día", CenterVocabulary.OutsideTheService);
        Assert.Equal("Unidad", CenterVocabulary.UnitAndPlaceHeading);
        Assert.DoesNotContain("habitación", CenterVocabulary.TransferChanges);
    }

    [Fact]
    public void EnUnaResidenciaDeclarada_EsElMismoVocabularioQueSinTipo()
    {
        using var _ = CurrentCenterService.Use(CenterServiceType.Residencia);

        Assert.Equal("la residencia", CenterVocabulary.TheService);
    }
}
