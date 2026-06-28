using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace BlazorDX.Documents;

/// <summary>
/// A tiny, dependency-free reader for the Office Open XML spreadsheet (.xlsx)
/// format. It is the inverse of <c>BlazorDX.Primitives.Grid.XlsxWriter</c>: it
/// opens the ZIP package by hand with <see cref="ZipArchive"/> and parses the
/// OOXML parts with a streaming <see cref="XmlReader"/> — no third-party library,
/// no reflection, AOT- and trim-safe, so it runs unchanged in the browser
/// WebAssembly runtime.
/// </summary>
/// <remarks>
/// <para>
/// The reader produces the same <see cref="Workbook"/> / <see cref="Worksheet"/>
/// model the spreadsheet viewer consumes. It round-trips what the writer produces
/// and also handles the parts a real-world workbook adds: a shared-string table,
/// <c>t="str"</c> formula-result strings, and ragged rows.
/// </para>
/// <para>
/// Formula cells round-trip as their source: a <c>&lt;f&gt;EXPR&lt;/f&gt;</c> surfaces as
/// <c>=EXPR</c> (the cached <c>&lt;v&gt;</c> is used only for plain value cells), so an
/// edited workbook saved by <see cref="XlsxWorkbookWriter"/> reads its formulas back
/// intact. Deferred (Phase 3+): number formats, styles, and merged cells are not yet
/// interpreted (cells carry their raw text). Dates therefore appear as their
/// underlying serial number.
/// </para>
/// </remarks>
public static class XlsxReader
{
    private const string SpreadsheetMl =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string RelationshipsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string OfficeRelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Defensive cap on a single decompressed part. An .xlsx is attacker-controlled
    // (uploaded by an untrusted user), so a maliciously crafted "zip bomb" entry —
    // tiny compressed, enormous when inflated — must fail cleanly rather than exhaust
    // memory. 64 MiB is far above any legitimate single OOXML part.
    private const long MaxPartBytes = 64L * 1024 * 1024;

    // Excel's hard maximum column count (the last column is "XFD"). A cell reference encoding a
    // column beyond this is malformed or hostile — a handful of letters can encode billions, and
    // the dense row would pre-pad that many cells — so such a reference is treated as invalid.
    private const int MaxColumns = 16384;

    // Upper bound on cells materialized for one sheet. With columns clamped to MaxColumns a single
    // row can't blow up, but rows × up-to-16384 still multiplies; this fails a hostile sheet cleanly
    // rather than exhausting memory. ~16M cells is generous for a viewer and bounds the backing
    // arrays to tens of MB.
    private const long MaxCellsPerSheet = 16L * 1024 * 1024;

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        // Trim-/AOT-safe and untrusting: no DTD processing, no external entity
        // resolution, no schema validation. Pure streaming reads. DtdProcessing.Prohibit
        // + XmlResolver = null together defeat XXE and the "billion laughs" entity-
        // expansion attack; MaxCharactersInDocument bounds the streamed character count.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxPartBytes,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    // Opens a zip entry for reading, first rejecting any entry whose declared
    // uncompressed length exceeds <see cref="MaxPartBytes"/>. ZipArchiveEntry.Length is
    // read from the local/central directory header without inflating the data, so this
    // check is cheap and happens before a single decompressed byte is buffered.
    private static Stream OpenChecked(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartBytes)
        {
            throw new InvalidDataException(
                $"xlsx part '{entry.FullName}' exceeds the {MaxPartBytes}-byte size limit and was rejected.");
        }

