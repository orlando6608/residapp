using System.Globalization;
using System.Text;
using ExcelDataReader;
using ResidApp.Application.Intercambio;
using ResidApp.Application.Ports;
using ResidApp.Shared;

namespace ResidApp.Infrastructure.Intercambio;

/// <summary>
/// ADR 0010: lee la primera hoja de un CSV, XLS o XLSX con ExcelDataReader, todo en memoria y sin guardar nada. El tipo se reconoce por
/// los primeros bytes (ZIP para XLSX, OLE para XLS; si no hay bytes nulos, texto CSV), nunca por la extensión ni por el Content-Type. Un
/// CSV sin BOM se lee como UTF-8 y, si no lo es, como Windows-1252 (lo habitual en exportaciones antiguas); el separador se detecta.
/// La cabecera es la primera fila no vacía; las filas vacías se saltan; las celdas de fecha se escriben año-mes-día y los números, sin
/// decimales sobrantes. Los macros de un fichero no se ejecutan nunca: ExcelDataReader solo lee valores.
/// </summary>
public sealed class ExcelTableFileReader : ITableFileReader
{
    /// <summary>Cuántas celdas de una fila se miran como mucho: una hoja con formato en miles de columnas vacías no se recorre entera.</summary>
    private const int MaxCellsRead = 256;

    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Utf16Le = [0xFF, 0xFE];
    private static readonly byte[] Utf16Be = [0xFE, 0xFF];

    static ExcelTableFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public ExchangeTable Read(byte[] content)
    {
        if (content.Length > ExchangeLimits.MaxBytes)
        {
            throw new DomainValidationException("EXCHANGE_FILE_TOO_LARGE");
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = content.AsSpan().StartsWith(Zip) ? ExcelReaderFactory.CreateOpenXmlReader(stream)
                : content.AsSpan().StartsWith(Ole) ? ExcelReaderFactory.CreateBinaryReader(stream)
                : LooksLikeText(content) ? ExcelReaderFactory.CreateCsvReader(stream, new ExcelReaderConfiguration
                {
                    FallbackEncoding = Encoding.GetEncoding(1252),
                    AutodetectSeparators = [';', ',', '\t', '|'],
                })
                : throw new DomainValidationException("EXCHANGE_FILE_INVALID");
            return ReadFirstSheet(reader);
        }
        catch (DomainValidationException)
        {
            throw;
        }
        catch (Exception)
        {
            // Un fichero dañado o que no es lo que parece: ExcelDataReader lanza excepciones de muchos tipos.
            throw new DomainValidationException("EXCHANGE_FILE_INVALID");
        }
    }

    private static ExchangeTable ReadFirstSheet(IExcelDataReader reader)
    {
        string[]? headers = null;
        var rows = new List<ExchangeTableRow>();
        var number = 0;
        while (reader.Read())
        {
            number++;
            var cells = Enumerable.Range(0, Math.Min(reader.FieldCount, MaxCellsRead)).Select(i => Text(reader.GetValue(i))).ToArray();
            var used = Array.FindLastIndex(cells, c => c.Length > 0) + 1;
            if (used == 0)
            {
                continue;
            }

            if (headers is null)
            {
                if (used > ExchangeLimits.MaxColumns)
                {
                    throw new DomainValidationException("EXCHANGE_FILE_TOO_LARGE");
                }

                headers = cells.Take(used).Select((h, i) => h.Length > 0 ? h : $"Columna {i + 1}").ToArray();
                continue;
            }

            if (rows.Count == ExchangeLimits.MaxRows)
            {
                throw new DomainValidationException("EXCHANGE_FILE_TOO_LARGE");
            }

            // Las celdas sin cabecera no se pueden emparejar con nada: se descartan al leer.
            rows.Add(new ExchangeTableRow(number, Enumerable.Range(0, headers.Length).Select(i => i < cells.Length ? cells[i] : "").ToArray()));
        }

        return headers is null ? throw new DomainValidationException("EXCHANGE_FILE_INVALID") : new ExchangeTable(headers, rows);
    }

    private static string Text(object? value) => value switch
    {
        null => "",
        string text => text.Trim(),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double number when number == Math.Floor(number) && Math.Abs(number) < 1e15 => ((long)number).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture).Trim(),
        _ => value.ToString()?.Trim() ?? "",
    };

    /// <summary>Texto: empieza con un BOM de UTF-16 o no tiene bytes nulos en los primeros 8 KB.</summary>
    private static bool LooksLikeText(byte[] content) =>
        content.Length > 0
        && (content.AsSpan().StartsWith(Utf16Le) || content.AsSpan().StartsWith(Utf16Be)
            || content.AsSpan(0, Math.Min(content.Length, 8192)).IndexOf((byte)0) < 0);
}
