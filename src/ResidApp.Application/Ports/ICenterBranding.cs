using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>La marca de un centro: su nombre visible y, si tiene logo propio, cuándo se cambió por última vez (la versión que lleva la
/// dirección de la imagen para que el navegador no guarde una vieja).</summary>
public sealed record CenterBranding(string Name, DateTimeOffset? LogoUpdatedAt);

/// <summary>
/// Lectura de la marca del centro (script 0068). Las dos primeras lecturas parten del ámbito de perfil de la cookie de ámbito y del sujeto
/// de la sesión: la cookie no es de fiar (ADR 0008), así que el ámbito solo vale si pertenece a la cuenta de ese sujeto, está vigente y no
/// es de Plataforma (que no tiene marca propia, sino la de ResidApp). Si no vale, devuelven null y quien llama pinta la marca de ResidApp.
/// Las otras dos son para quien ya sabe, por su propia comprobación de acceso, de qué centro se trata (la firma de un informe).
/// </summary>
public interface ICenterBrandingDirectory
{
    Task<CenterBranding?> FindForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default);

    /// <summary>El logo del centro del ámbito; null si el ámbito no vale o el centro no tiene logo.</summary>
    Task<CenterLogoImage?> ReadLogoForScopeAsync(string externalSubject, Guid profileScopeId, CancellationToken ct = default);

    Task<CenterBranding?> FindAsync(CenterId centerId, CancellationToken ct = default);

    Task<CenterLogoImage?> ReadLogoAsync(CenterId centerId, CancellationToken ct = default);
}

/// <summary>
/// Escrituras de la marca de un centro por su Administración. Cada una va en una transacción que comprueba de nuevo el ámbito de
/// Administración, bloquea la fila del centro y deja su evento (CENTER_RENAME, CENTER_LOGO_SET o CENTER_LOGO_REMOVE) en
/// dbo.eventos_auditoria, sin la imagen.
/// </summary>
public interface ICenterBrandingRepository
{
    /// <summary>Cambia el nombre visible del centro. Si es el que ya tenía: CENTER_INVALID.</summary>
    Task RenameAsync(AccountAdministrationAccess access, string name, CancellationToken ct = default);

    /// <summary>Pone o sustituye el logo.</summary>
    Task SetLogoAsync(AccountAdministrationAccess access, CenterLogoImage logo, CancellationToken ct = default);

    /// <summary>Devuelve false si el centro no tenía logo (y entonces no audita nada).</summary>
    Task<bool> RemoveLogoAsync(AccountAdministrationAccess access, CancellationToken ct = default);
}
