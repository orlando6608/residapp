using ResidApp.Domain.Auxiliar;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Una de las áreas observadas de un cambio, ya para lectura (ENF-04): mismo contenido que
/// DailyChangeAreaInput (escritura), pero como su propio record de puerto en vez de reutilizar el de
/// escritura, igual que el resto del esquema separa entrada y lectura.</summary>
public sealed record PendingChangeAreaSummary(
    DailyChangeAreaCode AreaCode, IReadOnlyList<DailyChangeAreaOptionCode> Options, string? FreeText);

/// <summary>ENF-02/ENF-03: una fila de bandeja (cambio ordinario o prioritario). Areas solo lleva los
/// códigos (la propia bandeja no necesita el detalle completo, ENF-04 sí). Estado no es un campo propio
/// todavía: mientras no exista la valoración (ENF-05, grupo E5), todo elemento de esta bandeja está,
/// invariablemente, "Pendiente" — el llamador lo pinta como constante.</summary>
public sealed record PendingChangeSummary(
    Guid ClosureId, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string? UnitName,
    IReadOnlyList<DailyChangeAreaCode> Areas, SystemProfile AuthorProfile, DateTimeOffset OccurredAt,
    DailyChangePriorityReason? PriorityReason, string? DirectNoticeNotes);

/// <summary>ENF-04: detalle completo de un cambio recibido (observación original íntegra, con opciones y
/// texto libre por área).</summary>
public sealed record PendingChangeDetail(
    Guid ClosureId, ResidentId ResidentId, string ResidentDisplayName, UnitId UnitId, string? UnitName,
    DailyChangeClassification Classification, IReadOnlyList<PendingChangeAreaSummary> Areas, decimal? TemperatureCelsius,
    SystemProfile AuthorProfile, DailyChangePriorityReason? PriorityReason, string? DirectNoticeNotes, DateTimeOffset OccurredAt);

/// <summary>
/// Traduce las bandejas ENF-02 (cambios ordinarios) y ENF-03 (prioritaria), más el detalle ENF-04. Alcance
/// mínimo acordado para el grupo E4: solo consume lo que Auxiliar ya genera (AUX-11A/AUX-12,
/// dbo.cierres_cotidianos_residente); los eventos propios de Enfermería (ENF-16, dbo.eventos_clinicos)
/// todavía no aparecen aquí — quedan pendientes de unificar cuando exista la valoración (ENF-05, grupo E5),
/// que necesitará de todas formas una noción común de "evento" sobre ambos orígenes. Mismo criterio de
/// ámbito "por defecto o restringido" que IEnfermeriaResidentDirectory, porque una bandeja "compartida por
/// unidad" (docs/flujos-clinicos/valoracion-escalado-enfermeria.md) no debe mostrar más residentes de los
/// que el ámbito ya autoriza a nivel individual.
/// </summary>
public interface IChangeInboxDirectory
{
    Task<IReadOnlyList<PendingChangeSummary>> ListAsync(
        Guid profileScopeId, CenterId centerId, DailyChangeClassification classification, CancellationToken ct = default);

    Task<PendingChangeDetail?> FindAsync(Guid profileScopeId, CenterId centerId, Guid closureId, CancellationToken ct = default);
}
