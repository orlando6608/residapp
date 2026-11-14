using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Pdf;

/// <summary>
/// DER-05: PDF del informe de derivación, generado al firmar con QuestPDF (licencia Community: ver docs/privacidad/antes-de-datos-reales.md,
/// apartado 12). Muestra las secciones tal como se firmaron, marcando cuáles son datos automáticos y cuáles escribió el
/// profesional, y cierra con la firma electrónica simple: quién, cuándo (hora local del servidor, la misma
/// con la que se muestran las horas en la aplicación) y la huella SHA-256 del contenido.
/// </summary>
public sealed class ReferralReportPdfRenderer : IReferralReportPdfRenderer
{
    private const string FontFamily = "Liberation Sans";

    /// <summary>Licencia Community de QuestPDF: gratuita por debajo de 1 millón de dólares de facturación anual y no para empresas que cotizan en
    /// bolsa ni para el sector público. Fijarla es una declaración de quien explota ResidApp: la sociedad titular debe comprobarla antes del primer
    /// centro real (apartado 12 de docs/privacidad/antes-de-datos-reales.md). QuestPDF solo usa las fuentes registradas (no las del sistema), así que el PDF sale igual en
    /// Windows y en Linux (Azure y CI): Liberation Sans, incrustada.</summary>
    static ReferralReportPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        FontManager.RegisterFontFromBinaryData(PdfBranding.EmbeddedResource("ResidApp.Fonts.LiberationSans-Regular.ttf"));
        FontManager.RegisterFontFromBinaryData(PdfBranding.EmbeddedResource("ResidApp.Fonts.LiberationSans-Bold.ttf"));
    }

    public byte[] Render(
        string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature, ReferralReportBranding? branding = null)
    {
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(2, Unit.Centimetre);
            page.MarginTop(1, Unit.Centimetre);
            page.MarginBottom(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontFamily(FontFamily).FontSize(10));

            page.Header().PaddingBottom(0.6f, Unit.Centimetre).Element(header => PdfBranding.ComposeHeader(header, branding));

            page.Footer().DefaultTextStyle(style => style.FontSize(8)).Column(footer =>
            {
                footer.Item().Text(text =>
                {
                    text.Span($"Informe de derivación a Urgencias · {residentDisplayName} · página ");
                    text.CurrentPageNumber();
                    text.Span(" de ");
                    text.TotalPages();
                });
                PdfBranding.ComposeGeneratedBy(footer, branding);
            });

            page.Content().Column(column =>
            {
                column.Item().PaddingBottom(12).Text("Informe de derivación a Urgencias").FontSize(16).Bold();

                foreach (var block in content.Sections)
                {
                    column.Item().PaddingTop(8).EnsureSpace(60).Text(text =>
                    {
                        text.Span(block.Title).Bold();
                        text.Span(block.Automatic ? " · dato automático" : " · escrito por el profesional");
                    });
                    foreach (var line in block.Lines)
                    {
                        column.Item().Text(line);
                    }
                }

                column.Item().PaddingTop(16).BorderTop(0.5f).BorderColor(Colors.Grey.Medium).Text("Firma electrónica simple").Bold();
                // Sin nombre visible (cuentas anteriores a 0023 a las que nadie se lo ha puesto), solo perfil e identificador.
                var signer = signature.SignerName is { } name
                    ? $"{name} ({Profile(signature.Profile)}, cuenta {signature.SignerSubject})"
                    : $"{Profile(signature.Profile)} (cuenta {signature.SignerSubject})";
                column.Item().Text(
                    $"Firmado por {signer} " +
                    $"el {signature.SignedAt.ToLocalTime():dd'/'MM'/'yyyy} a las {signature.SignedAt.ToLocalTime():HH':'mm}.");
                column.Item().Text($"Huella SHA-256 del contenido: {signature.ContentHash}");
            });
        }))
        .WithMetadata(new DocumentMetadata
        {
            Title = "Informe de derivación a Urgencias",
            Author = branding?.CenterName ?? "ResidApp",
        });

        return document.GeneratePdf();
    }

    private static string Profile(SystemProfile profile) => profile == SystemProfile.Medicina ? "Medicina" : "Enfermería";
}
