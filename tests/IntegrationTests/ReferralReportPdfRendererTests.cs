using PdfSharp.Pdf.IO;
using ResidApp.Application.Ports;
using ResidApp.Domain.Enfermeria;
using ResidApp.Infrastructure.Pdf;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>DER-05: el PDF de derivación sale igual aunque se generen varios a la vez. Sin serializar, PDFsharp/MigraDoc
/// mezclaban fuentes entre documentos y una palabra de un título salía sin negrita.</summary>
public class ReferralReportPdfRendererTests
{
    [Fact]
    public void Generados_ALaVez_TienenElMismoContenidoQueUnoSolo()
    {
        var content = ReferralReportContent.Compose(
            [
                new("Identificación del residente y del centro", true, ["Nombre: Residente de prueba"]),
                new("Evolución", true, ["Sin datos registrados."]),
            ],
            new ReferralReportInput("Desaturación que no remonta con oxigenoterapia.", null));
        var signature = new ReferralReportSignature(
            SystemProfile.Enfermeria, "test-pdf", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "huella", "Ana Ruiz Peña (ficticia)");
        string Render() => PageContent(new ReferralReportPdfRenderer().Render("Residente Valoración Enfermería", content, signature));

        var expected = Render();
        var rendered = new string[64];
        Parallel.For(0, rendered.Length, i => rendered[i] = Render());

        Assert.All(rendered, page => Assert.Equal(expected, page));
    }

    /// <summary>El encabezado lleva el icono de la app: la primera página dibuja una imagen.</summary>
    [Fact]
    public void Encabezado_LlevaElIconoDeLaApp()
    {
        var content = ReferralReportContent.Compose(
            [new("Evolución", true, ["Sin datos registrados."])],
            new ReferralReportInput("Motivo de prueba.", null));
        var signature = new ReferralReportSignature(
            SystemProfile.Medicina, "test-pdf", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "huella", null);

        using var document = PdfReader.Open(
            new MemoryStream(new ReferralReportPdfRenderer().Render("Residente de prueba", content, signature)), PdfDocumentOpenMode.Import);
        var xObjects = document.Pages[0].Resources.Elements.GetDictionary("/XObject");

        Assert.NotNull(xObjects);
        Assert.Contains(xObjects.Elements.Values,
            item => item is PdfSharp.Pdf.Advanced.PdfReference { Value: PdfSharp.Pdf.PdfDictionary image }
                && image.Elements.GetName("/Subtype") == "/Image");
    }

    /// <summary>El contenido de la primera página sin comprimir: texto, fuentes y posiciones, sin la fecha ni el id del
    /// documento, que cambian en cada PDF.</summary>
    private static string PageContent(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var contents = document.Pages[0].Contents;
        return string.Concat(Enumerable.Range(0, contents.Elements.Count)
            .Select(i => Convert.ToHexString(contents.Elements.GetDictionary(i)!.Stream!.UnfilteredValue)));
    }
}
