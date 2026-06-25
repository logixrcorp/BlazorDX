using System.Globalization;
using System.Net;
using System.Text;

namespace BlazorDX.MockReportServer;

/// <summary>
/// The fully-resolved request to render: the report, the validated parameter
/// values (multi-value preserved), and the device-info flags that affect chrome.
/// </summary>
/// <param name="Report">The report being rendered.</param>
/// <param name="Values">Resolved parameter values keyed by name; defaults already applied.</param>
/// <param name="Rows">The deterministic data the format will render.</param>
/// <param name="Toolbar">Honors <c>rc:Toolbar</c> — include the toolbar chrome in HTML.</param>
/// <param name="ShowParameters">Honors <c>rc:Parameters</c> — show the parameter panel in HTML.</param>
public sealed record RenderRequest(
    ReportDefinition Report,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    IReadOnlyList<ReportRow> Rows,
    bool Toolbar,
    bool ShowParameters);

/// <summary>A rendered report: the bytes, the MIME type, and a download file name.</summary>
public sealed record RenderResult(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Turns a <see cref="RenderRequest"/> into bytes for each supported SSRS
/// <c>rs:Format</c>. Output is deterministic and reflects the supplied
/// parameters so tests can prove the values flowed end to end.
/// </summary>
public static class ReportRenderer
{
    /// <summary>The SSRS format names this mock recognises, mapped to a renderer.</summary>
    public static bool IsSupportedFormat(string format) =>
        Normalize(format) is "HTML5" or "PDF" or "CSV" or "IMAGE";

    public static RenderResult Render(string format, RenderRequest request) => Normalize(format) switch
    {
        "HTML5" => RenderHtml(request),
        "PDF" => RenderPdf(request),
        "CSV" => RenderCsv(request),
        "IMAGE" => RenderPng(request),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported format."),
    };

    private static string Normalize(string format) =>
        format.Trim().ToUpperInvariant() switch
        {
            "HTML5" or "HTML4.0" or "HTML" => "HTML5",
            "PDF" => "PDF",
            "CSV" => "CSV",
            "IMAGE" or "PNG" => "IMAGE",
            _ => format.Trim().ToUpperInvariant(),
        };

    /// <summary>
    /// Renders an accessible HTML page: an <c>&lt;h1&gt;</c> title, a parameter
    /// echo, and a data table with <c>&lt;th scope="col"&gt;</c> headers. All
    /// dynamic text is HTML-encoded. The toolbar is included only when
    /// <c>rc:Toolbar</c> was true.
    /// </summary>
    private static RenderResult RenderHtml(RenderRequest request)
    {
        var sb = new StringBuilder();
        var title = Enc(request.Report.Title);

        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append($"<title>{title}</title>\n");
        sb.Append("</head>\n<body>\n");

        if (request.Toolbar)
        {
            sb.Append("<div class=\"ssrs-toolbar\" role=\"toolbar\" aria-label=\"Report toolbar\">\n");
            sb.Append("  <button type=\"button\">Print</button>\n");
            sb.Append("  <button type=\"button\">Export</button>\n");
            sb.Append("</div>\n");
        }

        sb.Append("<main>\n");
        sb.Append($"<h1>{title}</h1>\n");

        if (request.ShowParameters)
        {
            sb.Append("<section class=\"ssrs-parameters\" aria-label=\"Report parameters\">\n");
            sb.Append("<h2>Parameters</h2>\n<dl>\n");
            foreach (var p in request.Report.Parameters)
            {
                var supplied = request.Values.TryGetValue(p.Name, out var v) ? v : Array.Empty<string>();
                sb.Append($"  <dt>{Enc(p.Name)}</dt>\n");
                sb.Append($"  <dd>{Enc(string.Join(", ", supplied))}</dd>\n");
            }

            sb.Append("</dl>\n</section>\n");
        }

        sb.Append("<table>\n<caption>");
        sb.Append(title);
        sb.Append("</caption>\n<thead>\n<tr>\n");
        foreach (var col in request.Report.Columns)
        {
            sb.Append($"<th scope=\"col\">{Enc(col)}</th>\n");
        }

        sb.Append("</tr>\n</thead>\n<tbody>\n");
        foreach (var row in request.Rows)
        {
            sb.Append("<tr>\n");
            foreach (var cell in row.Cells)
            {
                sb.Append($"<td>{Enc(cell)}</td>\n");
            }

            sb.Append("</tr>\n");
        }

        sb.Append("</tbody>\n</table>\n</main>\n</body>\n</html>\n");

        return new RenderResult(
            Encoding.UTF8.GetBytes(sb.ToString()),
            "text/html; charset=utf-8",
            FileName(request.Report, ".html"));
    }

    /// <summary>The CSV: a header row of column names then one line per data row.</summary>
    private static RenderResult RenderCsv(RenderRequest request)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", request.Report.Columns.Select(CsvField)));
        sb.Append("\r\n");
        foreach (var row in request.Rows)
        {
            sb.Append(string.Join(",", row.Cells.Select(CsvField)));
            sb.Append("\r\n");
        }

        return new RenderResult(
            Encoding.UTF8.GetBytes(sb.ToString()),
            "text/csv; charset=utf-8",
            FileName(request.Report, ".csv"));
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    /// <summary>
    /// A minimal but structurally valid single-page PDF: correct <c>%PDF-1.4</c>
    /// header, four objects, a real cross-reference table with byte offsets, a
    /// trailer, and <c>%%EOF</c>. The report title is drawn on the page so a
    /// "valid PDF containing the title" assertion can pass.
    /// </summary>
    private static RenderResult RenderPdf(RenderRequest request)
    {
        var titleText = PdfEscape(request.Report.Title);
        var content = $"BT /F1 24 Tf 72 720 Td ({titleText}) Tj ET";
        var contentBytes = Encoding.ASCII.GetByteCount(content);

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {contentBytes} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        // A binary-marker comment, as recommended by the PDF spec for binary files.
        sb.Append("%âãÏÓ\n");

        var offsets = new int[objects.Length + 1];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = Encoding.Latin1.GetByteCount(sb.ToString());
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append("xref\n");
        sb.Append($"0 {objects.Length + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Length; i++)
        {
            sb.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture));
            sb.Append(" 00000 n \n");
        }

        sb.Append("trailer\n");
        sb.Append($"<< /Size {objects.Length + 1} /Root 1 0 R >>\n");
        sb.Append("startxref\n");
        sb.Append(xrefOffset.ToString(CultureInfo.InvariantCulture));
        sb.Append("\n%%EOF\n");

        return new RenderResult(
            Encoding.Latin1.GetBytes(sb.ToString()),
            "application/pdf",
            FileName(request.Report, ".pdf"));
    }

    private static string PdfEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>
    /// A tiny valid PNG: signature, IHDR (1x1 truecolor), a zlib-wrapped IDAT
    /// holding one stored block, and IEND. Each chunk carries a real CRC-32 so
    /// image decoders accept it.
    /// </summary>
    private static RenderResult RenderPng(RenderRequest request)
    {
        using var ms = new MemoryStream();
        // PNG signature.
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR: width=1, height=1, bit depth=8, colour type=2 (truecolor).
        var ihdr = new byte[]
        {
            0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0,
        };
        WriteChunk(ms, "IHDR", ihdr);

        // One scanline: filter byte 0 + RGB pixel. Wrapped in an uncompressed
        // zlib/DEFLATE stored block so we depend on nothing but CRC arithmetic.
        var raw = new byte[] { 0x00, 0x4F, 0x9E, 0xC7 };
        var idat = ZlibStored(raw);
        WriteChunk(ms, "IDAT", idat);

        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return new RenderResult(
            ms.ToArray(),
            "image/png",
            FileName(request.Report, ".png"));
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        WriteBigEndian(stream, (uint)data.Length);
        stream.Write(typeBytes);
        stream.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        WriteBigEndian(stream, Crc32(crcInput));
    }

    private static byte[] ZlibStored(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); // zlib CMF
        ms.WriteByte(0x01); // zlib FLG (no preset dict, fastest)

        // A single final stored DEFLATE block.
        ms.WriteByte(0x01);
        var len = (ushort)data.Length;
        ms.WriteByte((byte)(len & 0xFF));
        ms.WriteByte((byte)((len >> 8) & 0xFF));
        var nlen = (ushort)~len;
        ms.WriteByte((byte)(nlen & 0xFF));
        ms.WriteByte((byte)((nlen >> 8) & 0xFF));
        ms.Write(data);

        WriteBigEndian(ms, Adler32(data));
        return ms.ToArray();
    }

    private static void WriteBigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint Adler32(byte[] data)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % Mod;
            b = (b + a) % Mod;
        }

        return (b << 16) | a;
    }

    private static string FileName(ReportDefinition report, string extension)
    {
        var leaf = report.Path.TrimStart('/').Replace('/', '_');
        return leaf + extension;
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
