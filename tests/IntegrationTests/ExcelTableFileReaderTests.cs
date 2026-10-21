using System.IO.Compression;
using System.Text;
using ResidApp.Infrastructure.Intercambio;
using ResidApp.Shared;

namespace ResidApp.IntegrationTests;

/// <summary>ADR 0010: lectura de ficheros de importación (CSV con distintos separadores y codificaciones, y XLSX), con sus límites. No usa
/// la base de datos.</summary>
public class ExcelTableFileReaderTests
{
    private static readonly ExcelTableFileReader Reader = new();

    static ExcelTableFileReaderTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Csv_ConBomYPuntoYComa_SeLeeConLaCabeceraEnLaPrimeraFilaNoVacia()
    {
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(";;\r\nNombre;Unidad;\r\n Ana Pérez ;Norte;\r\n;;\r\nLuis;Sur\r\n")).ToArray();

        var table = Reader.Read(content);

        Assert.Equal(["Nombre", "Unidad"], table.Headers);
        Assert.Equal([(3, "Ana Pérez", "Norte"), (5, "Luis", "Sur")], table.Rows.Select(r => (r.Number, r.Cells[0], r.Cells[1])));
    }

    [Fact]
    public void Csv_ConComasYEnWindows1252_SeLeeConSusAcentos()
    {
        var content = Encoding.GetEncoding(1252).GetBytes("Nombre,Habitación\r\n\"Peña, Begoña\",101\r\n");

        var table = Reader.Read(content);

        Assert.Equal(["Nombre", "Habitación"], table.Headers);
        Assert.Equal(["Peña, Begoña", "101"], table.Rows.Single().Cells);
    }

    [Fact]
    public void Xlsx_ConFechaYNumero_SeLeeComoTexto_ConLaFechaAnioMesDia()
    {
        var table = Reader.Read(Xlsx(
            """<row r="1"><c r="A1" t="inlineStr"><is><t>Nombre</t></is></c><c r="B1" t="inlineStr"><is><t>F. Nac.</t></is></c><c r="C1" t="inlineStr"><is><t>Habitación</t></is></c></row>""",
            """<row r="2"><c r="A2" t="inlineStr"><is><t>Ana Pérez</t></is></c><c r="B2" s="1"><v>14675</v></c><c r="C2"><v>101</v></c></row>"""));

        Assert.Equal(["Nombre", "F. Nac.", "Habitación"], table.Headers);
        Assert.Equal(["Ana Pérez", DateTime.FromOADate(14675).ToString("yyyy-MM-dd"), "101"], table.Rows.Single().Cells);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    public void UnFicheroVacioDanadoOBinario_EsInvalido(byte[] content) =>
        Assert.Equal("EXCHANGE_FILE_INVALID", Assert.Throws<DomainValidationException>(() => Reader.Read(content)).Message);

    [Fact]
    public void UnCsvSoloConFilasVacias_EsInvalido() =>
        Assert.Equal("EXCHANGE_FILE_INVALID", Assert.Throws<DomainValidationException>(() => Reader.Read(Encoding.UTF8.GetBytes(";;\r\n;;\r\n"))).Message);

    [Fact]
    public void Pasar_De_Los_Limites_Es_Demasiado_Grande()
    {
        var tooManyRows = Encoding.UTF8.GetBytes("Nombre\r\n" + string.Concat(Enumerable.Repeat("Ana\r\n", 2001)));
        var tooManyColumns = Encoding.UTF8.GetBytes(string.Join(';', Enumerable.Range(1, 61).Select(i => $"C{i}")) + "\r\n");

        Assert.Equal("EXCHANGE_FILE_TOO_LARGE", Assert.Throws<DomainValidationException>(() => Reader.Read(tooManyRows)).Message);
        Assert.Equal("EXCHANGE_FILE_TOO_LARGE", Assert.Throws<DomainValidationException>(() => Reader.Read(tooManyColumns)).Message);
        Assert.Equal("EXCHANGE_FILE_TOO_LARGE", Assert.Throws<DomainValidationException>(() => Reader.Read(new byte[5 * 1024 * 1024 + 1])).Message);
    }

    /// <summary>Un XLSX mínimo con una hoja; el estilo 1 es de fecha (numFmtId 14).</summary>
    private static byte[] Xlsx(params string[] rows)
    {
        const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string officeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var parts = new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """
                <?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
                """,
            ["_rels/.rels"] = $"""<Relationships xmlns="{relationships}"><Relationship Id="rId1" Type="{officeRelationships}/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = $"""<workbook xmlns="{main}" xmlns:r="{officeRelationships}"><sheets><sheet name="Hoja1" sheetId="1" r:id="rId1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = $"""
                <Relationships xmlns="{relationships}"><Relationship Id="rId1" Type="{officeRelationships}/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="{officeRelationships}/styles" Target="styles.xml"/></Relationships>
                """,
            ["xl/styles.xml"] = $"""<styleSheet xmlns="{main}"><cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14" applyNumberFormat="1"/></cellXfs></styleSheet>""",
            ["xl/worksheets/sheet1.xml"] = $"""<worksheet xmlns="{main}"><sheetData>{string.Concat(rows)}</sheetData></worksheet>""",
        };
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, xml) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
        }

        return buffer.ToArray();
    }
}
