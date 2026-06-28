namespace BlazorDX.Documents;

/// <summary>
/// A parsed word-processing document: the ordered top-level blocks read from a
/// <c>.docx</c> file. This is <see cref="DocxReader"/>'s output and
/// <c>DxWordViewer</c>'s input — a plain, immutable semantic model with no
/// reflection and no live file handle. The block order is the document's reading
/// order (WCAG 1.3.2).
/// </summary>
/// <param name="Blocks">The body blocks, top to bottom, in document order.</param>
public sealed record WordDocument(IReadOnlyList<WordBlock> Blocks);

/// <summary>
/// One top-level block of a <see cref="WordDocument"/>. The hierarchy is closed and
/// pattern-matched by the viewer (no reflection): <see cref="WordHeading"/>,
/// <see cref="WordParagraph"/>, <see cref="WordList"/>, and <see cref="WordTable"/>.
/// Unknown constructs degrade to a <see cref="WordParagraph"/> of their plain text.
/// </summary>
public abstract record WordBlock;

/// <summary>
/// A heading paragraph (mapped from a <c>Heading1</c>–<c>Heading6</c> or <c>Title</c>
/// paragraph style). <see cref="Level"/> is clamped to 1–6 and drives the rendered
/// <c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c> element so the document has a real, ordered
/// heading outline (WCAG 1.3.1 / 2.4.6). A <c>Title</c> maps to level 1.
/// </summary>
/// <param name="Level">The heading level, 1 (most prominent) through 6.</param>
/// <param name="Runs">The inline runs making up the heading text.</param>
/// <param name="Alignment">Paragraph alignment (maps to <c>&lt;w:jc&gt;</c> / <c>text-align</c>).</param>
public sealed record WordHeading(
    int Level, IReadOnlyList<WordRun> Runs, WordAlignment Alignment = WordAlignment.Start) : WordBlock;

/// <summary>Horizontal alignment of a block. <see cref="Start"/> is the default (unset).</summary>
public enum WordAlignment
{
    /// <summary>Leading edge (left in LTR). The default; emits no markup.</summary>
    Start,

    /// <summary>Centered.</summary>
    Center,

    /// <summary>Trailing edge (right in LTR).</summary>
    End,

    /// <summary>Justified (both edges).</summary>
    Justify,
}

/// <summary>
/// A body paragraph: a sequence of inline <see cref="WordRun"/>s rendered as a
/// <c>&lt;p&gt;</c>. An empty run list is a blank paragraph.
/// </summary>
/// <param name="Runs">The inline runs making up the paragraph text, in order.</param>
/// <param name="Alignment">Paragraph alignment (maps to <c>&lt;w:jc&gt;</c> / <c>text-align</c>).</param>
public sealed record WordParagraph(
    IReadOnlyList<WordRun> Runs, WordAlignment Alignment = WordAlignment.Start) : WordBlock;

/// <summary>
/// A list: either bulleted (<c>&lt;ul&gt;</c>) or numbered (<c>&lt;ol&gt;</c>), with
/// each item a sequence of inline runs. Nesting is flattened in v1 — every item is a
/// top-level <c>&lt;li&gt;</c> (see <see cref="DocxReader"/> remarks).
/// </summary>
/// <param name="Ordered"><see langword="true"/> for a numbered list; otherwise bulleted.</param>
/// <param name="Items">The list items, in order; each is its own run sequence.</param>
/// <param name="Levels">
/// Optional 0-based indent level per item (parallel to <paramref name="Items"/>), so nested
/// lists round-trip (<c>&lt;w:ilvl&gt;</c> / nested <c>&lt;ul&gt;</c>). <see langword="null"/>
/// or a missing index means level 0 (flat). The whole list shares one <paramref name="Ordered"/>
/// kind; per-level ordering is not modeled.
/// </param>
public sealed record WordList(
    bool Ordered,
    IReadOnlyList<IReadOnlyList<WordRun>> Items,
    IReadOnlyList<int>? Levels = null) : WordBlock
{
    /// <summary>The 0-based indent level of item <paramref name="index"/> (0 when unset).</summary>
    public int LevelOf(int index) =>
        Levels is not null && index >= 0 && index < Levels.Count ? Math.Max(0, Levels[index]) : 0;
}

/// <summary>
/// A table of rows of cells. The first row is treated as a header row: its cells
/// render as <c>&lt;th scope="col"&gt;</c>, the rest as <c>&lt;td&gt;</c> (WCAG 1.3.1).
/// Each cell carries its own inline runs.
/// </summary>
/// <param name="Rows">The rows, top to bottom; the first is the header row.</param>
public sealed record WordTable(IReadOnlyList<WordTableRow> Rows) : WordBlock;

