using System.Globalization;
using System.Text;

namespace BlazorDX.Documents;

/// <summary>
/// A bidirectional, dependency-free mapper between the <see cref="WordDocument"/> model
/// and clean semantic HTML — the inverse pair of <see cref="DocxReader"/>/
/// <see cref="DocxWriter"/> but for the browser editing surface rather than the
/// <c>.docx</c> wire format. It backs the upcoming Word editor: <see cref="ToHtml"/>
/// produces the markup loaded into a <c>contentEditable</c> region, and
/// <see cref="FromHtml"/> parses the (often messy) markup that region emits back into the
/// model so it can be saved via <see cref="DocxWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both directions are hand-rolled over <see cref="System.Text"/> plus a small lenient
/// character scan — no HTML library, no third-party NuGet, no reflection — so the type is
/// AOT- and trim-safe and runs unchanged in the browser WebAssembly runtime. All text is
/// HTML-escaped on the way out; nothing is ever emitted raw, so the output cannot inject
/// markup.
/// </para>
/// <para>
/// The mapping mirrors <c>DxWordViewer</c>'s element vocabulary exactly:
/// <see cref="WordHeading"/> level <c>N</c> → <c>&lt;hN&gt;</c>; <see cref="WordParagraph"/>
/// → <c>&lt;p&gt;</c> with runs as bare text / <c>&lt;strong&gt;</c> / <c>&lt;em&gt;</c> /
/// nested <c>&lt;strong&gt;&lt;em&gt;</c>; <see cref="WordList"/> →
/// <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c> of <c>&lt;li&gt;</c>; <see cref="WordTable"/> →
/// <c>&lt;table&gt;</c> whose first row is <c>&lt;th scope="col"&gt;</c> and whose body
/// cells are <c>&lt;td&gt;</c>.
/// </para>
/// <para>
/// <b>Round-trip contract:</b> <c>FromHtml(ToHtml(doc))</c> reproduces
/// <paramref name="doc"/>'s structure — heading levels, paragraph text and bold/italic
/// runs, list kind and items, and table cells including the header row. As with the
/// <c>.docx</c> reader, <em>adjacent runs that share formatting coalesce</em> (e.g. a
/// plain run immediately followed by another plain run merges into one), so the run
/// <em>count</em> may shrink even though the rendered text and emphasis are identical.
/// </para>
/// <para>
/// <b>Leniency (parsing only):</b> <see cref="FromHtml"/> never throws. It accepts
/// upper/mixed-case tags, treats <c>&lt;b&gt;</c> as <c>&lt;strong&gt;</c> and <c>&lt;i&gt;</c>
/// as <c>&lt;em&gt;</c>, maps <c>&lt;div&gt;</c> and a bare <c>&lt;br&gt;</c> to paragraph
/// breaks, decodes HTML entities, and degrades unknown or non-inline tags to their text
/// content. Malformed, unclosed, or empty input yields a best-effort (possibly empty)
/// document rather than an error.
/// </para>
/// </remarks>
public static partial class WordHtml
{
    // ---------------------------------------------------------------------
    // ToHtml: model -> semantic HTML
    // ---------------------------------------------------------------------

    /// <summary>
    /// Serializes a <see cref="WordDocument"/> to clean semantic HTML. Every block becomes
    /// a real element and all text is HTML-escaped, so the result is safe to load directly
    /// into a <c>contentEditable</c> surface.
    /// </summary>
    /// <param name="document">The document model to serialize.</param>
    /// <returns>The HTML markup; an empty string for a document with no blocks.</returns>
    public static string ToHtml(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        StringBuilder sb = new();
        foreach (WordBlock block in document.Blocks)
        {
            switch (block)
            {
                case WordHeading heading:
                    AppendHeading(sb, heading);
                    break;
                case WordList list:
                    AppendList(sb, list);
                    break;
                case WordTable table:
                    AppendTable(sb, table);
                    break;
                case WordParagraph paragraph:
                    AppendParagraph(sb, paragraph);
                    break;
            }
        }

        return sb.ToString();
    }

