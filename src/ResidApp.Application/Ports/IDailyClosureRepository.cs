using ResidApp.Domain.Auxiliar;
using ResidApp.Domain.Residents;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Traduce el registro de un cierre cotidiano del Auxiliar (AUX-04/AUX-05). Reason solo se
/// persiste cuando Type es NoValorable; RegisterDailyClosure ya lo exige antes de llegar aquí, y la
/// migración lo repite como CHECK (defensa en profundidad, mismo criterio que el resto del esquema).</summary>
public sealed record RegisterDailyClosureInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId,
    DailyClosureType Type, string? Reason, Guid OperationId);

public sealed record DailyClosureResult(Guid ClosureId, DateTimeOffset OccurredAt);

/// <summary>Una de las áreas observadas de "Registrar cambio" (AUX-06/AUX-07): opciones rápidas marcadas
/// del catálogo cerrado de DailyChangeAreaOptionsCatalog, texto libre, o ambas. RegisterDailyChange ya
/// exige que al menos una de las dos llegue con contenido (texto obligatorio en las tres áreas sin
/// checklist) antes de llegar aquí; la migración lo repite como CHECK donde es posible.</summary>
public sealed record DailyChangeAreaInput(
    DailyChangeAreaCode AreaCode, IReadOnlyList<DailyChangeAreaOptionCode> Options, string? FreeText);

/// <summary>Traduce el registro de "Registrar cambio" (AUX-06 a AUX-12): Areas exige al menos una
/// (AUX-06); PriorityReason/DirectNoticeNotes solo se persisten cuando Classification es Prioritario
/// (AUX-10/AUX-11B) — RegisterDailyChange ya lo exige antes de llegar aquí, la migración lo repite como
/// CHECK.</summary>
public sealed record RegisterDailyChangeInput(
    AccountId AccountId, CenterId CenterId, UnitId UnitId, ResidentId ResidentId,
    IReadOnlyList<DailyChangeAreaInput> Areas, decimal? TemperatureCelsius,
    DailyChangeClassification Classification, DailyChangePriorityReason? PriorityReason,
    string? DirectNoticeNotes, Guid OperationId);

/// <summary>AUX-04 (Sin cambios), AUX-05 (No valorable) y AUX-06 a AUX-12 (Registrar cambio): registro
/// idempotente de un cierre cotidiano, las tres acciones mutuamente excluyentes del Auxiliar.</summary>
public interface IDailyClosureRepository
{
    Task<DailyClosureResult> RegisterAsync(RegisterDailyClosureInput input, CancellationToken ct = default);

    Task<DailyClosureResult> RegisterChangeAsync(RegisterDailyChangeInput input, CancellationToken ct = default);
}
