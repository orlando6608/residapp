using ResidApp.Application.Ports;

namespace ResidApp.UnitTests;

public class ResidentLocationLabelTests
{
    [Theory]
    [InlineData("Unidad 1", null, null, "Unidad 1")]
    [InlineData("Unidad 1", "Habitación 12", null, "Unidad 1 · Habitación 12")]
    [InlineData("Unidad 1", "Habitación 12", "Cama A", "Unidad 1 · Habitación 12 · Cama A")]
    [InlineData(null, null, null, "Sin unidad")]
    [InlineData("Unidad 1", "", "", "Unidad 1")]
    public void Format_OmiteLoQueNoConsta(string? unit, string? room, string? place, string expected) =>
        Assert.Equal(expected, ResidentLocationLabel.Format(unit, room, place));
}
