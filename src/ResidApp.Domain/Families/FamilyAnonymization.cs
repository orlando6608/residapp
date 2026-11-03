using System.ComponentModel.DataAnnotations;
using ResidApp.Shared;

namespace ResidApp.Domain.Families;

/// <summary>Quién pide que se borren los datos de un familiar (script 0087). Es todo lo que se guarda de la petición: ni quién la hizo, ni su documento, ni un texto libre;
/// eso va en el registro de derechos del centro. Lista propuesta por el agente DPO (docs/privacidad/expurgo-y-supresion.md, 3.5).</summary>
public enum FamilyAnonymizationReason
{
    [Code("PETICION_DEL_INTERESADO")] [Display(Name = "La propia persona")] PeticionDelInteresado,
    [Code("PETICION_DE_REPRESENTANTE")] [Display(Name = "Su representante")] PeticionDeRepresentante,
    [Code("PETICION_DE_ALLEGADOS_O_HEREDEROS")] [Display(Name = "Sus allegados o herederos (ha fallecido)")] PeticionDeAllegadosOHerederos,
    [Code("INICIATIVA_DEL_CENTRO")] [Display(Name = "El centro (alta por error u otra razón del centro)")] IniciativaDelCentro,
}

/// <summary>Anonimizar a un familiar (script 0087; Orlando, 2026-11-03): se borran su nombre, teléfono y correo y se cierra su acceso, pero la fila y los vínculos se conservan porque
/// citas, autorizaciones y auditoría apuntan a ellos. No es anonimato total mientras exista su identificador de acceso y su vínculo con el residente: es seudonimización.
/// Los textos fijos son lo único que queda escrito; ninguno lleva datos de la persona.</summary>
public static class FamilyAnonymization
{
    public const string Name = "Persona anonimizada";
    public const string Phone = "-";

    /// <summary>Motivo que consta en las autorizaciones que se revocan: fijo, porque queda inmutable y nunca debe llevar lo que escriba quien anonimiza.</summary>
    public const string AuthorizationRevocationReason = "Anonimización del familiar";

    public const string InvalidCode = "FAMILY_ANONYMIZATION_INVALID";
    public const string NotPrincipalCode = "FAMILY_ANONYMIZATION_NOT_PRINCIPAL";
    public const string BlockedCode = "FAMILY_ANONYMIZATION_BLOCKED";
    public const string AlreadyCode = "FAMILY_ANONYMIZATION_ALREADY";

    /// <summary>Comprueba el motivo de la lista cerrada y que quien anonimiza confirmó que comprobó la identidad y que no se puede deshacer.</summary>
    public static FamilyAnonymizationReason Validate(FamilyAnonymizationReason? reason, bool confirmed) =>
        confirmed && reason is { } value && Enum.IsDefined(value) ? value : throw new DomainValidationException(InvalidCode);
}