    private static void AppendHeading(StringBuilder sb, WordHeading heading)
    {
        int level = Math.Clamp(heading.Level, 1, 6);
        string tag = "h" + level.ToString(CultureInfo.InvariantCulture);
        sb.Append('<').Append(tag).Append(AlignStyle(heading.Alignment)).Append('>');
        AppendRuns(sb, heading.Runs);
        sb.Append("</").Append(tag).Append('>');
    }

    private static void AppendParagraph(StringBuilder sb, WordParagraph paragraph)
    {
        sb.Append("<p").Append(AlignStyle(paragraph.Alignment)).Append('>');
        AppendRuns(sb, paragraph.Runs);
        sb.Append("</p>");
    }

    // A text-align inline style for a non-default alignment, or "" for Start.
    private static string AlignStyle(WordAlignment alignment) => alignment switch
    {
        WordAlignment.Center => " style=\"text-align:center\"",
        WordAlignment.End => " style=\"text-align:right\"",
        WordAlignment.Justify => " style=\"text-align:justify\"",
        _ => string.Empty,
    };

    private static void AppendList(StringBuilder sb, WordList list)
    {
        string tag = list.Ordered ? "ol" : "ul";
        sb.Append('<').Append(tag).Append('>');
        foreach (IReadOnlyList<WordRun> item in list.Items)
        {
            sb.Append("<li>");
            AppendRuns(sb, item);
            sb.Append("</li>");
        }

        sb.Append("</").Append(tag).Append('>');
    }

    private static void AppendTable(StringBuilder sb, WordTable table)
    {
        sb.Append("<table>");
        IReadOnlyList<WordTableRow> rows = table.Rows;
        for (int r = 0; r < rows.Count; r++)
        {
            sb.Append("<tr>");
            // The first row is the header row: its cells are <th scope="col">. This
            // mirrors DxWordViewer and is what FromHtml keys on to rebuild the row split.
            string cellTag = r == 0 ? "th" : "td";
            foreach (WordTableCell cell in rows[r].Cells)
            {
                if (r == 0)
                {
                    sb.Append("<th scope=\"col\">");
                }
                else
                {
                    sb.Append("<td>");
                }

                AppendRuns(sb, cell.Runs);
                sb.Append("</").Append(cellTag).Append('>');
            }

            sb.Append("</tr>");
        }

        sb.Append("</table>");
    }

    // Renders a run sequence. An unstyled run is bare (escaped) text; bold wraps in
    // <strong>, italic in <em>, both nest <strong><em>. Matches DxWordViewer.BuildRuns.
    private static void AppendRuns(StringBuilder sb, IReadOnlyList<WordRun> runs)
    {
        foreach (WordRun run in runs)
        {
            bool bold = run.Bold;
            bool italic = run.Italic;
            bool underline = run.Underline;
            bool strike = run.Strike;
            bool link = !string.IsNullOrEmpty(run.Href);

            if (link)
            {
                sb.Append("<a href=\"");
                AppendEscaped(sb, run.Href!);
                sb.Append("\">");
            }

            if (bold)
            {
                sb.Append("<strong>");
            }

            if (italic)
            {
                sb.Append("<em>");
            }

            if (underline)
            {
                sb.Append("<u>");
            }

            if (strike)
            {
                sb.Append("<s>");
            }

            AppendEscaped(sb, run.Text ?? string.Empty);

            if (strike)
            {
                sb.Append("</s>");
            }

            if (underline)
            {
                sb.Append("</u>");
            }

            if (italic)
            {
                sb.Append("</em>");
            }

            if (bold)
            {
                sb.Append("</strong>");
            }

            if (link)
            {
                sb.Append("</a>");
            }
        }
    }

