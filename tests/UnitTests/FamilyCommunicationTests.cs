using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class FamilyCommunicationTests
{
    [Fact]
    public void NoComunicar_SinTipoNiTexto_SeAcepta()
    {
        var choice = new FamilyCommunicationChoice(FamilyCommunicationDecision.NoComunicar, null, "   ");

        Assert.Equal(FamilyCommunicationDecision.NoComunicar, choice.Decision);
        Assert.Null(choice.Text);
    }

    [Fact]
    public void Preparar_ConTipoYTexto_SeAceptaRecortado()
    {
        var choice = new FamilyCommunicationChoice(FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Relevante, "  Está tranquila.  ");

        Assert.Equal(FamilyCommunicationType.Relevante, choice.Type);
        Assert.Equal("Está tranquila.", choice.Text);
    }

    [Fact]
    public void SinDecision_SeRechaza()
    {
        var ex = Assert.Throws<DomainValidationException>(() => new FamilyCommunicationChoice(null, null, null));
        Assert.Equal("FAMILY_COMMUNICATION_DECISION_REQUIRED", ex.Message);
    }

    [Theory]
    [InlineData(FamilyCommunicationDecision.Preparar, null, "Texto.")]
    [InlineData(FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, " ")]
    [InlineData(FamilyCommunicationDecision.NoComunicar, FamilyCommunicationType.Ordinaria, null)]
    [InlineData(FamilyCommunicationDecision.NoComunicar, null, "Texto.")]
    public void CombinacionIncoherente_SeRechaza(FamilyCommunicationDecision decision, FamilyCommunicationType? type, string? text)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new FamilyCommunicationChoice(decision, type, text));
        Assert.Equal("FAMILY_COMMUNICATION_INVALID", ex.Message);
    }

    [Fact]
    public void Preparar_ConTextoDemasiadoLargo_SeRechaza() =>
        Assert.Throws<DomainValidationException>(() => new FamilyCommunicationChoice(
            FamilyCommunicationDecision.Preparar, FamilyCommunicationType.Ordinaria, new string('a', FamilyCommunicationChoice.MaxTextLength + 1)));
}
