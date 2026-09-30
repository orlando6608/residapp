using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class AssessmentCorrectionTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ventana_AbiertaHastaJustoAntesDelLimite_YCerradaDesdeElLimite()
    {
        var window = TimeSpan.FromHours(6);
        Assert.True(AssessmentCorrectionWindow.IsOpen(SavedAt, SavedAt, window));
        Assert.True(AssessmentCorrectionWindow.IsOpen(SavedAt, SavedAt + window - TimeSpan.FromMilliseconds(1), window));
        Assert.False(AssessmentCorrectionWindow.IsOpen(SavedAt, SavedAt + window, window));
        Assert.False(AssessmentCorrectionWindow.IsOpen(SavedAt, SavedAt, TimeSpan.Zero));
    }

    [Fact]
    public void Motivo_SeRecorta() =>
        Assert.Equal("Constante mal transcrita.", new AssessmentCorrectionReason("  Constante mal transcrita.  ").Text);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Motivo_Vacio_SeRechaza(string? text)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new AssessmentCorrectionReason(text));
        Assert.Equal("ASSESSMENT_CORRECTION_REASON_INVALID", ex.Message);
    }

    [Fact]
    public void Motivo_DemasiadoLargo_SeRechaza() =>
        Assert.Throws<DomainValidationException>(() => new AssessmentCorrectionReason(new string('a', AssessmentCorrectionReason.MaxLength + 1)));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Rectificacion_SinTexto_SeRechaza(string? text)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new AssessmentRectification(text, "Motivo."));
        Assert.Equal("ASSESSMENT_RECTIFICATION_TEXT_INVALID", ex.Message);
    }

    [Fact]
    public void Rectificacion_ExigeMotivo() =>
        Assert.Equal("ASSESSMENT_CORRECTION_REASON_INVALID",
            Assert.Throws<DomainValidationException>(() => new AssessmentRectification("Texto.", " ")).Message);
}
