using System.Globalization;
using System.IO.Compression;
using System.Text;
using BlazorDX.Documents.Formula;

namespace BlazorDX.Documents;

/// <summary>
/// The write-back half of the spreadsheet round-trip: serializes an edited
/// <see cref="Workbook"/> of raw cell content (literals and <c>=</c> formulas) back
/// to <c>.xlsx</c> bytes. It is the inverse of <see cref="XlsxReader"/> and lives in
/// the same package so the read/write pair stays symmetric.
/// </summary>
/// <remarks>
/// <para>
/// This writer differs from the grid's header-oriented
/// <c>BlazorDX.Primitives.Grid.XlsxWriter</c>: it persists a full sheet of arbitrary
/// rows (no special header row) and, crucially, <b>emits formula cells</b> as
/// <c>&lt;c&gt;&lt;f&gt;EXPR&lt;/f&gt;&lt;v&gt;cached&lt;/v&gt;&lt;/c&gt;</c> so formulas
/// survive a save. The leading <c>=</c> is stripped from the stored expression (OOXML
/// formulas are bare), and the cached <c>&lt;v&gt;</c> holds the value the formula
/// engine last computed so other readers see a value without recalculating.
/// </para>
/// <para>
/// Multi-sheet workbooks are written in order. Each sheet's formulas are recomputed
/// with <see cref="FormulaEngine.Recalculate(Worksheet)"/> to populate the cached
/// values. Pure C# over <see cref="System.IO.Compression"/> — no third-party library,
/// no reflection, AOT- and trim-safe, so it runs unchanged in the browser runtime.
/// </para>
/// </remarks>
public static class XlsxWorkbookWriter
{
    private const string SpreadsheetMl =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string OfficeRelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"" + OfficeRelNs + "/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"" + SpreadsheetMl + "\">" +
        "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs>" +
        "</styleSheet>";

