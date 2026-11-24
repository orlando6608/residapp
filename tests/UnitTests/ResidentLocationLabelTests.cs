using ResidApp.Application.Ports;
using ResidApp.Domain.Platform;

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

    [Theory]
    [InlineData("Sala 1", null, null, "Sala 1")]
    [InlineData("Sala 1", "", "", "Sala 1")]
    [InlineData("Sala 1", null, "Plaza 3", "Sala 1 · Plaza 3")]
    [InlineData("Sala 1", "Habitación 2", "Plaza 3", "Sala 1 · Habitación 2 · Plaza 3")]
    [InlineData(null, null, null, "Sin unidad")]
    public void Format_EnUnCentroDeDia_NoAvisaDeQueFaltaLaHabitacion(string? unit, string? room, string? place, string expected) =>
        Assert.Equal(expected, ResidentLocationLabel.Format(unit, room, place, dayCenter: true));

    [Fact]
    public void Format_SinIndicarElTipo_UsaElDelCentroDeLaPeticion()
    {
        Assert.Equal("Unidad 1 · Sin habitación asignada", ResidentLocationLabel.Format("Unidad 1", null, null));
        using (CurrentCenterService.Use(CenterServiceType.CentroDeDia))
        {
            Assert.Equal("Unidad 1", ResidentLocationLabel.Format("Unidad 1", null, null));
        }

        using (CurrentCenterService.Use(CenterServiceType.Residencia))
        {
            Assert.Equal("Unidad 1 · Sin habitación asignada", ResidentLocationLabel.Format("Unidad 1", null, null));
        }
    }
}
