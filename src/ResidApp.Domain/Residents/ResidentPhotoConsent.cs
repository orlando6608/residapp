using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Residents;

/// <summary>Quién firmó el consentimiento para la foto de identificación (CJ, foto 1.1).</summary>
public enum ResidentPhotoConsentSigner
{
    [Code("RESIDENTE")] [Display(Name = "El residente")] Residente,
    [Code("REPRESENTANTE")] [Display(Name = "Su representante")] Representante,
}

/// <summary>
/// El consentimiento que se guarda con la foto (CJ, foto 1.1; script 0071): quién lo firmó y cuándo. El documento firmado lo custodia el centro,
/// fuera de la app (minimización); esto basta para demostrar que consta (art. 7.1 RGPD). La fecha no puede ser futura.
/// </summary>
public sealed record ResidentPhotoConsent(ResidentPhotoConsentSigner Signer, DateOnly SignedOn)
{
    public static ResidentPhotoConsent Validate(ResidentPhotoConsentSigner? signer, DateOnly? signedOn, DateOnly today) =>
        signer is { } who && signedOn is { } date && date <= today
            ? new ResidentPhotoConsent(who, date)
            : throw new DomainValidationException(ResidentPhoto.InvalidCode);
}

/// <summary>Reglas de la foto que no son la imagen (CJ, foto 2.3): pasados 12 meses conviene renovarla, porque una foto antigua falla como
/// identificación.</summary>
public static class ResidentPhotoRenewal
{
    public const int Months = 12;

    public static bool IsDue(DateTimeOffset photoUpdatedAt, DateTimeOffset now) => photoUpdatedAt <= now.AddMonths(-Months);
}
