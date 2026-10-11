using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una fila del historial del horario de los comunicados: el horario antes y después. De quien lo cambió solo se expone si fue la
/// propia cuenta.</summary>
public sealed record FamilyCommunicationScheduleChange(
    FamilyCommunicationScheduleSettings Previous, FamilyCommunicationScheduleSettings New, bool ChangedByCurrentAccount, DateTimeOffset ChangedAt);

/// <summary>El horario vigente del centro (el de CJ salvo lo que el centro haya cambiado), su historial (el más reciente primero) y la versión
/// para la concurrencia optimista (número de cambios registrados en el centro).</summary>
public sealed record FamilyCommunicationScheduleView(
    FamilyCommunicationScheduleSettings Settings, IReadOnlyList<FamilyCommunicationScheduleChange> History, int Version);

/// <summary>
/// Hora de publicación y margen de los comunicados a la familia por centro (script 0051; CJ, 2026-10-10). Lo lee y lo guarda Administración
/// del centro (si no, AccessDenied). Guardar exige la versión leída (si otro cambió el horario entretanto,
/// FAMILY_COMMUNICATION_SCHEDULE_REVISION_CONFLICT), registra una fila de historial y devuelve la nueva versión; sin cambios no registra nada.
/// </summary>
public interface IFamilyCommunicationScheduleRepository
{
    Task<FamilyCommunicationScheduleView> ReadAsync(AccountAdministrationAccess access, CancellationToken ct = default);

    Task<int> SaveAsync(
        AccountAdministrationAccess access, int expectedVersion, FamilyCommunicationScheduleSettings desired, CancellationToken ct = default);

    /// <summary>El horario que rige hoy en el centro, para fijar la hora de un comunicado al guardarlo. Sin permiso: quien llama ya ha
    /// comprobado su ámbito.</summary>
    Task<FamilyCommunicationScheduleSettings> GetEffectiveAsync(CenterId centerId, CancellationToken ct = default);
}
