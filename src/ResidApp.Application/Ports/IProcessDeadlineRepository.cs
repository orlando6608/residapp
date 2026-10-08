using ResidApp.Domain.Supervision;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una fila del historial de plazos de los hitos: el plazo vigente antes y después (null en un plazo: no se mide). De quien lo
/// cambió solo se expone el perfil y si fue la propia cuenta.</summary>
public sealed record ProcessDeadlineChange(
    ProcessMilestone Milestone, MilestoneDeadline Previous, MilestoneDeadline New, SystemProfile ChangedByProfile,
    bool ChangedByCurrentAccount, DateTimeOffset ChangedAt);

/// <summary>Plazos vigentes del centro (los de CJ salvo lo que el centro haya cambiado), su historial (el más reciente primero) y la versión
/// para la concurrencia optimista (número de cambios registrados en el centro).</summary>
public sealed record ProcessDeadlinesView(
    IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline> Deadlines, IReadOnlyList<ProcessDeadlineChange> History, int Version);

public sealed record ProcessDeadlinesAccess(Guid ProfileScopeId, AccountId AccountId, SystemProfile ActiveProfile, CenterId CenterId);

/// <summary>Deadlines trae el plazo deseado de cada hito (los que falten quedan como están).</summary>
public sealed record SaveProcessDeadlinesInput(
    ProcessDeadlinesAccess Access, int ExpectedVersion, IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline> Deadlines);

/// <summary>
/// Plazos de los hitos del proceso por centro (script 0046). Leer y guardar exigen el permiso activo PROCESS_DEADLINES_MANAGE en ese ámbito (si
/// no, PROCESS_DEADLINES_NOT_AUTHORIZED). Guardar exige la versión leída (si otro cambió los plazos entretanto,
/// PROCESS_DEADLINES_REVISION_CONFLICT), registra una fila de historial por hito que cambia y devuelve la nueva versión.
/// </summary>
public interface IProcessDeadlineRepository
{
    Task<ProcessDeadlinesView> ReadAsync(ProcessDeadlinesAccess access, CancellationToken ct = default);

    Task<int> SaveAsync(SaveProcessDeadlinesInput input, CancellationToken ct = default);

    /// <summary>Los plazos que rigen hoy en el centro, para medir los hitos y avisar. Sin permiso: los usan Dirección, Enfermería y Medicina
    /// al medir y avisar; quien llama ya ha comprobado su ámbito.</summary>
    Task<IReadOnlyDictionary<ProcessMilestone, MilestoneDeadline>> GetEffectiveAsync(CenterId centerId, CancellationToken ct = default);
}
