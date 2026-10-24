using ResidApp.Domain.Enfermeria;
using ResidApp.Domain.Platform;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>DER-05: firma electrónica simple que figura en el PDF: quién firma (nombre visible de la cuenta, si lo tiene
/// (0023), perfil e identificador de la cuenta), cuándo, y la huella del contenido firmado.</summary>
public sealed record ReferralReportSignature(
    SystemProfile Profile, string SignerSubject, DateTimeOffset SignedAt, string ContentHash, string? SignerName = null);

/// <summary>La marca del centro que firma, para la cabecera del PDF (script 0068): su nombre y su logo propio, si lo tiene (sin él, el icono de
/// ResidApp). Arriba van el nombre y el logo del centro; ResidApp queda al pie.</summary>
public sealed record ReferralReportBranding(string CenterName, CenterLogoImage? Logo);

/// <summary>DER-05: genera el PDF del informe de derivación al firmarlo. Sin marca de centro, la cabecera es la de ResidApp.</summary>
public interface IReferralReportPdfRenderer
{
    byte[] Render(
        string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature, ReferralReportBranding? branding = null);
}

/// <summary>DER-05: el PDF firmado de un evento, para descargarlo.</summary>
public sealed record ReferralReportPdf(byte[] Content, DateTimeOffset SignedAt);

/// <summary>DER-05: descarga del PDF firmado desde cualquiera de los dos perfiles que ven el evento en su
/// ámbito (con un ámbito de Medicina, solo eventos escalados). Cada descarga deja su fila en
/// dbo.eventos_auditoria (REFERRAL_REPORT_DOWNLOAD) con el perfil que descarga. Null si el evento no está en
/// el ámbito o no tiene informe.</summary>
public interface IReferralReportRepository
{
    Task<ReferralReportPdf?> DownloadAsync(
        AccountId accountId, SystemProfile profile, Guid profileScopeId, CenterId centerId, Guid eventId, CancellationToken ct = default);

    /// <summary>DIR-12: la descarga de Dirección Clínica. Exige en SQL el ámbito de Dirección activo, el permiso clínico, el evento de ese residente
    /// en sus unidades y la declaración de acceso vigente (de la que toma la finalidad y la justificación que audita); cada descarga deja su
    /// fila CLINICAL_DETAIL_READ sobre REFERRAL_REPORT en la misma transacción. Null si algo de eso falla.</summary>
    Task<ReferralReportPdf?> DownloadAsDirectionAsync(DirectionReferralDownloadInput input, CancellationToken ct = default);
}

/// <summary>DIR-12: lo que identifica la descarga del informe de un evento por Dirección Clínica; todo sale de una autorización ya resuelta.</summary>
public sealed record DirectionReferralDownloadInput(
    AccountId AccountId, Guid ProfileScopeId, CenterId CenterId, ResidentId ResidentId, Guid EventId, Guid DeclarationId);
