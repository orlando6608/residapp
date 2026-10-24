using ResidApp.Domain.Accounts;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>Lo que hace falta para la hoja de la invitación recién emitida (el token no se guarda: lo pone quien la emite).</summary>
public sealed record IssuedAccessInvitation(AccessInvitationKind Kind, string Subject, string? DisplayName, string CenterName, DateTime ExpiresAt);

/// <summary>LiveExpiresAt (UTC) es null si la cuenta no tiene ninguna invitación viva.</summary>
public sealed record AccessInvitationStatus(bool HasPassword, DateTime? LiveExpiresAt);

/// <summary>Una invitación sin usar, sin anular y sin caducar, de una cuenta activa. Las fechas son UTC.</summary>
public sealed record LiveAccessInvitation(Guid Id, AccountId AccountId, AccessInvitationKind Kind, DateTime ExpiresAt);

/// <summary>
/// Invitaciones de acceso de un solo uso (script 0063, ADR 0007 I2). Emitir anula la anterior de la cuenta. Administración emite para las
/// cuentas con perfiles solo en su centro (personal o familiar), nunca para la propia; Plataforma, para una cuenta de Administración de
/// un centro. El tipo lo decide la base: recuperación si la cuenta ya tiene contraseña, alta si no. Cada emisión y anulación queda en la
/// auditoría administrativa.
/// </summary>
public interface IAccessInvitationRepository
{
    Task<IssuedAccessInvitation> IssueAsync(
        AccountAdministrationAccess access, string subject, string tokenHash, DateTime now, CancellationToken ct = default);

    Task<IssuedAccessInvitation> IssueForAdministratorAsync(
        PlatformAccess access, CenterId centerId, Guid administrationScopeId, string tokenHash, DateTime now, CancellationToken ct = default);

    /// <summary>I2b: la invitación (de recuperación, si ya tiene contraseña) de una cuenta con perfiles en varios centros, que ninguna
    /// Administración entrega. Nunca la del operador.</summary>
    Task<IssuedAccessInvitation> IssueForMultiCenterAsync(PlatformAccess access, AccountId accountId, string tokenHash, DateTime now, CancellationToken ct = default);

    Task CancelAsync(AccountAdministrationAccess access, string subject, DateTime now, CancellationToken ct = default);

    /// <summary>Para la ficha de la cuenta: si ya tiene contraseña (la invitación sería de recuperación) y cuándo caduca la viva.</summary>
    Task<AccessInvitationStatus> FindStatusAsync(AccountAdministrationAccess access, string subject, DateTime now, CancellationToken ct = default);

    Task<LiveAccessInvitation?> FindLiveAsync(string tokenHash, DateTime now, CancellationToken ct = default);

    /// <summary>La marca como usada si sigue viva; false si otra petición se adelantó o ya no vale.</summary>
    Task<bool> ConsumeAsync(Guid invitationId, DateTime now, CancellationToken ct = default);
}