/// <summary>One table row: its ordered cells.</summary>
/// <param name="Cells">The cells, left to right.</param>
public sealed record WordTableRow(IReadOnlyList<WordTableCell> Cells);

/// <summary>One table cell: the inline runs of its (first) paragraph's text.</summary>
/// <param name="Runs">The inline runs making up the cell text.</param>
public sealed record WordTableCell(IReadOnlyList<WordRun> Runs);

/// <summary>
/// An embedded image (block-level). Carries the raw bytes and media type so it can be
/// written as a <c>.docx</c> media part + <c>&lt;w:drawing&gt;</c> and rendered as a
/// <c>data:</c> URL in HTML. <see cref="Width"/>/<see cref="Height"/> are CSS pixels
/// (0 = unknown).
/// </summary>
/// <param name="Data">The raw image bytes.</param>
/// <param name="ContentType">The IANA media type, e.g. <c>image/png</c> or <c>image/jpeg</c>.</param>
/// <param name="AltText">Accessible description (WCAG 1.1.1), or null.</param>
/// <param name="Width">Display width in CSS pixels, or 0 if unknown.</param>
/// <param name="Height">Display height in CSS pixels, or 0 if unknown.</param>
public sealed record WordImage(
    byte[] Data, string ContentType, string? AltText = null, int Width = 0, int Height = 0) : WordBlock;

/// <summary>
/// An inline run of text carrying best-effort character formatting. A run with no
/// emphasis (all flags <see langword="false"/>) renders as bare text — the viewer never
/// fakes a semantic element for an unstyled run.
/// </summary>
/// <param name="Text">The run's literal text.</param>
/// <param name="Bold">Whether the run is bold (<c>&lt;w:b&gt;</c> / <c>&lt;strong&gt;</c>).</param>
/// <param name="Italic">Whether the run is italic (<c>&lt;w:i&gt;</c> / <c>&lt;em&gt;</c>).</param>
/// <param name="Underline">Whether the run is underlined (<c>&lt;w:u&gt;</c> / <c>&lt;u&gt;</c>).</param>
/// <param name="Strike">Whether the run is struck through (<c>&lt;w:strike&gt;</c> / <c>&lt;s&gt;</c>).</param>
/// <param name="Href">
/// When non-null, the run is a hyperlink to this URL (<c>&lt;w:hyperlink&gt;</c> /
/// <c>&lt;a href&gt;</c>). Only <c>http</c>/<c>https</c>/<c>mailto</c> URLs survive parsing.
/// </param>
/// <param name="Color">Text color as <c>#RRGGBB</c> (<c>&lt;w:color&gt;</c> / CSS <c>color</c>), or null.</param>
/// <param name="Highlight">Highlight/background as <c>#RRGGBB</c> (<c>&lt;w:shd&gt;</c> / CSS <c>background-color</c>), or null.</param>
/// <param name="FontFamily">Font family, e.g. <c>Arial</c> (<c>&lt;w:rFonts&gt;</c> / CSS <c>font-family</c>), or null for the default.</param>
/// <param name="FontSizePoints">Font size in points, e.g. <c>12</c> (<c>&lt;w:sz&gt;</c> half-points / CSS <c>font-size:…pt</c>), or null.</param>
/// <param name="VerticalAlign">Baseline (default), superscript, or subscript (<c>&lt;w:vertAlign&gt;</c> / <c>&lt;sup&gt;</c>/<c>&lt;sub&gt;</c>).</param>
public sealed record WordRun(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strike = false,
    string? Href = null,
    string? Color = null,
    string? Highlight = null,
    string? FontFamily = null,
    double? FontSizePoints = null,
    WordVerticalAlign VerticalAlign = WordVerticalAlign.Baseline);

/// <summary>Baseline (normal), superscript, or subscript positioning of a run's text.</summary>
public enum WordVerticalAlign
{
    /// <summary>Normal baseline (the default; emits no markup).</summary>
    Baseline,

    /// <summary>Raised, smaller text (<c>&lt;sup&gt;</c> / <c>w:vertAlign="superscript"</c>).</summary>
    Superscript,

    /// <summary>Lowered, smaller text (<c>&lt;sub&gt;</c> / <c>w:vertAlign="subscript"</c>).</summary>
    Subscript,
}
