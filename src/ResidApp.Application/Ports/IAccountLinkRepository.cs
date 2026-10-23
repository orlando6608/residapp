using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>La vinculación recién emitida, para la hoja que se entrega. Las fechas son UTC.</summary>
public sealed record IssuedAccountLink(string CenterName, SystemProfile Profile, IReadOnlyList<string> UnitNames, DateTime ExpiresAt);

/// <summary>Una vinculación del centro que sigue abierta: sin canjear (Redeemed* nulos) o canjeada y pendiente de confirmar.</summary>
public sealed record AccountLinkSummary(
    Guid Id, SystemProfile Profile, IReadOnlyList<string> UnitNames, DateTime IssuedAt, DateTime ExpiresAt,
    string? RedeemedBySubject, string? RedeemedByName, DateTime? RedeemedAt)
{
    public bool IsRedeemed => RedeemedAt is not null;
}

/// <summary>Lo que se le enseña a la persona antes de aceptar: de qué centro es, qué perfil y en qué unidades.</summary>
public sealed record LiveAccountLink(Guid Id, string CenterName, SystemProfile Profile, IReadOnlyList<string> UnitNames, DateTime ExpiresAt);

/// <summary>
/// Vincular una cuenta que ya existe en otro centro (ADR 0007, I2b, script 0065). La Administración del centro emite la vinculación sin
/// decir a qué cuenta; la persona la canjea ya autenticada con su segundo factor; y la Administración la confirma o la rechaza. Hasta
/// confirmarla no existe ningún ámbito de perfil. Cada emisión, anulación y resolución queda en la auditoría administrativa del centro, y
/// el canje, en el registro de accesos.
/// </summary>
public interface IAccountLinkRepository
{
    /// <summary>Solo perfiles de personal y unidades del ámbito de quien emite.</summary>
    Task<IssuedAccountLink> IssueAsync(
        AccountAdministrationAccess access, SystemProfile profile, IReadOnlyList<UnitId> units, string tokenHash, DateTime now,
        CancellationToken ct = default);

    /// <summary>Las del centro sin canjear y vivas, y las canjeadas pendientes de confirmar.</summary>
    Task<IReadOnlyList<AccountLinkSummary>> ListOpenAsync(AccountAdministrationAccess access, DateTime now, CancellationToken ct = default);

    /// <summary>Anula una sin canjear. Una canjeada se rechaza, no se anula.</summary>
    Task CancelAsync(AccountAdministrationAccess access, Guid linkId, DateTime now, CancellationToken ct = default);

    Task<LiveAccountLink?> FindLiveAsync(string tokenHash, DateTime now, CancellationToken ct = default);

    /// <summary>La persona la canjea. Falla si ya no vale, si la cuenta no está activa o no tiene segundo factor, si es la de quien la
    /// emitió o si ya tiene ese perfil en el centro.</summary>
    Task RedeemAsync(AccountId accountId, Guid linkId, DateTime now, CancellationToken ct = default);

    /// <summary>La Administración la confirma (crea el perfil y sus unidades; devuelve el ámbito creado) o la rechaza (devuelve null).</summary>
    Task<Guid?> ResolveAsync(AccountAdministrationAccess access, Guid linkId, bool confirm, DateTime now, CancellationToken ct = default);
}
