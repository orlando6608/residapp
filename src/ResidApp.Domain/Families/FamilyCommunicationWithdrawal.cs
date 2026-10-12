using ResidApp.Shared;

namespace ResidApp.Domain.Families;

/// <summary>
/// Retirada excepcional de un comunicado ya publicado (portal-familiar.md, historia 3; script 0053). Hace falta un motivo, que es interno: la
/// familia solo ve que el contenido ya no está disponible, y el original se conserva.
/// </summary>
public static class FamilyCommunicationWithdrawal
{
    public const int MaxReasonLength = 500;
    public const string InvalidCode = "FAMILY_COMMUNICATION_WITHDRAWAL_INVALID";

    /// <summary>El motivo sin espacios sobrantes; si falta o se pasa de largo, FAMILY_COMMUNICATION_WITHDRAWAL_INVALID.</summary>
    public static string NormalizeReason(string? reason) =>
        reason?.Trim() is { Length: > 0 and <= MaxReasonLength } trimmed ? trimmed : throw new DomainValidationException(InvalidCode);
}