    /// <summary>The IANA media type browsers expect for an .xlsx download.</summary>
    public const string MimeType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Serializes <paramref name="workbook"/> to <c>.xlsx</c> bytes, persisting each
    /// cell's raw content: literals as typed cells and <c>=</c> formulas as formula
    /// cells with an engine-computed cached value.
    /// </summary>
    public static byte[] Write(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        IReadOnlyList<Worksheet> sheets = workbook.Sheets;
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", BuildContentTypes(sheets.Count));
            AddEntry(zip, "_rels/.rels", RootRels);
            AddEntry(zip, "xl/workbook.xml", BuildWorkbookPart(sheets));
            AddEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Count));
            AddEntry(zip, "xl/styles.xml", Styles);

            for (int i = 0; i < sheets.Count; i++)
            {
                AddEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", BuildSheet(sheets[i]));
            }
        }

        return stream.ToArray();
    }

    private static string BuildContentTypes(int sheetCount)
    {
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        for (int i = 0; i < sheetCount; i++)
        {
            sb.Append("<Override PartName=\"/xl/worksheets/sheet").Append(i + 1)
              .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        }

        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string BuildWorkbookPart(IReadOnlyList<Worksheet> sheets)
    {
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<workbook xmlns=\"").Append(SpreadsheetMl)
          .Append("\" xmlns:r=\"").Append(OfficeRelNs).Append("\"><sheets>");
        for (int i = 0; i < sheets.Count; i++)
        {
            sb.Append("<sheet name=\"");
            AppendAttributeEscaped(sb, SheetName(sheets[i].Name, i));
            sb.Append("\" sheetId=\"").Append(i + 1).Append("\" r:id=\"rId").Append(i + 1).Append("\"/>");
        }

        sb.Append("</sheets></workbook>");
        return sb.ToString();
    }

    private static string BuildWorkbookRels(int sheetCount)
    {
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < sheetCount; i++)
        {
            sb.Append("<Relationship Id=\"rId").Append(i + 1)
              .Append("\" Type=\"").Append(OfficeRelNs).Append("/worksheet\" Target=\"worksheets/sheet")
              .Append(i + 1).Append(".xml\"/>");
        }

        sb.Append("<Relationship Id=\"rIdStyles\" Type=\"").Append(OfficeRelNs)
          .Append("/styles\" Target=\"styles.xml\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string BuildSheet(Worksheet sheet)
    {
        IReadOnlyList<IReadOnlyList<string>> rows = sheet.Rows;
        CellValue[][] computed = FormulaEngine.Recalculate(sheet);

        int columns = Math.Max(sheet.ColumnCount, 1);
        int lastRow = Math.Max(rows.Count, 1);

        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"").Append(SpreadsheetMl).Append("\">");
        sb.Append("<dimension ref=\"A1:").Append(ColumnName(columns - 1)).Append(lastRow).Append("\"/>");
        sb.Append("<sheetData>");

        for (int r = 0; r < rows.Count; r++)
        {
            IReadOnlyList<string> row = rows[r];
            sb.Append("<row r=\"").Append(r + 1).Append("\">");
            for (int c = 0; c < row.Count; c++)
            {
                CellValue cached = (r < computed.Length && c < computed[r].Length)
                    ? computed[r][c]
                    : CellValue.Blank;
                AppendCell(sb, c, r + 1, row[c] ?? string.Empty, cached);
            }

            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    // Writes one cell. Formulas (=…) become <f>EXPR</f><v>cached</v> so they survive a
    // round-trip; literals become numeric, boolean, or inline-string cells.
    private static void AppendCell(StringBuilder sb, int column, int rowNumber, string raw, CellValue cached)
    {
        if (raw.Length == 0)
        {
            return; // a blank cell needs no element
        }

        string reference = ColumnName(column) + rowNumber.ToString(CultureInfo.InvariantCulture);

        if (raw[0] == '=')
        {
            // OOXML formulas omit the leading "="; the cached value lets non-evaluating
            // readers (and our own XlsxReader's value path) show a result.
            AppendFormulaCell(sb, reference, raw[1..], cached);
            return;
        }

        if (IsNumber(raw))
        {
            sb.Append("<c r=\"").Append(reference).Append("\"><v>").Append(raw).Append("</v></c>");
            return;
        }

        sb.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
        AppendTextEscaped(sb, raw);
        sb.Append("</t></is></c>");
    }

    private static void AppendFormulaCell(StringBuilder sb, string reference, string expression, CellValue cached)
    {
        // A formula whose result is text is typed t="str" (a formula-result string);
        // numeric/boolean/blank results use the default numeric value channel; an error
        // result is written with t="e" so readers recognise it.
        bool isText = cached.Kind == CellValueKind.Text;
        bool isError = cached.IsError;

        sb.Append("<c r=\"").Append(reference).Append('"');
        if (isText)
        {
            sb.Append(" t=\"str\"");
        }
        else if (isError)
        {
            sb.Append(" t=\"e\"");
        }

        sb.Append("><f>");
        AppendTextEscaped(sb, expression);
        sb.Append("</f><v>");
        AppendTextEscaped(sb, CachedValueText(cached));
        sb.Append("</v></c>");
    }

    // The text written into a formula's cached <v>. Numbers/booleans use their
    // round-trippable form; text and errors use the display string.
    private static string CachedValueText(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => value.AsRawNumber.ToString("R", CultureInfo.InvariantCulture),
        CellValueKind.Boolean => value.AsRawBool ? "1" : "0",
        CellValueKind.Blank => "0",
        _ => value.ToDisplayString(),
    };

    private static string SheetName(string? name, int index) =>
        string.IsNullOrEmpty(name) ? $"Sheet{index + 1}" : name;

    // A value is treated as numeric only when it round-trips exactly, so identifiers
    // like "007" or "1e5" keep their text form instead of being silently rewritten.
    private static bool IsNumber(string text) =>
        text.Length != 0
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        && parsed.ToString(CultureInfo.InvariantCulture) == text;

    // 0 -> "A", 25 -> "Z", 26 -> "AA" (spreadsheet bijective base-26 column names).
    private static string ColumnName(int index)
    {
        if (index < 0)
        {
            index = 0;
        }

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

    private static void AppendTextEscaped(StringBuilder sb, string value)
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

    private static void AppendAttributeEscaped(StringBuilder sb, string value)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
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
