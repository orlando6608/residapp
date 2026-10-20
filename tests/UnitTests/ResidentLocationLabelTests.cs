using ResidApp.Application.Ports;

namespace ResidApp.UnitTests;

public class ResidentLocationLabelTests
{
    [Theory]
    [InlineData("Unidad 1", null, null, "Unidad 1 · Sin habitación asignada")]
    [InlineData("Unidad 1", "Habitación 12", null, "Unidad 1 · Habitación 12")]
    [InlineData("Unidad 1", "Habitación 12", "Cama A", "Unidad 1 · Habitación 12 · Cama A")]
    [InlineData(null, null, null, "Sin unidad · Sin habitación asignada")]
    [InlineData("Unidad 1", "", "", "Unidad 1 · Sin habitación asignada")]
    public void Format_DiceSiNoTieneHabitacion_YOmiteLaPlazaQueNoConsta(string? unit, string? room, string? place, string expected) =>
        Assert.Equal(expected, ResidentLocationLabel.Format(unit, room, place));
}
