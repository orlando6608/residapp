using ResidApp.Domain.Enfermeria;

namespace ResidApp.Application.UseCases;

/// <summary>DIR-13: cuántos comunicados a la familia tiene una unidad en la ventana de <see cref="FamilyCommunicationPublicationApplicationService.WindowDays"/> días,
/// en qué estado están y cuántos se prepararon en la última semana. Solo cifras: nada del texto ni del residente. Un comunicado retirado cuenta como retirado y
/// no como publicado; uno publicado con alguna correctora cuenta además como «con correctora».</summary>
public sealed record UnitCommunicationFrequency(
    string UnitName, int Total, int Scheduled, int Published, int WithCorrective, int Withdrawn, int PreparedLastWeek)
{
    public const int RecentDays = 7;

    /// <summary>Comunicados por semana en la ventana de <see cref="FamilyCommunicationPublicationApplicationService.WindowDays"/> días.</summary>
    public double PerWeek => Math.Round(Total * 7.0 / FamilyCommunicationPublicationApplicationService.WindowDays, 1);

    /// <summary>Las unidades que tienen algún comunicado en la ventana, por nombre; los comunicados de un residente sin unidad vigente salen juntos como «Sin unidad».</summary>
    public static IReadOnlyList<UnitCommunicationFrequency> From(IEnumerable<FamilyCommunicationStatusView> communications, DateTimeOffset now) =>
        communications
            .Where(c => c.Communication.PreparedAt >= now.AddDays(-FamilyCommunicationPublicationApplicationService.WindowDays))
            .GroupBy(c => c.Communication.UnitName ?? "Sin unidad")
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new UnitCommunicationFrequency(
                g.Key, g.Count(),
                g.Count(c => !c.Published),
                g.Count(c => c.Published && c.Communication.Withdrawal is null),
                g.Count(c => c.Published && c.Communication.Withdrawal is null && c.Communication.Correctives.Count > 0),
                g.Count(c => c.Communication.Withdrawal is not null),
                g.Count(c => c.Communication.PreparedAt >= now.AddDays(-RecentDays))))
            .ToList();

    /// <summary>La suma de las unidades, para la fila de totales.</summary>
    public static UnitCommunicationFrequency Sum(IReadOnlyList<UnitCommunicationFrequency> units) => new(
        "Todas", units.Sum(u => u.Total), units.Sum(u => u.Scheduled), units.Sum(u => u.Published), units.Sum(u => u.WithCorrective),
        units.Sum(u => u.Withdrawn), units.Sum(u => u.PreparedLastWeek));
}
