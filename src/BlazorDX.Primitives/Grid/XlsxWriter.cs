using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// A tiny, dependency-free writer for the Office Open XML spreadsheet (.xlsx)
/// format. It assembles the minimal set of OOXML parts (content types, package
/// relationships, a workbook, a styles part, and one worksheet) into a ZIP
/// package by hand — no third-party library, no reflection, AOT- and trim-safe,
/// so it runs unchanged in the browser WebAssembly runtime.
/// </summary>
/// <remarks>
/// Cells are written as inline strings, except values that round-trip exactly as
/// an invariant number (so "10" becomes a real numeric cell while "007" stays
/// text). The first row is rendered bold via a single styled cell format.
/// </remarks>
internal static class XlsxWriter
{
    private const string SpreadsheetMl =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string Workbook =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"" + SpreadsheetMl + "\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private const string WorkbookRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // Two cell formats: index 0 is the default, index 1 applies the bold font.
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"" + SpreadsheetMl + "\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
        "</styleSheet>";

    /// <summary>The IANA media type browsers expect for an .xlsx download.</summary>
    public const string MimeType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Builds a single-worksheet workbook: <paramref name="headers"/> become a
    /// bold first row, then each item of <paramref name="rows"/> becomes a row.
    /// </summary>
    public static byte[] Write(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        string sheet = BuildSheet(headers, rows);

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", ContentTypes);
            AddEntry(zip, "_rels/.rels", RootRels);
            AddEntry(zip, "xl/workbook.xml", Workbook);
            AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            AddEntry(zip, "xl/styles.xml", Styles);
            AddEntry(zip, "xl/worksheets/sheet1.xml", sheet);
        }

        return stream.ToArray();
    }

    private static string BuildSheet(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        int columns = headers.Count;
        int lastRow = rows.Count + 1;

        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"").Append(SpreadsheetMl).Append("\">");
        sb.Append("<dimension ref=\"A1:").Append(ColumnName(Math.Max(columns - 1, 0)))
          .Append(lastRow).Append("\"/>");
        sb.Append("<sheetData>");

        AppendRow(sb, 1, headers, styleIndex: 1);
        for (int r = 0; r < rows.Count; r++)
        {
            AppendRow(sb, r + 2, rows[r], styleIndex: 0);
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowNumber, IReadOnlyList<string> cells, int styleIndex)
    {
        sb.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (int c = 0; c < cells.Count; c++)
        {
            string reference = ColumnName(c) + rowNumber.ToString(CultureInfo.InvariantCulture);
            string value = cells[c] ?? string.Empty;

            sb.Append("<c r=\"").Append(reference).Append('"');
            if (styleIndex != 0)
            {
                sb.Append(" s=\"").Append(styleIndex).Append('"');
            }

            if (styleIndex == 0 && IsNumber(value))
            {
                sb.Append("><v>").Append(value).Append("</v></c>");
            }
            else
            {
                sb.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
                AppendEscaped(sb, value);
                sb.Append("</t></is></c>");
            }
        }

        sb.Append("</row>");
    }

    // A value is treated as numeric only when it round-trips exactly, so identifiers
    // like "007" or "1e5" keep their text form instead of being silently rewritten.
    private static bool IsNumber(string text) =>
        !string.IsNullOrEmpty(text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        && parsed.ToString(CultureInfo.InvariantCulture) == text;

    // 0 -> "A", 25 -> "Z", 26 -> "AA" (spreadsheet bijective base-26 column names).
    private static string ColumnName(int index)
    {
        Span<char> buffer = stackalloc char[8];
        int position = buffer.Length;
        for (int n = index + 1; n > 0; n /= 26)
        {
            int remainder = (n - 1) % 26;
            buffer[--position] = (char)('A' + remainder);
            n -= remainder;
        }

        return new string(buffer[position..]);
    }

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default:
                    // Drop characters the XML 1.0 spec forbids; keep tab/newline/return.
                    if (c >= 0x20 || c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