    // HTML text escaping. & < > are escaped always; " is escaped too so the same routine
    // is safe in an attribute context if reused. Control chars XML 1.0 forbids are dropped
    // (tab/newline/return kept), matching DocxWriter's escaper.
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
                    if (c >= 0x20 || c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }
    }

    // ---------------------------------------------------------------------
    // FromHtml: lenient HTML -> model
    // ---------------------------------------------------------------------

    /// <summary>
    /// Parses editor/<c>contentEditable</c> HTML back into a <see cref="WordDocument"/>,
    /// supporting the subset <see cref="ToHtml"/> emits. Lenient by design: it never
    /// throws, accepts messy/mixed-case/unclosed markup, treats <c>&lt;b&gt;</c>/<c>&lt;i&gt;</c>
    /// as <c>&lt;strong&gt;</c>/<c>&lt;em&gt;</c>, maps <c>&lt;div&gt;</c>/<c>&lt;br&gt;</c> to
    /// paragraph breaks, decodes entities, and degrades unknown tags to their text content.
    /// </summary>
    /// <param name="html">The HTML to parse; <see langword="null"/>/empty yields an empty document.</param>
    /// <returns>The parsed document; never <see langword="null"/>.</returns>
    public static WordDocument FromHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return new WordDocument([]);
        }

        List<Token> tokens = Tokenize(html);
        Parser parser = new(tokens);
        return new WordDocument(parser.Parse());
    }

    // A token is either literal text or a tag. We keep the scan deliberately small and
    // forgiving: anything that does not parse as a clean tag is treated as text.
    private readonly record struct Token(bool IsTag, string Name, bool IsClose, bool SelfClose, string Text);

    // Upper bound on the number of tokens we produce. contentEditable HTML is small;
    // a pathologically nested or huge hostile input (e.g. <strong> repeated millions of
    // times) must degrade gracefully instead of growing the token list — and the run
    // buffer it feeds — without limit. Once the cap is hit the remainder of the input is
    // appended as a single literal-text token so no content is silently dropped.
    private const int MaxTokens = 1_000_000;

    // Splits HTML into a flat token stream of text and tags. Unterminated '<' is treated
    // as literal text; comments and the like degrade to text harmlessly.
    private static List<Token> Tokenize(string html)
    {
        List<Token> tokens = [];
        int i = 0;
        int n = html.Length;

        while (i < n)
        {
            // Bound pathological input: stop tokenizing past the cap and flush the rest
            // as one text token so the parser still terminates in linear time.
            if (tokens.Count >= MaxTokens)
            {
                tokens.Add(new Token(false, string.Empty, false, false, DecodeEntities(html.AsSpan(i))));
                break;
            }

            char c = html[i];
            if (c == '<')
            {
                int close = html.IndexOf('>', i + 1);
                if (close <= i)
                {
                    // No closing '>' (or one that does not advance past the '<'): the
                    // rest is literal text. Guarding against close <= i keeps the
                    // Substring length below from ever going negative on input like "<>".
                    tokens.Add(new Token(false, string.Empty, false, false, DecodeEntities(html.AsSpan(i))));
                    break;
                }

                string raw = html.Substring(i + 1, close - i - 1);
                i = close + 1;

                // Drop comments / doctype / processing instructions entirely.
                if (raw.StartsWith('!') || raw.StartsWith('?'))
                {
                    continue;
                }

                ParseTag(raw, out string name, out bool isClose, out bool selfClose);
                if (name.Length == 0)
                {
                    continue;
                }

                // Keep the raw attribute text on opening tags so the parser can read href
                // off an <a>. Closing tags carry none. (Text is otherwise unused for tags.)
                tokens.Add(new Token(true, name, isClose, selfClose, isClose ? string.Empty : raw));
            }
            else
            {
                int next = html.IndexOf('<', i);
                if (next < 0)
                {
                    next = n;
                }

                tokens.Add(new Token(false, string.Empty, false, false, DecodeEntities(html.AsSpan(i, next - i))));
                i = next;
            }
        }

        return tokens;
    }

    // Extracts the lower-cased tag name and the close/self-close flags from raw tag inner
    // text (everything between '<' and '>'). Attributes are ignored.
    private static void ParseTag(string raw, out string name, out bool isClose, out bool selfClose)
    {
        isClose = false;
        selfClose = raw.EndsWith('/');

        int start = 0;
        if (start < raw.Length && raw[start] == '/')
        {
            isClose = true;
            start++;
        }

        int end = start;
        while (end < raw.Length)
        {
            char c = raw[end];
            if (char.IsWhiteSpace(c) || c == '/' || c == '>')
            {
                break;
            }

            end++;
        }

        name = end > start
            ? raw.Substring(start, end - start).ToLowerInvariant()
            : string.Empty;
    }

    private enum BlockKind
    {
        Paragraph,
        Heading,
        UnorderedList,
        OrderedList,
        Table,
    }

    // The streaming parser. It walks the token stream once, maintaining the current block
    // context and the current run-formatting depth (bold/italic counts) so nested or
    // sloppily-closed emphasis still produces the right flags.
    private sealed class Parser(List<Token> tokens)
    {
        private readonly List<Token> _tokens = tokens;
        private readonly List<WordBlock> _blocks = [];

        // Current paragraph/heading run buffer and its formatting state.
        private readonly List<WordRun> _runs = [];
        private int _bold;
        private int _italic;
        private int _underline;
        private int _strike;
        private string? _href;
        private WordAlignment _alignment;

        // Current block context.
        private BlockKind _kind = BlockKind.Paragraph;
        private int _headingLevel = 1;

        // Active list, if any.
        private bool _listOrdered;
        private List<IReadOnlyList<WordRun>>? _listItems;
        private bool _inListItem;

        // Active table, if any.
        private List<WordTableRow>? _tableRows;
        private List<WordTableCell>? _rowCells;
        private bool _inCell;

        public List<WordBlock> Parse()
        {
            foreach (Token token in _tokens)
            {
                if (token.IsTag)
                {
                    HandleTag(token);
                }
                else if (token.Text.Length > 0)
                {
                    HandleText(token.Text);
                }
            }

            // Flush whatever is still open at end of input (lenient: unclosed is fine).
            FlushParagraph();
            FlushList();
            FlushTable();
            return _blocks;
        }

        private void HandleText(string text)
        {
            // Inside a table cell or list item, text accumulates into the active run
            // buffer (the cell/item shares _runs). Outside any block it begins an
            // implicit paragraph. Whitespace-only stray text between block tags is
            // ignored so layout whitespace in the markup does not create empty runs.
            if (_runs.Count == 0 && !_inCell && !_inListItem
                && _kind == BlockKind.Paragraph && IsWhitespace(text))
            {
                return;
            }

            AddRun(text);
        }

        private void AddRun(string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            bool bold = _bold > 0;
            bool italic = _italic > 0;
            bool underline = _underline > 0;
            bool strike = _strike > 0;

            // Coalesce with the previous run when formatting matches (mirrors the .docx
            // reader, keeping the model compact and the round-trip stable).
            if (_runs.Count > 0)
            {
                WordRun last = _runs[^1];
                if (last.Bold == bold && last.Italic == italic
                    && last.Underline == underline && last.Strike == strike
                    && last.Href == _href)
                {
                    _runs[^1] = last with { Text = last.Text + text };
                    return;
                }
            }

            _runs.Add(new WordRun(text, bold, italic, underline, strike, _href));
        }

        private void HandleTag(Token tag)
        {
            switch (tag.Name)
            {
                case "strong" or "b":
                    if (tag.IsClose)
                    {
                        if (_bold > 0)
                        {
                            _bold--;
                        }
                    }
                    else if (!tag.SelfClose)
                    {
                        _bold++;
                    }

                    break;

                case "em" or "i":
                    if (tag.IsClose)
                    {
                        if (_italic > 0)
                        {
                            _italic--;
                        }
                    }
                    else if (!tag.SelfClose)
                    {
                        _italic++;
                    }

                    break;

                case "u" or "ins":
                    if (tag.IsClose)
                    {
                        if (_underline > 0)
                        {
                            _underline--;
                        }
                    }
                    else if (!tag.SelfClose)
                    {
                        _underline++;
                    }

                    break;

                case "s" or "strike" or "del":
                    if (tag.IsClose)
                    {
                        if (_strike > 0)
                        {
                            _strike--;
                        }
                    }
                    else if (!tag.SelfClose)
                    {
                        _strike++;
                    }

                    break;

                case "a":
                    if (tag.IsClose)
                    {
                        _href = null;
                    }
                    else if (!tag.SelfClose)
                    {
                        // Only safe schemes survive; an unsafe/missing href leaves the
                        // text un-linked rather than dropping it.
                        _href = SanitizeUrl(ExtractHref(tag.Text));
                    }

                    break;

                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    if (tag.IsClose)
                    {
                        FlushParagraph();
                    }
                    else
                    {
                        StartBlock(BlockKind.Heading);
                        _headingLevel = tag.Name[1] - '0';
                        _alignment = ParseAlignment(tag.Text);
                    }

                    break;

                case "p":
                    if (tag.IsClose)
                    {
                        FlushParagraph();
                    }
                    else
                    {
                        StartBlock(BlockKind.Paragraph);
                        _alignment = ParseAlignment(tag.Text);
                    }

                    break;

                case "div":
                    // contentEditable often uses <div> as a line/paragraph wrapper. Treat
                    // both edges as paragraph breaks so each div line stands alone.
                    FlushParagraph();
                    if (!tag.IsClose && _listItems is null && _tableRows is null)
                    {
                        StartBlock(BlockKind.Paragraph);
                        _alignment = ParseAlignment(tag.Text);
                    }

                    break;

                case "br":
                    // A line break ends the current paragraph (we have no intra-paragraph
                    // break in the model). Only meaningful outside lists/tables.
                    if (_listItems is null && _tableRows is null)
                    {
                        FlushParagraph();
                    }

                    break;

                case "ul" or "ol":
                    if (tag.IsClose)
                    {
                        FlushList();
                    }
                    else
                    {
                        FlushParagraph();
                        FlushList();
                        _listOrdered = tag.Name == "ol";
                        _listItems = [];
                        _kind = _listOrdered ? BlockKind.OrderedList : BlockKind.UnorderedList;
                    }

                    break;

                case "li":
                    if (tag.IsClose)
                    {
                        CloseListItem();
                    }
                    else
                    {
                        // A new <li> without a closing one before it: close the prior item.
                        CloseListItem();
                        _listItems ??= [];
                        _inListItem = true;
                        ResetRuns();
                    }

                    break;

                case "table":
                    if (tag.IsClose)
                    {
                        FlushTable();
                    }
                    else
                    {
                        FlushParagraph();
                        FlushList();
                        FlushTable();
                        _tableRows = [];
                        _kind = BlockKind.Table;
                    }

                    break;

                case "tr":
                    if (tag.IsClose)
                    {
                        CloseRow();
                    }
                    else if (_tableRows is not null)
                    {
                        CloseRow();
                        _rowCells = [];
                    }

                    break;

                case "th" or "td":
                    if (tag.IsClose)
                    {
                        CloseCell();
                    }
                    else if (_tableRows is not null)
                    {
                        CloseCell();
                        _rowCells ??= [];
                        _inCell = true;
                        ResetRuns();
                    }

                    break;

                // Any other tag (span, font, a, unknown, ...) is transparent: its text
                // content flows through to the current run buffer. Emphasis carried by
                // such tags is not recognized, which is the documented lenient behavior.
                default:
                    break;
            }
        }

        // Starts a fresh top-level block context (paragraph or heading), flushing any
        // paragraph already in progress. Not used for list/table which manage their own.
        private void StartBlock(BlockKind kind)
        {
            FlushParagraph();
            _kind = kind;
            ResetRuns();
        }

        private void ResetRuns()
        {
            _runs.Clear();
            _bold = 0;
            _italic = 0;
            _underline = 0;
            _strike = 0;
            _href = null;
            _alignment = WordAlignment.Start;
        }


        private void FlushParagraph()
        {
            // Only emit a standalone paragraph/heading when we are not inside a list item
            // or table cell (those own the run buffer and flush it themselves).
            if (_inListItem || _inCell)
            {
                return;
            }

            if (_kind == BlockKind.Heading)
            {
                _blocks.Add(new WordHeading(Math.Clamp(_headingLevel, 1, 6), Snapshot(), _alignment));
                _kind = BlockKind.Paragraph;
                ResetRuns();
                return;
            }

            if (_kind is BlockKind.UnorderedList or BlockKind.OrderedList or BlockKind.Table)
            {
                // Inside a list/table shell but not in an item/cell: discard stray runs.
                ResetRuns();
                return;
            }

            if (_runs.Count > 0)
            {
                _blocks.Add(new WordParagraph(Snapshot(), _alignment));
            }

            ResetRuns();
        }

        private void CloseListItem()
        {
            if (!_inListItem)
            {
                return;
            }

            _listItems ??= [];
            _listItems.Add(Snapshot());
            _inListItem = false;
            ResetRuns();
        }

        private void FlushList()
        {
            CloseListItem();
            if (_listItems is { Count: > 0 })
            {
                _blocks.Add(new WordList(_listOrdered, _listItems));
            }

            _listItems = null;
            _kind = BlockKind.Paragraph;
            ResetRuns();
        }

        private void CloseCell()
        {
            if (!_inCell)
            {
                return;
            }

            _rowCells ??= [];
            _rowCells.Add(new WordTableCell(Snapshot()));
            _inCell = false;
            ResetRuns();
        }

        private void CloseRow()
        {
            CloseCell();
            if (_rowCells is not null)
            {
                _tableRows ??= [];
                _tableRows.Add(new WordTableRow(_rowCells));
                _rowCells = null;
            }
        }

        private void FlushTable()
        {
            CloseRow();
            if (_tableRows is { Count: > 0 })
            {
                _blocks.Add(new WordTable(_tableRows));
            }

            _tableRows = null;
            _kind = BlockKind.Paragraph;
            ResetRuns();
        }

        // Returns a copy of the current run buffer (the buffer is reused across blocks).
        private IReadOnlyList<WordRun> Snapshot() =>
            _runs.Count == 0 ? [] : new List<WordRun>(_runs);

        private static bool IsWhitespace(string text)
        {
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    return false;
                }
            }

            return true;
        }
    }

    // ---------------------------------------------------------------------
    // Entity decoding
    // ---------------------------------------------------------------------

    // Decodes the HTML entities ToHtml emits plus the common named ones and numeric
    // (decimal/hex) references. Unknown entities are left verbatim (lenient).
    private static string DecodeEntities(ReadOnlySpan<char> span)
    {
        int amp = span.IndexOf('&');
        if (amp < 0)
        {
            return span.ToString();
        }

        StringBuilder sb = new(span.Length);
        int i = 0;
        while (i < span.Length)
        {
            char c = span[i];
            if (c != '&')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int semi = -1;
            int limit = Math.Min(span.Length, i + 12);
            for (int j = i + 1; j < limit; j++)
            {
                if (span[j] == ';')
                {
                    semi = j;
                    break;
                }
            }

            if (semi < 0)
            {
                sb.Append(c);
                i++;
                continue;
            }

            ReadOnlySpan<char> entity = span[(i + 1)..semi];
            if (TryDecodeEntity(entity, out string decoded))
            {
                sb.Append(decoded);
                i = semi + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool TryDecodeEntity(ReadOnlySpan<char> entity, out string decoded)
    {
        decoded = string.Empty;
        if (entity.Length == 0)
        {
            return false;
        }

        if (entity[0] == '#')
        {
            return TryDecodeNumeric(entity[1..], out decoded);
        }

        // Named entities are case-sensitive in HTML; compare ordinally.
        switch (entity)
        {
            case "amp": decoded = "&"; return true;
            case "lt": decoded = "<"; return true;
            case "gt": decoded = ">"; return true;
            case "quot": decoded = "\""; return true;
            case "apos": decoded = "'"; return true;
            case "nbsp": decoded = " "; return true;
            default: return false;
        }
    }

    private static bool TryDecodeNumeric(ReadOnlySpan<char> digits, out string decoded)
    {
        decoded = string.Empty;
        if (digits.Length == 0)
        {
            return false;
        }

        int code;
        if (digits[0] is 'x' or 'X')
        {
            if (!int.TryParse(digits[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
            {
                return false;
            }
        }
        else if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
        {
            return false;
        }

        if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
        {
            return false;
        }

        decoded = char.ConvertFromUtf32(code);
        return true;
    }
}
