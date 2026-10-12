using ResidApp.Application.Ports;
using ResidApp.Application.UseCases;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.UnitTests;

/// <summary>DIR-13: la frecuencia y el estado de los comunicados a la familia por unidad, solo con cifras.</summary>
public class UnitCommunicationFrequencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    private static FamilyCommunicationStatusView Communication(
        string? unit, DateTimeOffset preparedAt, bool published, int correctives = 0, bool withdrawn = false)
    {
        var scheduledAt = preparedAt.AddHours(8);
        var correctiveList = Enumerable.Range(0, correctives)
            .Select(_ => new FamilyCommunicationCorrective(Guid.NewGuid(), FamilyCommunicationType.Ordinaria, "Texto", scheduledAt.AddHours(1))).ToList();
        return new(new AdministrationFamilyCommunication(
                Guid.NewGuid(), Guid.NewGuid(), ResidentId.From(Guid.NewGuid()), "Residente", unit, FamilyCommunicationType.Ordinaria, "Texto",
                preparedAt, null, scheduledAt, correctiveList, withdrawn ? new FamilyCommunicationWithdrawalNote("Motivo", scheduledAt) : null),
            scheduledAt, published, published && !withdrawn);
    }

    [Fact]
    public void CuentaPorUnidad_ElEstado_LaUltimaSemana_YOrdenaPorNombre()
    {
        var units = UnitCommunicationFrequency.From(
        [
            Communication("Planta 2", Now.AddDays(-1), published: false),
            Communication("Planta 1", Now.AddDays(-2), published: true),
            Communication("Planta 1", Now.AddDays(-10), published: true, correctives: 2),
            Communication("Planta 1", Now.AddDays(-20), published: true, withdrawn: true),
            Communication("Planta 1", Now.AddDays(-7), published: true),
            Communication(null, Now.AddDays(-3), published: true),
        ], Now);

        Assert.Equal(["Planta 1", "Planta 2", "Sin unidad"], units.Select(u => u.UnitName));
        // Planta 1: 4 comunicados, ninguno programado; 3 publicados (el retirado no cuenta como publicado), uno con correctora, uno retirado y dos de la última semana.
        Assert.Equal((4, 0, 3, 1, 1, 2), (units[0].Total, units[0].Scheduled, units[0].Published, units[0].WithCorrective, units[0].Withdrawn, units[0].PreparedLastWeek));
        Assert.Equal((1, 1, 0, 0, 0, 1), (units[1].Total, units[1].Scheduled, units[1].Published, units[1].WithCorrective, units[1].Withdrawn, units[1].PreparedLastWeek));
        Assert.Equal((1, 0, 1, 0, 0, 1), (units[2].Total, units[2].Scheduled, units[2].Published, units[2].WithCorrective, units[2].Withdrawn, units[2].PreparedLastWeek));
    }

    [Fact]
    public void PorSemana_SeCalculaSobreLaVentana_YLaSumaJuntaLasUnidades()
    {
        var units = UnitCommunicationFrequency.From(
            Enumerable.Range(0, 15).Select(i => Communication("Planta 1", Now.AddDays(-i), published: true)).Concat(
                [Communication("Planta 2", Now, published: false)]), Now);
        var total = UnitCommunicationFrequency.Sum(units);

        // 15 comunicados en 30 días: 3,5 por semana.
        Assert.Equal(3.5, units[0].PerWeek);
        Assert.Equal((16, 1, 15), (total.Total, total.Scheduled, total.Published));
        Assert.Empty(UnitCommunicationFrequency.From([], Now));
    }
}
