using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace BlazorDX.Documents;

/// <summary>
/// A tiny, dependency-free writer for the Office Open XML word-processing (.docx)
/// format — the inverse of <see cref="DocxReader"/> and the sibling of the
/// spreadsheet writer. It assembles the minimal set of OOXML parts (content types,
/// package relationships, the document body, a styles part, and a numbering part)
/// into a ZIP package by hand with <see cref="ZipArchive"/> and raw XML — no
/// third-party library, no reflection, AOT- and trim-safe, so it runs unchanged in
/// the browser WebAssembly runtime.
/// </summary>
/// <remarks>
/// <para>
/// The writer serializes the immutable <see cref="WordDocument"/> model back into
/// WordprocessingML, emitting exactly the constructs <see cref="DocxReader"/>
/// resolves so that <c>DocxReader.Read(DocxWriter.Write(doc))</c> reproduces
/// <paramref name="doc"/>'s structure:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="WordHeading"/> → a <c>&lt;w:p&gt;</c> carrying
///   <c>&lt;w:pStyle w:val="Heading1..6"&gt;</c> (level 1 is also reachable via the
///   <c>Title</c> style, but the writer uses <c>Heading1</c> for symmetry).</description></item>
///   <item><description><see cref="WordParagraph"/> → a <c>&lt;w:p&gt;</c> of runs,
///   each run emitting <c>&lt;w:b/&gt;</c>/<c>&lt;w:i/&gt;</c> when set.</description></item>
///   <item><description><see cref="WordList"/> → one <c>&lt;w:p&gt;</c> per item, each
///   carrying <c>&lt;w:numPr&gt;</c>. A bulleted list references <c>w:numId="0"</c>
///   (the reader's "no numbering" bullet sentinel); a numbered list references a
///   positive <c>w:numId</c>.</description></item>
///   <item><description><see cref="WordTable"/> → <c>&lt;w:tbl&gt;/&lt;w:tr&gt;/&lt;w:tc&gt;</c>;
///   the first row is the header row by position, as the reader expects.</description></item>
/// </list>
/// <para>
/// <b>Deferred / not round-tripped (mirrors the reader's own limits):</b> list
/// <em>nesting</em> (every item is a single top-level <c>&lt;li&gt;</c>); run
/// formatting beyond bold/italic (color, size, underline); images, hyperlinks,
/// footnotes, fields, and merged cells. These are dropped by the reader too, so the
/// model never carries them.
/// </para>
/// </remarks>
public static class DocxWriter
{
    private const string WordprocessingMl =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string RelationshipsNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // The numId a numbered list references. Any positive value reads back as ordered;
    // 0 is the reader's bullet sentinel. Both are declared in word/numbering.xml.
    private const int OrderedNumId = 1;
    private const int BulletNumId = 0;

    private const string XmlDeclaration =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    /// <summary>The IANA media type browsers expect for a .docx download.</summary>
    public const string MimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string ContentTypes =
        XmlDeclaration +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>" +
        "</Types>";

    private const string RootRels =
        XmlDeclaration +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"" + RelationshipsNs + "/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    // The fixed relationships every document carries. Hyperlink relationships (rId3+) are
    // appended per-document by BuildDocumentRels.
    private const string BaseDocumentRels =
        "<Relationship Id=\"rId1\" Type=\"" + RelationshipsNs + "/styles\" Target=\"styles.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"" + RelationshipsNs + "/numbering\" Target=\"numbering.xml\"/>";

    // Collects external hyperlink relationships while the body is written, assigning each
    // distinct URL a stable r:id (rId3, rId4, …) the body references via <w:hyperlink>.
    private sealed class LinkRels
    {
        private readonly Dictionary<string, string> _idByUrl = new(StringComparer.Ordinal);
        private readonly List<(string Id, string Target)> _all = [];
        private int _next = 3; // rId1 = styles, rId2 = numbering

        public string IdFor(string url)
        {
            if (!_idByUrl.TryGetValue(url, out string? id))
            {
                id = "rId" + _next++.ToString(CultureInfo.InvariantCulture);
                _idByUrl[url] = id;
                _all.Add((id, url));
            }

            return id;
        }

        public IReadOnlyList<(string Id, string Target)> All => _all;
    }

    private static string BuildDocumentRels(LinkRels links)
    {
        StringBuilder sb = new();
        sb.Append(XmlDeclaration);
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        sb.Append(BaseDocumentRels);
        foreach ((string id, string target) in links.All)
        {
            sb.Append("<Relationship Id=\"").Append(id)
              .Append("\" Type=\"").Append(RelationshipsNs).Append("/hyperlink\" Target=\"");
            AppendEscaped(sb, target);
            sb.Append("\" TargetMode=\"External\"/>");
        }

        sb.Append("</Relationships>");
        return sb.ToString();
    }

    // Heading1..6 + Title paragraph styles. Each declares <w:name> ("heading N" /
    // "Title") and <w:outlineLvl> so the reader's styles.xml-based resolution agrees
    // with the conventional "HeadingN" id check. outlineLvl is 0-based (level - 1).
    private static readonly string Styles = BuildStyles();

