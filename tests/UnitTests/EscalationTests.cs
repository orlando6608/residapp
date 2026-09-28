using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

public class EscalationTests
{
    [Fact]
    public void Motivo_SeRecorta() =>
        Assert.Equal("Disnea progresiva.", new EscalationReason("  Disnea progresiva.  ").Text);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Motivo_Vacio_SeRechaza(string? text)
    {
        var ex = Assert.Throws<DomainValidationException>(() => new EscalationReason(text));
        Assert.Equal("CLINICAL_EVENT_ESCALATION_REASON_INVALID", ex.Message);
    }

    [Fact]
    public void Motivo_DemasiadoLargo_SeRechaza() =>
        Assert.Throws<DomainValidationException>(() => new EscalationReason(new string('a', EscalationReason.MaxLength + 1)));
}
