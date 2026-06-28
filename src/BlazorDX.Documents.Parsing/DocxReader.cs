using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace BlazorDX.Documents;

/// <summary>
/// A tiny, dependency-free reader for the Office Open XML word-processing (.docx)
/// format — the sibling of <see cref="XlsxReader"/>. It opens the ZIP package by
/// hand with <see cref="ZipArchive"/> and parses <c>word/document.xml</c> (and, when
/// needed, <c>word/styles.xml</c>) with a streaming <see cref="XmlReader"/> — no
/// third-party library, no reflection, AOT- and trim-safe, so it runs unchanged in
/// the browser WebAssembly runtime.
/// </summary>
/// <remarks>
/// <para>
/// The reader produces the immutable <see cref="WordDocument"/> model the Word viewer
/// consumes, in document (reading) order. It maps the common WordprocessingML body
/// constructs:
/// </para>
/// <list type="bullet">
///   <item><description><c>&lt;w:p&gt;</c> paragraphs, with <c>&lt;w:r&gt;&lt;w:t&gt;</c>
///   runs capturing <c>&lt;w:b&gt;</c>/<c>&lt;w:i&gt;</c> emphasis.</description></item>
///   <item><description>Headings from <c>&lt;w:pStyle w:val="Heading1..6"&gt;</c> (or
///   <c>Title</c> → level 1). Non-standard style ids are resolved through
///   <c>word/styles.xml</c> via the style's <c>&lt;w:name&gt;</c> or
///   <c>&lt;w:outlineLvl&gt;</c>.</description></item>
///   <item><description>Lists from <c>&lt;w:numPr&gt;</c>: a paragraph carrying a numbering
///   reference becomes a list item. Numbered vs. bulleted is a best-effort guess (see
///   below).</description></item>
///   <item><description>Tables <c>&lt;w:tbl&gt;</c>/<c>&lt;w:tr&gt;</c>/<c>&lt;w:tc&gt;</c>;
///   the first row is treated as the header row.</description></item>
/// </list>
/// <para>
/// <b>Deferred / not interpreted (best-effort, degrade gracefully):</b> images and
/// drawings; footnotes/endnotes, comments, and fields; hyperlinks render as their
/// plain text; list <em>nesting</em> is flattened to a single level; the
/// numbered-vs-bulleted decision reads <c>w:numId</c> heuristically (a <c>numId</c>
/// of <c>0</c>/absent is treated as bulleted) rather than resolving
/// <c>word/numbering.xml</c>; run-level color/size/underline beyond bold/italic is
/// dropped; merged table cells are read as individual cells. Anything unrecognized
/// degrades to a plain paragraph.
/// </para>
/// </remarks>
public static partial class DocxReader
{
    private const string WordprocessingMl =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // The relationships namespace — the r:id on <w:hyperlink> lives here, and the rels
    // part lists each Relationship's package-relationships namespace.
    private const string RelationshipsNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Defensive cap on a single decompressed part. A .docx is attacker-controlled
    // (uploaded by an untrusted user), so a maliciously crafted "zip bomb" entry —
    // tiny compressed, enormous when inflated — must fail cleanly rather than exhaust
    // memory. 64 MiB is far above any legitimate single OOXML part.
    private const long MaxPartBytes = 64L * 1024 * 1024;

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
                $"docx part '{entry.FullName}' exceeds the {MaxPartBytes}-byte size limit and was rejected.");
        }

        return entry.Open();
    }

    /// <summary>
    /// Parses a <c>.docx</c> byte stream into a <see cref="WordDocument"/>: the body
    /// blocks in document (reading) order.
    /// </summary>
    /// <param name="bytes">The raw bytes of a <c>.docx</c> file.</param>
    /// <returns>The parsed document.</returns>
    public static WordDocument Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using MemoryStream stream = new(bytes, writable: false);
        return Read(stream);
    }

    /// <summary>
    /// Parses a <c>.docx</c> stream into a <see cref="WordDocument"/>. The stream is
    /// read as a ZIP package; the caller owns its lifetime.
    /// </summary>
    /// <param name="stream">A seekable stream positioned at the start of a <c>.docx</c> file.</param>
    /// <returns>The parsed document.</returns>
    public static WordDocument Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using ZipArchive zip = new(stream, ZipArchiveMode.Read, leaveOpen: true);

        // styleId -> heading level (1-6), resolved from word/styles.xml. Lets us map
        // non-standard heading style ids (e.g. "Ttulo1") via name/outlineLvl.
        IReadOnlyDictionary<string, int> headingStyles = ReadHeadingStyles(zip);
        // rId -> external target, so <w:hyperlink r:id> runs can carry their URL.
        IReadOnlyDictionary<string, string> linkRels = ReadHyperlinkRels(zip);
        // rId -> decoded image bytes, so <w:drawing> blips become WordImage blocks.
        IReadOnlyDictionary<string, ImagePart> imageParts = ReadImageParts(zip);

        ZipArchiveEntry? document = zip.GetEntry("word/document.xml");
        if (document is null)
        {
            return new WordDocument([]);
        }

        using Stream content = OpenChecked(document);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        List<WordBlock> blocks = [];
        // Pending list run: consecutive list-item paragraphs of the same kind are
        // coalesced into one WordList so they render as a single <ul>/<ol>.
        bool listOrdered = false;
        List<IReadOnlyList<WordRun>>? listItems = null;
        List<int>? listLevels = null;

        void FlushList()
        {
            if (listItems is { Count: > 0 })
            {
                bool nested = listLevels is not null && listLevels.Exists(l => l > 0);
                blocks.Add(new WordList(listOrdered, listItems, nested ? listLevels : null));
            }

            listItems = null;
            listLevels = null;
        }

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            if (reader.LocalName == "p")
            {
                ParagraphContent para = ReadParagraph(reader, headingStyles, linkRels, imageParts);

                if (para.ListKind is { } kind)
                {
                    // Same-kind run continues; a kind switch closes the previous list.
                    if (listItems is not null && listOrdered != kind.Ordered)
                    {
                        FlushList();
                    }

                    listOrdered = kind.Ordered;
                    listItems ??= [];
                    listLevels ??= [];
                    listItems.Add(para.Runs);
                    listLevels.Add(kind.Level);
                    continue;
                }

                FlushList();

                if (para.Image is { } image)
                {
                    blocks.Add(image); // a paragraph that holds a drawing becomes an image block
                }
                else if (para.HeadingLevel is { } level)
                {
                    blocks.Add(new WordHeading(level, para.Runs, para.Alignment));
                }
                else
                {
                    blocks.Add(new WordParagraph(para.Runs, para.Alignment));
                }
            }
            else if (reader.LocalName == "tbl")
            {
                FlushList();
                blocks.Add(ReadTable(reader, linkRels));
            }
        }

        FlushList();
        return new WordDocument(blocks);
    }

    private readonly record struct ListKind(bool Ordered, int Level);

    private sealed record ParagraphContent(
        IReadOnlyList<WordRun> Runs,
        int? HeadingLevel,
        ListKind? ListKind,
        WordAlignment Alignment = WordAlignment.Start,
        WordImage? Image = null);

    // Reads one <w:p>: its paragraph properties (<w:pPr>: style, numbering) then its
    // runs (and any embedded image). The reader is positioned on the <w:p> start element;
    // on return it sits on the matching </w:p> end element.
    private static ParagraphContent ReadParagraph(
        XmlReader reader,
        IReadOnlyDictionary<string, int> headingStyles,
        IReadOnlyDictionary<string, string> linkRels,
        IReadOnlyDictionary<string, ImagePart> imageParts)
    {
        if (reader.IsEmptyElement)
        {
            return new ParagraphContent([], null, null);
        }

        int paraDepth = reader.Depth;
        List<WordRun> runs = [];
        List<WordImage> images = [];
        int? headingLevel = null;
        ListKind? listKind = null;
        WordAlignment alignment = WordAlignment.Start;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == paraDepth
                && reader.LocalName == "p")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "pPr":
                    (headingLevel, listKind, alignment) = ReadParagraphProperties(reader, headingStyles);
                    break;
                case "r":
                    AppendRun(reader, runs, imageParts, images);
                    break;
                case "hyperlink":
                    // Capture r:id BEFORE descending into the runs, then tag the runs the
                    // hyperlink contained with their resolved external URL.
                    string? rid = reader.GetAttribute("id", RelationshipsNs);
                    int from = runs.Count;
                    ReadRunsWithin(reader, "hyperlink", runs, imageParts, images);
                    if (rid is not null && linkRels.TryGetValue(rid, out string? url))
                    {
                        for (int k = from; k < runs.Count; k++)
                        {
                            runs[k] = runs[k] with { Href = url };
                        }
                    }

                    break;
            }
        }

        return new ParagraphContent(CoalesceRuns(runs), headingLevel, listKind, alignment,
            images.Count > 0 ? images[0] : null);
    }

    // Reads <w:pPr>: heading level (<w:pStyle>), list item (<w:numPr>), and alignment
    // (<w:jc>). Returns (headingLevel?, listKind?, alignment).
    private static (int? HeadingLevel, ListKind? ListKind, WordAlignment Alignment) ReadParagraphProperties(
        XmlReader reader, IReadOnlyDictionary<string, int> headingStyles)
    {
        if (reader.IsEmptyElement)
        {
            return (null, null, WordAlignment.Start);
        }

        int pprDepth = reader.Depth;
        int? headingLevel = null;
        bool isList = false;
        int numId = -1;
        int ilvl = 0;
        WordAlignment alignment = WordAlignment.Start;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == pprDepth
                && reader.LocalName == "pPr")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "pStyle":
                    string? styleId = reader.GetAttribute("val", WordprocessingMl);
                    headingLevel = HeadingLevelForStyle(styleId, headingStyles);
                    break;
                case "numPr":
                    isList = true;
                    break;
                case "numId" when isList:
                    // <w:numId w:val="N"> inside <w:numPr>; best-effort ordered guess.
                    if (int.TryParse(reader.GetAttribute("val", WordprocessingMl),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        numId = parsed;
                    }

                    break;
                case "ilvl" when isList:
                    // <w:ilvl w:val="N"> inside <w:numPr>: the 0-based nesting level.
                    if (int.TryParse(reader.GetAttribute("val", WordprocessingMl),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int lvl) && lvl > 0)
                    {
                        ilvl = lvl;
                    }

                    break;
                case "jc":
                    alignment = ParseJustification(reader.GetAttribute("val", WordprocessingMl));
                    break;
            }
        }

        if (isList)
        {
            // Heuristic without resolving numbering.xml: numId 0 (or absent) is the
            // "no numbering" sentinel Word uses for bullets in many documents; any
            // positive id is treated as ordered. Headings never coexist with lists here.
            bool ordered = numId > 0;
            return (null, new ListKind(ordered, ilvl), alignment);
        }

        return (headingLevel, null, alignment);
    }

    private static WordAlignment ParseJustification(string? val) => val?.ToLowerInvariant() switch
    {
        "center" => WordAlignment.Center,
        "end" or "right" => WordAlignment.End,
        "both" or "distribute" or "justify" => WordAlignment.Justify,
        _ => WordAlignment.Start,
    };

    // Maps a paragraph style id to a heading level (1-6), or null if it is not a
    // heading. Recognizes the conventional "Heading1".."Heading6" / "Title" ids first,
    // then falls back to the styles.xml-derived map for localized/custom ids.
    private static int? HeadingLevelForStyle(string? styleId, IReadOnlyDictionary<string, int> headingStyles)
    {
        if (string.IsNullOrEmpty(styleId))
        {
            return null;
        }

        if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(styleId.AsSpan("Heading".Length), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int level)
            && level is >= 1 and <= 6)
        {
            return level;
        }

        return headingStyles.TryGetValue(styleId, out int mapped) ? mapped : null;
    }

    // Reads one <w:r>: appends a WordRun carrying its concatenated <w:t> text plus
    // run formatting from <w:rPr>, and surfaces any embedded <w:drawing> image.
    private static void AppendRun(
        XmlReader reader, List<WordRun> runs,
        IReadOnlyDictionary<string, ImagePart> imageParts, List<WordImage> images)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        int runDepth = reader.Depth;
        RunFormat fmt = default;
        System.Text.StringBuilder? text = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == runDepth
                && reader.LocalName == "r")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            // <w:drawing> is in the Wordprocessing namespace; its blip/extent children are not.
            if (reader.LocalName == "drawing" && reader.NamespaceURI == WordprocessingMl)
            {
                WordImage? img = ReadDrawing(reader, imageParts);
                if (img is not null)
                {
                    images.Add(img);
                }

                continue;
            }

            if (reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "rPr":
                    fmt = ReadRunProperties(reader);
                    break;
                case "t":
                    (text ??= new System.Text.StringBuilder()).Append(ReadElementText(reader));
                    break;
                case "tab":
                    (text ??= new System.Text.StringBuilder()).Append('\t');
                    break;
                case "br":
                case "cr":
                    (text ??= new System.Text.StringBuilder()).Append('\n');
                    break;
            }
        }

        if (text is { Length: > 0 })
        {
            runs.Add(new WordRun(text.ToString(), fmt.Bold, fmt.Italic, fmt.Underline, fmt.Strike,
                Href: null, fmt.Color, fmt.Highlight, fmt.FontFamily, fmt.FontSizePoints, fmt.VerticalAlign));
        }
    }

    private readonly record struct ImagePart(byte[] Data, string ContentType);

    private const int EmuPerPixel = 9525;

    // Scans a <w:drawing> subtree for the blip's r:embed (-> image bytes), the extent
    // (-> px size), and the docPr description (-> alt text). The reader is on <w:drawing>.
    private static WordImage? ReadDrawing(XmlReader reader, IReadOnlyDictionary<string, ImagePart> imageParts)
    {
        if (reader.IsEmptyElement)
        {
            return null;
        }

        int depth = reader.Depth;
        string? rid = null;
        string? alt = null;
        int width = 0;
        int height = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == depth && reader.LocalName == "drawing")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "blip":
                    rid ??= reader.GetAttribute("embed", RelationshipsNs);
                    break;
                case "extent":
                    width = EmuToPx(reader.GetAttribute("cx"));
                    height = EmuToPx(reader.GetAttribute("cy"));
                    break;
                case "docPr":
                    alt ??= reader.GetAttribute("descr");
                    break;
            }
        }

        if (rid is not null && imageParts.TryGetValue(rid, out ImagePart part))
        {
            return new WordImage(part.Data, part.ContentType,
                string.IsNullOrEmpty(alt) ? null : alt, width, height);
        }

        return null;
    }

    private static int EmuToPx(string? emu) =>
        long.TryParse(emu, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) && v > 0
            ? (int)(v / EmuPerPixel)
            : 0;

    private readonly record struct RunFormat(
        bool Bold, bool Italic, bool Underline, bool Strike, string? Color, string? Highlight,
        string? FontFamily, double? FontSizePoints, WordVerticalAlign VerticalAlign);

    // Reads <w:rPr>: bold/italic/strike toggles, the w:u underline style, w:color (text),
    // and the highlight from w:shd's fill or a named w:highlight.
    private static RunFormat ReadRunProperties(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return default;
        }

        int rprDepth = reader.Depth;
        bool bold = false;
        bool italic = false;
        bool underline = false;
        bool strike = false;
        string? color = null;
        string? highlight = null;
        string? fontFamily = null;
        double? fontSizePoints = null;
        WordVerticalAlign vertAlign = WordVerticalAlign.Baseline;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == rprDepth
                && reader.LocalName == "rPr")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "b":
                    bold = IsToggleOn(reader.GetAttribute("val", WordprocessingMl));
                    break;
                case "i":
                    italic = IsToggleOn(reader.GetAttribute("val", WordprocessingMl));
                    break;
                case "u":
                    string? style = reader.GetAttribute("val", WordprocessingMl);
                    underline = style is not null
                        && !style.Equals("none", StringComparison.OrdinalIgnoreCase);
                    break;
                case "strike":
                    strike = IsToggleOn(reader.GetAttribute("val", WordprocessingMl));
                    break;
                case "color":
                    color = HexColor(reader.GetAttribute("val", WordprocessingMl));
                    break;
                case "shd":
                    highlight = HexColor(reader.GetAttribute("fill", WordprocessingMl)) ?? highlight;
                    break;
                case "highlight":
                    highlight = NamedHighlight(reader.GetAttribute("val", WordprocessingMl)) ?? highlight;
                    break;
                case "rFonts":
                    string? ascii = reader.GetAttribute("ascii", WordprocessingMl)
                        ?? reader.GetAttribute("hAnsi", WordprocessingMl);
                    if (!string.IsNullOrWhiteSpace(ascii))
                    {
                        fontFamily = ascii;
                    }

                    break;
                case "sz":
                    if (int.TryParse(reader.GetAttribute("val", WordprocessingMl), out int half) && half > 0)
                    {
                        fontSizePoints = half / 2.0; // OOXML w:sz is half-points
                    }

                    break;
                case "vertAlign":
                    vertAlign = reader.GetAttribute("val", WordprocessingMl) switch
                    {
                        "superscript" => WordVerticalAlign.Superscript,
                        "subscript" => WordVerticalAlign.Subscript,
                        _ => WordVerticalAlign.Baseline,
                    };
                    break;
            }
        }

        return new RunFormat(bold, italic, underline, strike, color, highlight,
            fontFamily, fontSizePoints, vertAlign);
    }

    // An OOXML RRGGBB value -> "#rrggbb"; "auto"/"none"/empty -> null.
    private static string? HexColor(string? val)
    {
        if (string.IsNullOrEmpty(val)
            || val.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || val.Equals("none", StringComparison.OrdinalIgnoreCase)
            || val.Length != 6)
        {
            return null;
        }

        foreach (char c in val)
        {
            if (!Uri.IsHexDigit(c))
            {
                return null;
            }
        }

        return "#" + val.ToLowerInvariant();
    }

    // The common named w:highlight values -> hex. Unknown names map to null.
    private static string? NamedHighlight(string? name) => name?.ToLowerInvariant() switch
    {
        "yellow" => "#ffff00",
        "green" => "#00ff00",
        "cyan" => "#00ffff",
        "magenta" => "#ff00ff",
        "blue" => "#0000ff",
        "red" => "#ff0000",
        "darkblue" => "#000080",
        "darkcyan" => "#008080",
        "darkgreen" => "#008000",
        "darkmagenta" => "#800080",
        "darkred" => "#800000",
        "darkyellow" => "#808000",
        "darkgray" => "#808080",
        "lightgray" => "#c0c0c0",
        "black" => "#000000",
        _ => null,
    };

    // An OOXML on/off toggle: absent attribute means "on"; explicit false/0/off mean off.
    private static bool IsToggleOn(string? val) =>
        val is null
        || !(val.Equals("false", StringComparison.OrdinalIgnoreCase)
             || val == "0"
             || val.Equals("off", StringComparison.OrdinalIgnoreCase));

    // Reads runs nested directly inside a container (e.g. <w:hyperlink>) into the
    // supplied list. The reader is positioned on the container start element.
    private static void ReadRunsWithin(
        XmlReader reader, string containerName, List<WordRun> runs,
        IReadOnlyDictionary<string, ImagePart> imageParts, List<WordImage> images)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        int depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == depth
                && reader.LocalName == containerName)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "r"
                && reader.NamespaceURI == WordprocessingMl)
            {
                AppendRun(reader, runs, imageParts, images);
            }
        }
    }

    // Reads one <w:tbl> into a WordTable of rows of cells. The reader is on the <w:tbl>
    // start element; on return it sits on the matching </w:tbl>.
    private static WordTable ReadTable(XmlReader reader, IReadOnlyDictionary<string, string> linkRels)
    {
        if (reader.IsEmptyElement)
        {
            return new WordTable([]);
        }

        int tableDepth = reader.Depth;
        List<WordTableRow> rows = [];

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == tableDepth
                && reader.LocalName == "tbl")
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "tr"
                && reader.NamespaceURI == WordprocessingMl)
            {
                rows.Add(ReadTableRow(reader, linkRels));
            }
        }

        return new WordTable(rows);
    }

    // Reads one <w:tr> into a row of cells.
    private static WordTableRow ReadTableRow(XmlReader reader, IReadOnlyDictionary<string, string> linkRels)
    {
        if (reader.IsEmptyElement)
        {
            return new WordTableRow([]);
        }

        int rowDepth = reader.Depth;
        List<WordTableCell> cells = [];

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == rowDepth
                && reader.LocalName == "tr")
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "tc"
                && reader.NamespaceURI == WordprocessingMl)
            {
                cells.Add(ReadTableCell(reader, linkRels));
            }
        }

        return new WordTableRow(cells);
    }

    // Reads one <w:tc>: gathers the runs of every paragraph it contains, joining
    // paragraph boundaries with a newline so multi-paragraph cells keep their breaks.
    private static WordTableCell ReadTableCell(XmlReader reader, IReadOnlyDictionary<string, string> linkRels)
    {
        if (reader.IsEmptyElement)
        {
            return new WordTableCell([]);
        }

        int cellDepth = reader.Depth;
        List<WordRun> runs = [];

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == cellDepth
                && reader.LocalName == "tc")
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.LocalName == "p"
                && reader.NamespaceURI == WordprocessingMl)
            {
                if (runs.Count > 0)
                {
                    runs.Add(new WordRun("\n"));
                }

                // Nested tables inside cells are not interpreted in v1; only paragraph
                // runs are gathered (headings/lists inside cells degrade to text).
                ParagraphContent para = ReadParagraph(reader, EmptyHeadingStyles, linkRels, EmptyImageParts);
                runs.AddRange(para.Runs);
            }
        }

        return new WordTableCell(CoalesceRuns(runs));
    }

}
