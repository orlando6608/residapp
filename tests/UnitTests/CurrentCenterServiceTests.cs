using ResidApp.Domain.Platform;

namespace ResidApp.UnitTests;

public class CurrentCenterServiceTests
{
    [Fact]
    public void SinFijar_NoHayTipo_YSeComportaComoUnaResidencia()
    {
        Assert.Null(CurrentCenterService.Type);
        Assert.False(CurrentCenterService.IsDayCenter);
    }

    [Fact]
    public void Use_FijaElTipoHastaLiberarlo_YRestauraElAnterior()
    {
        using (CurrentCenterService.Use(CenterServiceType.CentroDeDia))
        {
            Assert.True(CurrentCenterService.IsDayCenter);
            using (CurrentCenterService.Use(CenterServiceType.Residencia))
            {
                Assert.False(CurrentCenterService.IsDayCenter);
                Assert.Equal(CenterServiceType.Residencia, CurrentCenterService.Type);
            }

            Assert.True(CurrentCenterService.IsDayCenter);
        }

        Assert.Null(CurrentCenterService.Type);
    }

    [Fact]
    public async Task ElTipoEsDeLaPeticion_NoSeFiltraAOtraTareaEnParalelo()
    {
        using var _ = CurrentCenterService.Use(CenterServiceType.CentroDeDia);
        var other = await Task.Run(async () =>
        {
            await Task.Yield();
            using (CurrentCenterService.Use(CenterServiceType.Residencia))
            {
                return CurrentCenterService.Type;
            }
        });

        Assert.Equal(CenterServiceType.Residencia, other);
        Assert.Equal(CenterServiceType.CentroDeDia, CurrentCenterService.Type);
    }
}
