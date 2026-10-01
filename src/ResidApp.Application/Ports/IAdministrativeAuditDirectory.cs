using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>ADM-28: la consulta. FromUtc/ToExclusiveUtc son los límites UTC del periodo; Action, si no es null, es una de
/// AdministrativeAudit.Actions; AffectedAccountId filtra por la cuenta afectada (nunca por quien actuó, AUD-02).</summary>
public sealed record AdministrativeAuditQuery(DateTime FromUtc, DateTime ToExclusiveUtc, string? Action, AccountId? AffectedAccountId);

/// <summary>
/// ADM-28: un evento de auditoría administrativa. Solo nombres y códigos: la tabla no guarda valores ni texto, y este tipo no tiene
/// ningún campo de texto libre ni contadores (AUD-02). ActorName es el nombre visible de la cuenta (o su identificador de acceso);
/// AffectedName, la cuenta afectada si la hay; UnitName y ResidentName, cuando constan.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset OccurredAt, string ActorName, SystemProfile ActorProfile, string Action, string? AffectedName, string? UnitName,
    string? ResidentName);

/// <summary>Los eventos más recientes primero. Truncated dice que había más de MaxEntries y se han quitado los más antiguos.</summary>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, bool Truncated)
{
    public const int MaxEntries = 500;
}

/// <summary>ADM-28: lectura de la auditoría administrativa del ámbito de Administración (centro del ámbito; los eventos con unidad,
/// solo si la unidad está concedida al ámbito). Solo lee: dbo.eventos_auditoria es de solo inserción.</summary>
public interface IAdministrativeAuditDirectory
{
    Task<AuditPage> ListAsync(AccountAdministrationAccess access, AdministrativeAuditQuery query, CancellationToken ct = default);
}