        // The declared Length above is a cheap early-out, but a crafted zip can understate it and
        // inflate further; the wrapper bounds the bytes actually read (the true zip-bomb guard).
        return new LengthLimitingStream(entry.Open(), MaxPartBytes, entry.FullName);
    }

    /// <summary>
    /// Parses an <c>.xlsx</c> byte stream into a <see cref="Workbook"/>: the sheets
    /// in workbook order, each as a dense, padded grid of string cell values.
    /// </summary>
    /// <param name="bytes">The raw bytes of an <c>.xlsx</c> file.</param>
    /// <returns>The parsed workbook.</returns>
    public static Workbook Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using MemoryStream stream = new(bytes, writable: false);
        return Read(stream);
    }

    /// <summary>
    /// Parses an <c>.xlsx</c> stream into a <see cref="Workbook"/>. The stream is
    /// read as a ZIP package; the caller owns its lifetime.
    /// </summary>
    /// <param name="stream">A seekable stream positioned at the start of an <c>.xlsx</c> file.</param>
    /// <returns>The parsed workbook.</returns>
    public static Workbook Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read, leaveOpen: true);

        IReadOnlyList<string> sharedStrings = ReadSharedStrings(zip);

        // r:id -> worksheet part path (resolved relative to xl/).
        IReadOnlyDictionary<string, string> relationships = ReadWorkbookRelationships(zip);

        // (sheet name, r:id) in workbook order.
        IReadOnlyList<(string Name, string RelId)> sheetRefs = ReadWorkbookSheets(zip);

        List<Worksheet> sheets = new(sheetRefs.Count);
        foreach ((string name, string relId) in sheetRefs)
        {
            string? partPath = relId.Length != 0 && relationships.TryGetValue(relId, out string? p) ? p : null;
            ZipArchiveEntry? entry = partPath is null ? null : zip.GetEntry(partPath);
            sheets.Add(ReadWorksheet(name, entry, sharedStrings));
        }

        return new Workbook(sheets);
    }

    // xl/sharedStrings.xml: a flat ordered list of <si> entries. Each <si> holds
    // either a single <t> or several <r><t> runs (rich text) we concatenate.
    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive zip)
    {
        ZipArchiveEntry? entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        List<string> strings = [];
        using Stream content = OpenChecked(entry);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "si"
                && reader.NamespaceURI == SpreadsheetMl)
            {
                strings.Add(ReadStringItem(reader));
            }
        }

        return strings;
    }

    // Reads the text of a string item (<si> or an inline <is>): the union of every
    // <t> descendant. The reader is positioned on the container element; on return
    // it sits on that element's EndElement.
    private static string ReadStringItem(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        string containerName = reader.LocalName;
        int containerDepth = reader.Depth;
        string? single = null;            // common case: exactly one <t> with no runs
        System.Text.StringBuilder? many = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == containerDepth
                && reader.LocalName == containerName)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "t"
                && reader.NamespaceURI == SpreadsheetMl)
            {
                string text = ReadElementText(reader);
                if (many is not null)
                {
                    many.Append(text);
                }
                else if (single is null)
                {
                    single = text;
                }
                else
                {
                    many = new System.Text.StringBuilder(single);
                    many.Append(text);
                }
            }
        }

        return many?.ToString() ?? single ?? string.Empty;
    }

    // xl/_rels/workbook.xml.rels: maps each relationship Id to a worksheet part.
    // Targets are stored relative to xl/, so "worksheets/sheet1.xml" -> "xl/worksheets/sheet1.xml".
    private static IReadOnlyDictionary<string, string> ReadWorkbookRelationships(ZipArchive zip)
    {
        ZipArchiveEntry? entry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (entry is null)
        {
            return new Dictionary<string, string>(0);
        }

        Dictionary<string, string> map = [];
        using Stream content = OpenChecked(entry);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "Relationship"
                || reader.NamespaceURI != RelationshipsNs)
            {
                continue;
            }

            string? id = reader.GetAttribute("Id");
            string? type = reader.GetAttribute("Type");
            string? target = reader.GetAttribute("Target");
            if (id is null || target is null)
            {
                continue;
            }

            // Only worksheet relationships matter; ignore styles/theme/sharedStrings.
            if (type is not null && !type.EndsWith("/worksheet", StringComparison.Ordinal))
            {
                continue;
            }

            map[id] = ResolvePartPath(target);
        }

        return map;
    }

    // xl/workbook.xml: <sheets> children carry the display name and the r:id linking
    // to the worksheet part. Document order is workbook order.
    private static IReadOnlyList<(string Name, string RelId)> ReadWorkbookSheets(ZipArchive zip)
    {
        ZipArchiveEntry? entry = zip.GetEntry("xl/workbook.xml");
        if (entry is null)
        {
            return [];
        }

        List<(string, string)> sheets = [];
        using Stream content = OpenChecked(entry);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "sheet"
                || reader.NamespaceURI != SpreadsheetMl)
            {
                continue;
            }

            string name = reader.GetAttribute("name") ?? string.Empty;
            string relId = reader.GetAttribute("id", OfficeRelNs) ?? string.Empty;
            sheets.Add((name, relId));
        }

        return sheets;
    }

    // Parses one worksheet part into a dense, padded grid. A missing/empty part
    // yields an empty sheet (no rows, zero columns) so the model stays well-formed.
    private static Worksheet ReadWorksheet(string name, ZipArchiveEntry? entry, IReadOnlyList<string> sharedStrings)
    {
        if (entry is null)
        {
            return new Worksheet(name, [], 0);
        }

        // Read every <row> as a dense, left-anchored list of cell strings.
        List<List<string>> rows = [];
        long totalCells = 0;

        using Stream content = OpenChecked(entry);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "row"
                || reader.NamespaceURI != SpreadsheetMl)
            {
                continue;
            }

            List<string> row = ReadRow(reader, sharedStrings);
            totalCells += row.Count;
            if (totalCells > MaxCellsPerSheet)
            {
                throw new InvalidDataException(
                    $"xlsx sheet '{name}' exceeds the {MaxCellsPerSheet}-cell limit and was rejected.");
            }

            rows.Add(row);
        }

        // Trim to the TRUE used range. Excel commonly persists styled-but-empty trailing
        // cells and rows (the declared dimension over-reports), which would otherwise
        // render as blank columns/rows after the data visibly stops. We size the sheet to
        // the last row/column that actually holds a value; interior blanks are preserved.
        // Out-of-bounds formula references already read as blank (see WorkbookRecalc), so
        // narrowing the grid cannot change any computed result.
        int usedColumns = 0;
        int usedRows = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            List<string> row = rows[r];
            int lastValue = -1;
            for (int c = 0; c < row.Count; c++)
            {
                if (!string.IsNullOrEmpty(row[c]))
                {
                    lastValue = c;
                }
            }

            if (lastValue >= 0)
            {
                usedRows = r + 1;
                if (lastValue + 1 > usedColumns)
                {
                    usedColumns = lastValue + 1;
                }
            }
        }

        // Drop trailing all-empty rows, then square every remaining row to the used width
        // (trim overruns, pad short rows) so the grid stays dense and uniform.
        if (rows.Count > usedRows)
        {
            rows.RemoveRange(usedRows, rows.Count - usedRows);
        }

        foreach (List<string> row in rows)
        {
            if (row.Count > usedColumns)
            {
                row.RemoveRange(usedColumns, row.Count - usedColumns);
            }

            for (int c = row.Count; c < usedColumns; c++)
            {
                row.Add(string.Empty);
            }
        }

        return new Worksheet(name, rows, usedColumns);
    }

    // Reads one <row>: places each cell at the column its A1 reference encodes,
    // filling gaps (sparse cells) with "". Returns a dense, left-anchored row.
    private static List<string> ReadRow(XmlReader reader, IReadOnlyList<string> sharedStrings)
    {
        List<string> cells = [];
        if (reader.IsEmptyElement)
        {
            return cells;
        }

        int rowDepth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == rowDepth
                && reader.LocalName == "row")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "c"
                || reader.NamespaceURI != SpreadsheetMl)
            {
                continue;
            }

            string? cellRef = reader.GetAttribute("r");
            string? cellType = reader.GetAttribute("t");
            int column = cellRef is not null ? ColumnIndex(cellRef) : cells.Count;

            string value = ReadCellValue(reader, cellType, sharedStrings);

            // Fill any gap left by sparse (omitted) cells with empty strings.
            if (column < 0)
            {
                column = cells.Count;
            }

            while (cells.Count < column)
            {
                cells.Add(string.Empty);
            }

            if (column < cells.Count)
            {
                cells[column] = value;
            }
            else
            {
                cells.Add(value);
            }
        }

        return cells;
    }

    // Reads a cell's resolved text. The reader is positioned on the <c> element.
    //   t="s"          -> <v> is an index into the shared-string table
    //   t="inlineStr"  -> <is> holds the literal string
    //   t="str"        -> <v> is a formula-result string (used verbatim)
    //   t="b"          -> <v> is "1"/"0" boolean
    //   (no t / "n")   -> <v> is a number (kept as its invariant text)
    // A formula cell (<f>EXPR</f>) surfaces as its source text prefixed with "=", so an
    // edited workbook's formulas round-trip; the cached <v> is kept only when there is
    // no <f> (a plain value cell). This makes the reader the faithful inverse of
    // <see cref="XlsxWorkbookWriter"/>.
    private static string ReadCellValue(XmlReader reader, string? cellType, IReadOnlyList<string> sharedStrings)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        int cellDepth = reader.Depth;
        string? value = null;
        string? formula = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == cellDepth
                && reader.LocalName == "c")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != SpreadsheetMl)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "f":
                    formula = ReadElementText(reader);
                    break;
                case "v":
                    value = ReadElementText(reader);
                    break;
                case "is":
                    // Inline string: gather its <t> runs directly.
                    return ReadStringItem(reader);
            }
        }

        // A formula cell round-trips as its "=EXPR" source, not the cached value.
        if (formula is not null)
        {
            return "=" + formula;
        }

        if (value is null)
        {
            return string.Empty;
        }

        if (cellType == "s")
        {
            // Shared-string cell: <v> is the table index.
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                && index >= 0 && index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }

            return string.Empty;
        }

        // "str" (formula result), "b" (boolean), numeric, and anything else: the
        // cached value text is used verbatim.
        return value;
    }

    // Reads the concatenated text content of the current element and leaves the
    // reader on its EndElement (or on the element itself if it is empty).
    private static string ReadElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        string elementName = reader.LocalName;
        int elementDepth = reader.Depth;
        string? single = null;
        System.Text.StringBuilder? many = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == elementDepth
                && reader.LocalName == elementName)
            {
                break;
            }

            if (reader.NodeType is XmlNodeType.Text
                or XmlNodeType.CDATA
                or XmlNodeType.SignificantWhitespace
                or XmlNodeType.Whitespace)
            {
                if (many is not null)
                {
                    many.Append(reader.Value);
                }
                else if (single is null)
                {
                    single = reader.Value;
                }
                else
                {
                    many = new System.Text.StringBuilder(single);
                    many.Append(reader.Value);
                }
            }
        }

        return many?.ToString() ?? single ?? string.Empty;
    }

    // Relationship targets may be relative ("worksheets/sheet1.xml"), have a leading
    // "/" (package-absolute), or a "../" prefix; normalize to a zip entry path under xl/.
    private static string ResolvePartPath(string target)
    {
        if (target.StartsWith('/'))
        {
            // Package-absolute path: drop the leading slash.
            return target[1..];
        }

        if (target.StartsWith("../", StringComparison.Ordinal))
        {
            // Relative to xl/, climbing out: "../foo" -> "foo".
            return target[3..];
        }

        // Relative to the workbook's folder (xl/).
        return "xl/" + target;
    }

    // Parses the leading column letters of an A1 reference ("B12" -> 1, "AA3" -> 26).
    // Returns -1 when the reference has no column letters.
    private static int ColumnIndex(string cellRef)
    {
        // Accumulate in long and bail the moment we pass Excel's last column, so a crafted reference
        // (e.g. "AAAAAA1") can neither overflow nor drive an enormous row pre-pad. Beyond range = -1
        // ("invalid"), which ReadRow treats as "append at the next position".
        long column = 0;
        int i = 0;
        while (i < cellRef.Length)
        {
            char ch = cellRef[i];
            if (ch is >= 'A' and <= 'Z')
            {
                column = (column * 26) + (ch - 'A' + 1);
            }
            else if (ch is >= 'a' and <= 'z')
            {
                column = (column * 26) + (ch - 'a' + 1);
            }
            else
            {
                break;
            }

            if (column > MaxColumns)
            {
                return -1;
            }

            i++;
        }

        return column == 0 ? -1 : (int)(column - 1);
    }
}
