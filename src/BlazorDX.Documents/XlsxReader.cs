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
/// Deferred (Phase 3+): formulas surface their last cached <c>&lt;v&gt;</c> value,
/// they are not recomputed; number formats, styles, and merged cells are not yet
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

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        // Trim-/AOT-safe and untrusting: no DTD processing, no external entity
        // resolution, no schema validation. Pure streaming reads.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

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
        using Stream content = entry.Open();
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
        using Stream content = entry.Open();
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
        using Stream content = entry.Open();
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

        // Sparse rows keyed by 1-based row number; track the widest column so we can
        // pad every row to a uniform width afterwards.
        List<List<string>> rows = [];
        int maxColumns = 0;

        using Stream content = entry.Open();
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
            rows.Add(row);
            if (row.Count > maxColumns)
            {
                maxColumns = row.Count;
            }
        }

        // Pad ragged rows to the widest row so the grid is dense and uniform.
        foreach (List<string> row in rows)
        {
            for (int c = row.Count; c < maxColumns; c++)
            {
                row.Add(string.Empty);
            }
        }

        return new Worksheet(name, rows, maxColumns);
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
    // Formulas (<f>) are ignored; the cached <v> is what we surface.
    private static string ReadCellValue(XmlReader reader, string? cellType, IReadOnlyList<string> sharedStrings)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        int cellDepth = reader.Depth;
        string? value = null;

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
                case "v":
                    value = ReadElementText(reader);
                    break;
                case "is":
                    // Inline string: gather its <t> runs directly.
                    return ReadStringItem(reader);
            }
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
        int column = 0;
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

            i++;
        }

        return column == 0 ? -1 : column - 1;
    }
}
