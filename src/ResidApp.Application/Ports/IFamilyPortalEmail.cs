using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>
/// Si la familia entra en el portal con un código que le llega por correo (ADR 0012, pieza 3). Con él, la dirección de la ficha pasa a dar acceso: es obligatoria para dar el
/// acceso e invitar, y cambiarla tiene efectos. Apagado, todo es como si no existiera.
/// </summary>
public sealed record FamilyAccessPolicy(bool EmailCode)
{
    public static FamilyAccessPolicy Off { get; } = new(false);
}

/// <summary>Códigos de error de esta pieza.</summary>
public static class FamilyAccessCodes
{
    /// <summary>Dar acceso o invitar a un familiar sin correo en su ficha: no podría entrar.</summary>
    public const string EmailRequired = "FAMILY_EMAIL_REQUIRED";
}

/// <summary>Se ha cambiado o borrado la dirección con la que entraba un familiar: hay que avisar a la anterior. La dirección solo viaja para enviar; no se guarda.</summary>
public sealed record FamilyEmailChanged(Guid FamilyId, string PreviousAddress);

/// <summary>El aviso de cambio de correo ya resuelto, para el registro de envíos. Sin direcciones.</summary>
public sealed record FamilyEmailChangeEntry(
    CenterId CenterId, Guid FamilyId, string TemplateVersion, string Language, string State, string? Reason, string? OperationId, int Attempts, DateTime At);

public interface IFamilyEmailChangeLog
{
    Task RecordAsync(FamilyEmailChangeEntry entry, CancellationToken ct = default);
}

/// <summary>Qué pasó con el aviso a la dirección anterior, para decírselo a Administración.</summary>
public enum FamilyEmailNotice
{
    NotNeeded,
    Sent,
    NotSent,
}
