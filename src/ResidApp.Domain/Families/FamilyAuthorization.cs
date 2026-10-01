using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Families;

/// <summary>FAM-01 (script 0022): estado de la autorización de acceso de un familiar a un residente. Solo Activa
/// permitirá entrar al Portal Familiar. Caducada no se guarda: es una Activa cuya fecha «válida hasta» ya pasó.</summary>
public enum FamilyAuthorizationStatus
{
    [Code("PENDIENTE")] [Display(Name = "Pendiente")] Pendiente,
    [Code("ACTIVA")] [Display(Name = "Activa")] Activa,
    [Code("SUSPENDIDA")] [Display(Name = "Suspendida")] Suspendida,
    [Code("REVOCADA")] [Display(Name = "Revocada")] Revocada,
    [Code("CADUCADA")] [Display(Name = "Caducada")] Caducada,
}

/// <summary>ADM-10/ADM-11: lo que Administración puede hacer con una autorización.</summary>
public enum FamilyAuthorizationChange
{
    [Display(Name = "Abrir autorización")] Abrir,
    [Display(Name = "Activar")] Activar,
    [Display(Name = "Suspender")] Suspender,
    [Display(Name = "Revocar")] Revocar,
}

/// <summary>
/// ADM-10/ADM-11 (decisión del usuario, 2026-10-01): Pendiente → Activa → Suspendida / Revocada / Caducada.
/// <list type="bullet">
/// <item>Abrir crea la autorización en Pendiente; crear o vincular un familiar no la abre (FAM-01).</item>
/// <item>Activar vale desde Pendiente, desde Suspendida o para renovar una Caducada, con «válida hasta» opcional (hoy o
/// después; el último día incluido).</item>
/// <item>Suspender (solo una Activa vigente) y Revocar piden motivo y surten efecto al momento. Revocada es final.</item>
/// </list>
/// TR_fac_transition repite en la base de datos las transiciones que no dependen del día.
/// </summary>
public static class FamilyAuthorizationRules
{
    public const int MaxReasonLength = 1000;
    public const string InvalidCode = "FAMILY_AUTHORIZATION_CHANGE_INVALID";

    /// <summary>El estado que cuenta hoy: una Activa con «válida hasta» anterior a hoy está Caducada.</summary>
    public static FamilyAuthorizationStatus Effective(FamilyAuthorizationStatus stored, DateOnly? validUntil, DateOnly today) =>
        stored == FamilyAuthorizationStatus.Activa && validUntil < today ? FamilyAuthorizationStatus.Caducada : stored;

    /// <summary>Los cambios posibles desde el estado efectivo; null es «sin autorización».</summary>
    public static IReadOnlyList<FamilyAuthorizationChange> Allowed(FamilyAuthorizationStatus? effective) => effective switch
    {
        null => [FamilyAuthorizationChange.Abrir],
        FamilyAuthorizationStatus.Pendiente => [FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar],
        FamilyAuthorizationStatus.Activa => [FamilyAuthorizationChange.Suspender, FamilyAuthorizationChange.Revocar],
        FamilyAuthorizationStatus.Suspendida => [FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar],
        FamilyAuthorizationStatus.Caducada => [FamilyAuthorizationChange.Activar, FamilyAuthorizationChange.Revocar],
        _ => [],
    };

    /// <summary>Valida el cambio desde el estado efectivo y devuelve lo que se guarda: estado, «válida hasta» (solo al
    /// activar) y motivo (solo al suspender o revocar).</summary>
    public static (FamilyAuthorizationStatus Status, DateOnly? ValidUntil, string? Reason) Validate(
        FamilyAuthorizationStatus? effective, FamilyAuthorizationChange change, DateOnly? validUntil, string? reason, DateOnly today)
    {
        var reasonText = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var valid = Allowed(effective).Contains(change) && change switch
        {
            FamilyAuthorizationChange.Abrir => validUntil is null && reasonText is null,
            FamilyAuthorizationChange.Activar => (validUntil is null || validUntil >= today) && reasonText is null,
            _ => validUntil is null && reasonText is not null && reasonText.Length <= MaxReasonLength,
        };
        if (!valid)
        {
            throw new DomainValidationException(InvalidCode);
        }

        return change switch
        {
            FamilyAuthorizationChange.Abrir => (FamilyAuthorizationStatus.Pendiente, null, null),
            FamilyAuthorizationChange.Activar => (FamilyAuthorizationStatus.Activa, validUntil, null),
            FamilyAuthorizationChange.Suspender => (FamilyAuthorizationStatus.Suspendida, null, reasonText),
            _ => (FamilyAuthorizationStatus.Revocada, null, reasonText),
        };
    }
}