    // Number of nesting levels declared per list (0..MaxListLevels-1). Deeper items clamp
    // to the last level. Word needs each ilvl defined to render bullets/indents correctly.
    private const int MaxListLevels = 4;

    // Two abstract numberings (id 0 bulleted, id 1 decimal), each declaring MaxListLevels
    // indented levels so nested lists render in Word; the reader keys on numId + ilvl.
    private static readonly string Numbering = BuildNumbering();

    private static string BuildNumbering()
    {
        StringBuilder sb = new();
        sb.Append(XmlDeclaration).Append("<w:numbering xmlns:w=\"").Append(WordprocessingMl).Append("\">");

        string[] bullets = ["•", "◦", "▪", "‣"];
        sb.Append("<w:abstractNum w:abstractNumId=\"0\">");
        for (int lvl = 0; lvl < MaxListLevels; lvl++)
        {
            AppendListLevel(sb, lvl, "bullet", bullets[lvl % bullets.Length]);
        }

        sb.Append("</w:abstractNum><w:abstractNum w:abstractNumId=\"1\">");
        for (int lvl = 0; lvl < MaxListLevels; lvl++)
        {
            AppendListLevel(sb, lvl, "decimal",
                "%" + (lvl + 1).ToString(CultureInfo.InvariantCulture) + ".");
        }

        sb.Append("</w:abstractNum>");
        sb.Append("<w:num w:numId=\"0\"><w:abstractNumId w:val=\"0\"/></w:num>");
        sb.Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"1\"/></w:num>");
        sb.Append("</w:numbering>");
        return sb.ToString();
    }

    private static void AppendListLevel(StringBuilder sb, int ilvl, string numFmt, string lvlText)
    {
        int indent = (ilvl + 1) * 720; // 0.5" per level
        sb.Append("<w:lvl w:ilvl=\"").Append(ilvl.ToString(CultureInfo.InvariantCulture))
          .Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"").Append(numFmt)
          .Append("\"/><w:lvlText w:val=\"").Append(lvlText)
          .Append("\"/><w:pPr><w:ind w:left=\"").Append(indent.ToString(CultureInfo.InvariantCulture))
          .Append("\" w:hanging=\"360\"/></w:pPr></w:lvl>");
    }

    /// <summary>
    /// Serializes a <see cref="WordDocument"/> into a valid <c>.docx</c> byte array
    /// that <see cref="DocxReader"/> reads back to an equivalent model.
    /// </summary>
    /// <param name="document">The document model to serialize.</param>
    /// <returns>The raw bytes of a <c>.docx</c> package.</returns>
    public static byte[] Write(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LinkRels links = new();
        string documentXml = BuildDocument(document, links);

        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", ContentTypes);
            AddEntry(zip, "_rels/.rels", RootRels);
            AddEntry(zip, "word/_rels/document.xml.rels", BuildDocumentRels(links));
            AddEntry(zip, "word/document.xml", documentXml);
            AddEntry(zip, "word/styles.xml", Styles);
            AddEntry(zip, "word/numbering.xml", Numbering);
        }

        return stream.ToArray();
    }

    private static string BuildDocument(WordDocument document, LinkRels links)
    {
        StringBuilder sb = new();
        sb.Append(XmlDeclaration);
        // xmlns:r is needed for the r:id attribute on <w:hyperlink>.
        sb.Append("<w:document xmlns:w=\"").Append(WordprocessingMl)
          .Append("\" xmlns:r=\"").Append(RelationshipsNs).Append("\"><w:body>");

        foreach (WordBlock block in document.Blocks)
        {
            switch (block)
            {
                case WordHeading heading:
                    AppendHeading(sb, heading, links);
                    break;
                case WordParagraph paragraph:
                    AppendParagraph(sb, paragraph.Runs, pPr: null, links, paragraph.Alignment);
                    break;
                case WordList list:
                    AppendList(sb, list, links);
                    break;
                case WordTable table:
                    AppendTable(sb, table, links);
                    break;
            }
        }

        sb.Append("</w:body></w:document>");
        return sb.ToString();
    }

    private static void AppendHeading(StringBuilder sb, WordHeading heading, LinkRels links)
    {
        // Clamp to the styles we define (1-6). The reader maps "HeadingN" directly.
        int level = Math.Clamp(heading.Level, 1, 6);
        string pPr = "<w:pStyle w:val=\"Heading" +
            level.ToString(CultureInfo.InvariantCulture) + "\"/>";
        AppendParagraph(sb, heading.Runs, pPr, links, heading.Alignment);
    }

    private static void AppendList(StringBuilder sb, WordList list, LinkRels links)
    {
        int numId = list.Ordered ? OrderedNumId : BulletNumId;
        string numIdText = numId.ToString(CultureInfo.InvariantCulture);

        for (int i = 0; i < list.Items.Count; i++)
        {
            int level = Math.Clamp(list.LevelOf(i), 0, MaxListLevels - 1);
            string pPr =
                "<w:numPr><w:ilvl w:val=\"" + level.ToString(CultureInfo.InvariantCulture) +
                "\"/><w:numId w:val=\"" + numIdText + "\"/></w:numPr>";
            AppendParagraph(sb, list.Items[i], pPr, links);
        }
    }

    // OOXML colors are 6-hex-digit RRGGBB with no leading '#'.
    private static string HexValue(string color) => color.TrimStart('#').ToUpperInvariant();

    private static string? JustificationElement(WordAlignment alignment) => alignment switch
    {
        WordAlignment.Center => "<w:jc w:val=\"center\"/>",
        WordAlignment.End => "<w:jc w:val=\"end\"/>",
        WordAlignment.Justify => "<w:jc w:val=\"both\"/>",
        _ => null,
    };

    // Emits one <w:p>: optional <w:pPr> (style/numbering + justification) then its runs. A
    // run carrying an Href is wrapped in <w:hyperlink r:id="…"> referencing an external rel.
    private static void AppendParagraph(
        StringBuilder sb, IReadOnlyList<WordRun> runs, string? pPr, LinkRels links,
        WordAlignment alignment = WordAlignment.Start)
    {
        sb.Append("<w:p>");
        string? jc = JustificationElement(alignment);
        if (pPr is not null || jc is not null)
        {
            sb.Append("<w:pPr>");
            if (pPr is not null)
            {
                sb.Append(pPr);
            }

            if (jc is not null)
            {
                sb.Append(jc);
            }

            sb.Append("</w:pPr>");
        }

        foreach (WordRun run in runs)
        {
            if (!string.IsNullOrEmpty(run.Href))
            {
                sb.Append("<w:hyperlink r:id=\"").Append(links.IdFor(run.Href)).Append("\">");
                AppendRun(sb, run);
                sb.Append("</w:hyperlink>");
            }
            else
            {
                AppendRun(sb, run);
            }
        }

        sb.Append("</w:p>");
    }

    private static void AppendRun(StringBuilder sb, WordRun run)
    {
        sb.Append("<w:r>");
        bool hasColor = !string.IsNullOrEmpty(run.Color);
        bool hasHighlight = !string.IsNullOrEmpty(run.Highlight);
        if (run.Bold || run.Italic || run.Underline || run.Strike || hasColor || hasHighlight)
        {
            sb.Append("<w:rPr>");
            if (run.Bold)
            {
                sb.Append("<w:b/>");
            }

            if (run.Italic)
            {
                sb.Append("<w:i/>");
            }

            if (run.Underline)
            {
                sb.Append("<w:u w:val=\"single\"/>");
            }

            if (run.Strike)
            {
                sb.Append("<w:strike/>");
            }

            if (hasColor)
            {
                sb.Append("<w:color w:val=\"").Append(HexValue(run.Color!)).Append("\"/>");
            }

            if (hasHighlight)
            {
                // Arbitrary background via shading fill (w:highlight only allows named colors).
                sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"")
                  .Append(HexValue(run.Highlight!)).Append("\"/>");
            }

            sb.Append("</w:rPr>");
        }

        // xml:space="preserve" keeps leading/trailing whitespace the reader needs to
        // reproduce the run text exactly.
        sb.Append("<w:t xml:space=\"preserve\">");
        AppendEscaped(sb, run.Text ?? string.Empty);
        sb.Append("</w:t></w:r>");
    }

    private static void AppendTable(StringBuilder sb, WordTable table, LinkRels links)
    {
        sb.Append("<w:tbl>");
        foreach (WordTableRow row in table.Rows)
        {
            sb.Append("<w:tr>");
            foreach (WordTableCell cell in row.Cells)
            {
                sb.Append("<w:tc>");
                // A cell is a paragraph of runs (the reader gathers paragraph runs).
                AppendParagraph(sb, cell.Runs, pPr: null, links);
                sb.Append("</w:tc>");
            }

            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
    }

    private static string BuildStyles()
    {
        StringBuilder sb = new();
        sb.Append(XmlDeclaration);
        sb.Append("<w:styles xmlns:w=\"").Append(WordprocessingMl).Append("\">");

        for (int level = 1; level <= 6; level++)
        {
            string id = "Heading" + level.ToString(CultureInfo.InvariantCulture);
            string name = "heading " + level.ToString(CultureInfo.InvariantCulture);
            int outline = level - 1;
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"").Append(id).Append("\">");
            sb.Append("<w:name w:val=\"").Append(name).Append("\"/>");
            sb.Append("<w:pPr><w:outlineLvl w:val=\"")
              .Append(outline.ToString(CultureInfo.InvariantCulture)).Append("\"/></w:pPr>");
            sb.Append("</w:style>");
        }

        // Title maps to heading level 1 in the reader.
        sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Title\">");
        sb.Append("<w:name w:val=\"Title\"/>");
        sb.Append("</w:style>");

        sb.Append("</w:styles>");
        return sb.ToString();
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
                case '"': sb.Append("&quot;"); break;
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
