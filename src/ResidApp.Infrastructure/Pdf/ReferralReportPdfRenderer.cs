using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Pdf;

/// <summary>
/// DER-05: PDF del informe de derivación, generado al firmar con MigraDoc (PDFsharp-MigraDoc, MIT). Muestra
/// las secciones tal como se firmaron, marcando cuáles son datos automáticos y cuáles escribió el
/// profesional, y cierra con la firma electrónica simple: quién, cuándo (hora local del servidor, la misma
/// con la que se muestran las horas en la aplicación) y la huella SHA-256 del contenido.
/// </summary>
public sealed class ReferralReportPdfRenderer : IReferralReportPdfRenderer
{
    private const string FontFamily = "Liberation Sans";

    /// <summary>PDFsharp/MigraDoc comparten estado de fuentes entre documentos: dos PDF generados a la vez pueden salir
    /// con palabras de un título en la fuente que no toca (visto en las pruebas en paralelo). Se generan de uno en uno.</summary>
    private static readonly Lock RenderLock = new();

    static ReferralReportPdfRenderer() => GlobalFontSettings.FontResolver = new EmbeddedFontResolver();

    public byte[] Render(string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature)
    {
        lock (RenderLock)
        {
            return RenderDocument(residentDisplayName, content, signature);
        }
    }

    private static byte[] RenderDocument(string residentDisplayName, ReferralReportContent content, ReferralReportSignature signature)
    {
        var document = new Document();
        document.Info.Title = "Informe de derivación a Urgencias";
        document.Info.Author = "ResidApp";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontFamily;
        normal.Font.Size = 10;

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(2);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.AddText($"Informe de derivación a Urgencias · {residentDisplayName} · página ");
        footer.AddPageField();
        footer.AddText(" de ");
        footer.AddNumPagesField();

        var title = section.AddParagraph("Informe de derivación a Urgencias");
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(12);

        foreach (var block in content.Sections)
        {
            var heading = section.AddParagraph();
            heading.Format.SpaceBefore = Unit.FromPoint(8);
            heading.Format.KeepWithNext = true;
            heading.AddFormattedText(block.Title, TextFormat.Bold);
            heading.AddText(block.Automatic ? " · dato automático" : " · escrito por el profesional");
            foreach (var line in block.Lines)
            {
                section.AddParagraph(line);
            }
        }

        var signed = section.AddParagraph();
        signed.Format.SpaceBefore = Unit.FromPoint(16);
        signed.Format.Borders.Top.Width = 0.5;
        signed.Format.Borders.Top.Color = Colors.Gray;
        signed.AddFormattedText("Firma electrónica simple", TextFormat.Bold);
        // Sin nombre visible (cuentas anteriores a 0023 a las que nadie se lo ha puesto), solo perfil e identificador.
        var signer = signature.SignerName is { } name
            ? $"{name} ({Profile(signature.Profile)}, cuenta {signature.SignerSubject})"
            : $"{Profile(signature.Profile)} (cuenta {signature.SignerSubject})";
        section.AddParagraph(
            $"Firmado por {signer} " +
            $"el {signature.SignedAt.ToLocalTime():dd'/'MM'/'yyyy} a las {signature.SignedAt.ToLocalTime():HH':'mm}.");
        section.AddParagraph($"Huella SHA-256 del contenido: {signature.ContentHash}");

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static string Profile(SystemProfile profile) => profile == SystemProfile.Medicina ? "Medicina" : "Enfermería";

    /// <summary>Toda familia se resuelve a Liberation Sans incrustada (normal o negrita; la cursiva se simula),
    /// para que el PDF salga igual en Windows y en Linux, donde PDFsharp no lee fuentes del sistema.</summary>
    private sealed class EmbeddedFontResolver : IFontResolver
    {
        private const string Regular = "LiberationSans-Regular";
        private const string Bold = "LiberationSans-Bold";

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? Bold : Regular, false, isItalic);

        public byte[] GetFont(string faceName)
        {
            using var resource = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream($"ResidApp.Fonts.{faceName}.ttf")
                ?? throw new InvalidOperationException($"Falta la fuente incrustada {faceName}.");
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            return bytes.ToArray();
        }
    }
}
