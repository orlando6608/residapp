using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Application.Ports;

/// <summary>DER-05: firma electrónica simple que figura en el PDF: quién firma (nombre visible de la cuenta, si lo tiene
/// (0023), perfil e identificador de la cuenta), cuándo, y la huella del contenido firmado.</summary>
public sealed record ReferralReportSignature(
    SystemProfile Profile, string SignerSubject, DateTimeOffset SignedAt, string ContentHash, string? SignerName = null);

/// <summary>DER-05: genera el PDF del informe de derivación al firmarlo.</summary>
public interface IReferralReportPdfRenderer
{
    byte[] Render(string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature);
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
}
