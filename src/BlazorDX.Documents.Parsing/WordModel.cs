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
public sealed record WordHeading(int Level, IReadOnlyList<WordRun> Runs) : WordBlock;

/// <summary>
/// A body paragraph: a sequence of inline <see cref="WordRun"/>s rendered as a
/// <c>&lt;p&gt;</c>. An empty run list is a blank paragraph.
/// </summary>
/// <param name="Runs">The inline runs making up the paragraph text, in order.</param>
public sealed record WordParagraph(IReadOnlyList<WordRun> Runs) : WordBlock;

/// <summary>
/// A list: either bulleted (<c>&lt;ul&gt;</c>) or numbered (<c>&lt;ol&gt;</c>), with
/// each item a sequence of inline runs. Nesting is flattened in v1 — every item is a
/// top-level <c>&lt;li&gt;</c> (see <see cref="DocxReader"/> remarks).
/// </summary>
/// <param name="Ordered"><see langword="true"/> for a numbered list; otherwise bulleted.</param>
/// <param name="Items">The list items, in order; each is its own run sequence.</param>
public sealed record WordList(bool Ordered, IReadOnlyList<IReadOnlyList<WordRun>> Items) : WordBlock;

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
/// An inline run of text carrying best-effort character formatting. A run with no
/// emphasis (<see cref="Bold"/> and <see cref="Italic"/> both <see langword="false"/>)
/// renders as bare text — the viewer never fakes a semantic element for an unstyled
/// run.
/// </summary>
/// <param name="Text">The run's literal text.</param>
/// <param name="Bold">Whether the run is bold (<c>&lt;w:b&gt;</c>).</param>
/// <param name="Italic">Whether the run is italic (<c>&lt;w:i&gt;</c>).</param>
public sealed record WordRun(string Text, bool Bold = false, bool Italic = false);
