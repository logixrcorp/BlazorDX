using System.Globalization;
using BlazorDX.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Htmx;

/// <summary>The document kind a <see cref="DxHtmxDocumentViewer"/> renders.</summary>
public enum HtmxDocumentKind
{
    /// <summary>A PDF, shown in the browser's native viewer via <c>&lt;embed&gt;</c> + a download link.</summary>
    Pdf,

    /// <summary>An <c>.xlsx</c> workbook, parsed server-side and rendered as a table grid with sheet tabs.</summary>
    Excel,

    /// <summary>A <c>.docx</c> document, parsed server-side and rendered as semantic HTML.</summary>
    Word,
}

/// <summary>
/// A <b>no-WASM, no-circuit, read-only</b> document viewer for the static-SSR + HTMX
/// tier. It parses the document <em>on the server</em> with the dependency-free
/// <see cref="XlsxReader"/> / <see cref="DocxReader"/> and renders <b>real semantic
/// elements straight to the Blazor render tree</b> — <c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>,
/// <c>&lt;p&gt;</c>, <c>&lt;strong&gt;</c>/<c>&lt;em&gt;</c>, <c>&lt;ul&gt;</c>/
/// <c>&lt;ol&gt;</c>/<c>&lt;li&gt;</c>, a <c>&lt;table&gt;</c> with <c>&lt;th scope&gt;</c>/
/// <c>&lt;td&gt;</c>, and <c>&lt;embed&gt;</c> for PDF. There is no
/// <c>MarkupString</c> and no hand-built HTML string anywhere; all text flows through
/// <see cref="RenderTreeBuilder.AddContent(int, string?)"/> (HTML-encoded).
/// </summary>
/// <remarks>
/// <para>
/// <b>Progressive enhancement is the whole point.</b> The sheet tabs and the paging
/// controls are <em>real anchors</em> carrying both an <c>href</c> (so a browser with
/// JavaScript disabled full-page-navigates and re-renders the chosen sheet/page) and
/// matching <c>hx-get</c>/<c>hx-target</c>/<c>hx-swap</c> attributes (so HTMX, when
/// present, swaps just this viewer's fragment instead). The no-JS path is the
/// conformance floor: everything is readable and navigable without a single byte of
/// script (WCAG 1.3.1 / 1.3.2 / 2.4.6).
/// </para>
/// <para>
/// The component is identical whether it serves a full page or an HTMX fragment: the
/// host endpoint renders the same <see cref="DxHtmxDocumentViewer"/> with different
/// <see cref="SheetIndex"/> / <see cref="Page"/> values and either embeds it in the
/// page (normal request) or returns it alone (HX request) — the root element carries a
/// stable <c>id</c> so <c>hx-target="this"</c> / <c>hx-swap="outerHTML"</c> replaces it
/// in place. Zero reflection, AOT/trim-safe, constant render-tree sequence numbers.
/// </para>
/// </remarks>
public sealed class DxHtmxDocumentViewer : ComponentBase
{
    /// <summary>The kind of document to render. Required.</summary>
    [Parameter, EditorRequired] public HtmxDocumentKind Kind { get; set; }

    /// <summary>
    /// The raw document bytes, parsed server-side. Required for
    /// <see cref="HtmxDocumentKind.Excel"/> and <see cref="HtmxDocumentKind.Word"/>;
    /// ignored for <see cref="HtmxDocumentKind.Pdf"/> (which uses <see cref="Source"/>).
    /// </summary>
    [Parameter] public byte[]? Bytes { get; set; }

    /// <summary>
    /// For <see cref="HtmxDocumentKind.Pdf"/>: the URL of the PDF to embed and to offer
    /// as a download. Validated against a scheme allowlist before use.
    /// </summary>
    [Parameter] public string? Source { get; set; }

    /// <summary>A display name for the document (PDF frame title, download filename, grid label).</summary>
    [Parameter] public string Name { get; set; } = "Document";

    /// <summary>
    /// The endpoint the sheet tabs / paging controls navigate to (both as an
    /// <c>href</c> and as an <c>hx-get</c>). Query parameters <c>sheet</c> and
    /// <c>page</c> are appended. When empty, links fall back to the current path.
    /// </summary>
    [Parameter] public string Endpoint { get; set; } = string.Empty;

    /// <summary>The active Excel sheet index (0-based). Bound from the <c>sheet</c> query parameter.</summary>
    [Parameter] public int SheetIndex { get; set; }

    /// <summary>
    /// The active page (0-based) for paged Excel rows / Word blocks. Bound from the
    /// <c>page</c> query parameter. Paging only engages when the content exceeds
    /// <see cref="PageSize"/>.
    /// </summary>
    [Parameter] public int Page { get; set; }

    /// <summary>
    /// Rows per page (Excel) / blocks per page (Word). <c>0</c> (the default) disables
    /// paging — the whole sheet/document renders at once.
    /// </summary>
    [Parameter] public int PageSize { get; set; }

    /// <summary>Optional extra CSS class on the viewer root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// A stable DOM id for the viewer root, so HTMX can target it
    /// (<c>hx-target="this"</c> with <c>hx-swap="outerHTML"</c>) and replace the
    /// fragment in place. Defaults to <c>dx-htmxdoc</c>.
    /// </summary>
    [Parameter] public string ElementId { get; set; } = "dx-htmxdoc";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", ElementId);
        builder.AddAttribute(2, "class", $"dx-htmxdoc {Class}".TrimEnd());
        builder.AddAttribute(3, "data-doc-kind", Kind.ToString());

        switch (Kind)
        {
            case HtmxDocumentKind.Pdf:
                BuildPdf(builder);
                break;
            case HtmxDocumentKind.Excel:
                BuildExcel(builder);
                break;
            case HtmxDocumentKind.Word:
                BuildWord(builder);
                break;
        }

        builder.CloseElement();
    }

    // ----- PDF -------------------------------------------------------------

    private void BuildPdf(RenderTreeBuilder builder)
    {
        if (!IsSafeSource(Source))
        {
            BuildUnavailable(builder, "PDF source unavailable.");
            return;
        }

        string source = Source!.Trim();

        // Native browser PDF viewer. The non-empty `title` gives the embedded frame an
        // accessible name (WCAG 4.1.2). No bundled PDF engine, no JS.
        builder.OpenElement(100, "embed");
        builder.AddAttribute(101, "class", "dx-htmxdoc-pdf");
        builder.AddAttribute(102, "type", "application/pdf");
        builder.AddAttribute(103, "src", source);
        builder.AddAttribute(104, "title", FrameTitle(Name));
        builder.CloseElement();

        // Accessible fallback: a real link so AT users and browsers without an inline
        // PDF plugin can still obtain the file. Mirrors DxDocumentViewer, minus the
        // interactive toolbar (this tier is read-only and script-free).
        builder.OpenElement(105, "p");
        builder.AddAttribute(106, "class", "dx-htmxdoc-pdf-fallback");
        builder.AddContent(107, "Can't see the PDF above? ");
        builder.OpenElement(108, "a");
        builder.AddAttribute(109, "class", "dx-htmxdoc-download");
        builder.AddAttribute(110, "href", source);
        builder.AddAttribute(111, "download", Name);
        builder.AddAttribute(112, "rel", "noopener noreferrer");
        builder.AddContent(113, $"Download {Name}");
        builder.CloseElement();
        builder.AddContent(114, ".");
        builder.CloseElement();
    }

    // ----- Excel -----------------------------------------------------------

    private void BuildExcel(RenderTreeBuilder builder)
    {
        if (Bytes is null || Bytes.Length == 0)
        {
            BuildUnavailable(builder, "No workbook to display.");
            return;
        }

        Workbook workbook = XlsxReader.Read(Bytes);
        IReadOnlyList<Worksheet> sheets = workbook.Sheets;
        if (sheets.Count == 0)
        {
            BuildUnavailable(builder, "This workbook has no sheets.");
            return;
        }

        int active = Math.Clamp(SheetIndex, 0, sheets.Count - 1);
        BuildSheetTabs(builder, sheets, active);
        BuildSheetTable(builder, sheets[active], active);
    }

    // Sheet tabs are a nav of REAL anchors: each carries an href (no-JS full navigation)
    // AND hx-get/hx-target/hx-swap (HTMX swaps just this fragment). aria-current marks
    // the active sheet so the link role conveys selection without JS.
    private void BuildSheetTabs(RenderTreeBuilder builder, IReadOnlyList<Worksheet> sheets, int active)
    {
        if (sheets.Count <= 1)
        {
            return;
        }

        builder.OpenElement(120, "nav");
        builder.AddAttribute(121, "class", "dx-htmxdoc-tabs");
        builder.AddAttribute(122, "aria-label", "Worksheets");

        for (int i = 0; i < sheets.Count; i++)
        {
            bool selected = i == active;
            string url = BuildUrl(i, 0);

            builder.OpenElement(123, "a");
            builder.SetKey(sheets[i]);
            builder.AddAttribute(124, "class", selected ? "dx-htmxdoc-tab dx-htmxdoc-tab-selected" : "dx-htmxdoc-tab");
            builder.AddAttribute(125, "href", url);
            AddHxAttributes(builder, 126, url);
            if (selected)
            {
                builder.AddAttribute(131, "aria-current", "true");
            }

            builder.AddContent(132, sheets[i].Name);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildSheetTable(RenderTreeBuilder builder, Worksheet sheet, int sheetIndex)
    {
        builder.OpenElement(140, "div");
        builder.AddAttribute(141, "class", "dx-htmxdoc-panel");
        builder.AddAttribute(142, "role", "region");
        builder.AddAttribute(143, "aria-label", $"{sheet.Name} worksheet");
        builder.AddAttribute(144, "tabindex", "-1");

        if (sheet.Rows.Count == 0)
        {
            BuildUnavailable(builder, "This sheet is empty.");
            builder.CloseElement();
            return;
        }

        IReadOnlyList<string> header = sheet.Rows[0];
        int dataRowCount = sheet.Rows.Count - 1;

        // Window the DATA rows when paging is on; the header always renders.
        int firstDataRow = 0;
        int dataRowsThisPage = dataRowCount;
        int pageCount = 1;
        if (PageSize > 0 && dataRowCount > PageSize)
        {
            pageCount = (dataRowCount + PageSize - 1) / PageSize;
            int page = Math.Clamp(Page, 0, pageCount - 1);
            firstDataRow = page * PageSize;
            dataRowsThisPage = Math.Min(PageSize, dataRowCount - firstDataRow);
        }

        builder.OpenElement(145, "table");
        builder.AddAttribute(146, "class", "dx-htmxdoc-grid");
        builder.AddAttribute(147, "role", "grid");
        builder.AddAttribute(148, "aria-readonly", "true");
        builder.AddAttribute(149, "aria-rowcount", sheet.Rows.Count.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(150, "aria-colcount", sheet.ColumnCount.ToString(CultureInfo.InvariantCulture));

        // Header row: one <th scope="col"> per column (WCAG 1.3.1).
        builder.OpenElement(151, "thead");
        builder.OpenElement(152, "tr");
        builder.SetKey("head");
        for (int c = 0; c < sheet.ColumnCount; c++)
        {
            builder.OpenElement(153, "th");
            builder.SetKey(c);
            builder.AddAttribute(154, "scope", "col");
            builder.AddContent(155, c < header.Count ? header[c] : string.Empty);
            builder.CloseElement();
        }

        builder.CloseElement(); // tr
        builder.CloseElement(); // thead

        builder.OpenElement(156, "tbody");
        for (int r = 0; r < dataRowsThisPage; r++)
        {
            // sheet.Rows[0] is the header; data row `firstDataRow + r` lives at index +1.
            IReadOnlyList<string> row = sheet.Rows[firstDataRow + r + 1];
            builder.OpenElement(157, "tr");
            builder.SetKey(firstDataRow + r);

            // Row-label gutter: a row header cell (WCAG 1.3.1).
            builder.OpenElement(158, "th");
            builder.AddAttribute(159, "scope", "row");
            builder.AddAttribute(160, "class", "dx-htmxdoc-rowlabel");
            builder.AddContent(161, (firstDataRow + r + 1).ToString(CultureInfo.InvariantCulture));
            builder.CloseElement();

            for (int c = 0; c < sheet.ColumnCount; c++)
            {
                builder.OpenElement(162, "td");
                builder.SetKey(c);
                builder.AddContent(163, c < row.Count ? row[c] : string.Empty);
                builder.CloseElement();
            }

            builder.CloseElement(); // tr
        }

        builder.CloseElement(); // tbody
        builder.CloseElement(); // table

        BuildPager(builder, sheetIndex, pageCount, $"rows {firstDataRow + 1}–{firstDataRow + dataRowsThisPage} of {dataRowCount}");
        builder.CloseElement(); // panel
    }

    // ----- Word ------------------------------------------------------------

    private void BuildWord(RenderTreeBuilder builder)
    {
        if (Bytes is null || Bytes.Length == 0)
        {
            BuildUnavailable(builder, "No document to display.");
            return;
        }

        WordDocument document = DocxReader.Read(Bytes);
        IReadOnlyList<WordBlock> blocks = document.Blocks;

        // A focusable document region: role="document" lets screen-reader users drop
        // into normal reading mode; tabindex makes it keyboard-reachable (WCAG 2.1.1).
        builder.OpenElement(200, "div");
        builder.AddAttribute(201, "class", "dx-htmxdoc-word");
        builder.AddAttribute(202, "role", "document");
        builder.AddAttribute(203, "aria-label", Name);
        builder.AddAttribute(204, "tabindex", "0");

        if (blocks.Count == 0)
        {
            BuildUnavailable(builder, "No document to display.");
            builder.CloseElement();
            return;
        }

        int firstBlock = 0;
        int blocksThisPage = blocks.Count;
        int pageCount = 1;
        if (PageSize > 0 && blocks.Count > PageSize)
        {
            pageCount = (blocks.Count + PageSize - 1) / PageSize;
            int page = Math.Clamp(Page, 0, pageCount - 1);
            firstBlock = page * PageSize;
            blocksThisPage = Math.Min(PageSize, blocks.Count - firstBlock);
        }

        for (int i = 0; i < blocksThisPage; i++)
        {
            WordBlock block = blocks[firstBlock + i];
            switch (block)
            {
                case WordHeading heading:
                    BuildHeading(builder, heading);
                    break;
                case WordList list:
                    BuildList(builder, list);
                    break;
                case WordTable table:
                    BuildWordTable(builder, table);
                    break;
                case WordParagraph paragraph:
                    BuildParagraph(builder, paragraph);
                    break;
            }
        }

        builder.CloseElement(); // document region

        BuildPager(builder, SheetIndex, pageCount, $"blocks {firstBlock + 1}–{firstBlock + blocksThisPage} of {blocks.Count}");
    }

    private static void BuildHeading(RenderTreeBuilder builder, WordHeading heading)
    {
        int level = Math.Clamp(heading.Level, 1, 6);
        builder.OpenElement(210, HeadingTag(level));
        builder.AddAttribute(211, "class", "dx-htmxdoc-heading");
        BuildRuns(builder, heading.Runs);
        builder.CloseElement();
    }

    private static void BuildParagraph(RenderTreeBuilder builder, WordParagraph paragraph)
    {
        builder.OpenElement(220, "p");
        builder.AddAttribute(221, "class", "dx-htmxdoc-para");
        BuildRuns(builder, paragraph.Runs);
        builder.CloseElement();
    }

    private static void BuildList(RenderTreeBuilder builder, WordList list)
    {
        builder.OpenElement(230, list.Ordered ? "ol" : "ul");
        builder.AddAttribute(231, "class", "dx-htmxdoc-list");
        for (int i = 0; i < list.Items.Count; i++)
        {
            builder.OpenElement(232, "li");
            builder.SetKey(i);
            BuildRuns(builder, list.Items[i]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static void BuildWordTable(RenderTreeBuilder builder, WordTable table)
    {
        builder.OpenElement(240, "table");
        builder.AddAttribute(241, "class", "dx-htmxdoc-wordtable");

        IReadOnlyList<WordTableRow> rows = table.Rows;
        if (rows.Count == 0)
        {
            builder.CloseElement();
            return;
        }

        // First row is the header: a <thead> of <th scope="col"> (WCAG 1.3.1).
        builder.OpenElement(242, "thead");
        builder.OpenElement(243, "tr");
        builder.SetKey("head");
        foreach (WordTableCell cell in rows[0].Cells)
        {
            builder.OpenElement(244, "th");
            builder.AddAttribute(245, "scope", "col");
            BuildRuns(builder, cell.Runs);
            builder.CloseElement();
        }

        builder.CloseElement(); // tr
        builder.CloseElement(); // thead

        if (rows.Count > 1)
        {
            builder.OpenElement(246, "tbody");
            for (int r = 1; r < rows.Count; r++)
            {
                builder.OpenElement(247, "tr");
                builder.SetKey(r);
                foreach (WordTableCell cell in rows[r].Cells)
                {
                    builder.OpenElement(248, "td");
                    BuildRuns(builder, cell.Runs);
                    builder.CloseElement();
                }

                builder.CloseElement(); // tr
            }

            builder.CloseElement(); // tbody
        }

        builder.CloseElement(); // table
    }

    // Renders a run sequence. An unstyled run is bare text (no fake wrapper); bold wraps
    // in <strong>, italic in <em>, both nest. All text goes through AddContent so it is
    // HTML-encoded — no MarkupString, XSS-safe.
    private static void BuildRuns(RenderTreeBuilder builder, IReadOnlyList<WordRun> runs)
    {
        for (int i = 0; i < runs.Count; i++)
        {
            WordRun run = runs[i];

            if (!run.Bold && !run.Italic)
            {
                builder.AddContent(250, run.Text);
                continue;
            }

            if (run.Bold)
            {
                builder.OpenElement(251, "strong");
            }

            if (run.Italic)
            {
                builder.OpenElement(252, "em");
            }

            builder.AddContent(253, run.Text);

            if (run.Italic)
            {
                builder.CloseElement();
            }

            if (run.Bold)
            {
                builder.CloseElement();
            }
        }
    }

    // ----- Shared bits -----------------------------------------------------

    // A "Previous"/"Next" pager: real anchors carrying href (no-JS) + hx-get (HTMX).
    // Only rendered when there is more than one page. The status text is an aria-live
    // polite region so an HTMX swap announces the new range (WCAG 4.1.3).
    private void BuildPager(RenderTreeBuilder builder, int sheetIndex, int pageCount, string status)
    {
        if (pageCount <= 1)
        {
            return;
        }

        int page = Math.Clamp(Page, 0, pageCount - 1);

        builder.OpenElement(300, "nav");
        builder.AddAttribute(301, "class", "dx-htmxdoc-pager");
        builder.AddAttribute(302, "aria-label", "Pagination");

        BuildPagerLink(builder, 303, sheetIndex, page - 1, page > 0, "← Previous");

        builder.OpenElement(310, "span");
        builder.AddAttribute(311, "class", "dx-htmxdoc-pageinfo");
        builder.AddAttribute(312, "aria-live", "polite");
        builder.AddContent(313, $"Page {page + 1} of {pageCount} ({status})");
        builder.CloseElement();

        BuildPagerLink(builder, 320, sheetIndex, page + 1, page < pageCount - 1, "Next →");

        builder.CloseElement();
    }

    private void BuildPagerLink(RenderTreeBuilder builder, int seq, int sheetIndex, int targetPage, bool enabled, string label)
    {
        if (!enabled)
        {
            // Disabled control: a non-focusable span, not a dead link.
            builder.OpenElement(seq, "span");
            builder.AddAttribute(seq + 1, "class", "dx-htmxdoc-page dx-htmxdoc-page-disabled");
            builder.AddAttribute(seq + 2, "aria-disabled", "true");
            builder.AddContent(seq + 3, label);
            builder.CloseElement();
            return;
        }

        string url = BuildUrl(sheetIndex, targetPage);
        builder.OpenElement(seq, "a");
        builder.AddAttribute(seq + 1, "class", "dx-htmxdoc-page");
        builder.AddAttribute(seq + 2, "href", url);
        AddHxAttributes(builder, seq + 3, url);
        builder.AddContent(seq + 9, label);
        builder.CloseElement();
    }

    // The dual-mode enhancement on every navigation control. The server endpoint
    // returns the FULL page (so the bare href works with JS off); when HTMX is present
    // it issues the GET, pulls only this viewer fragment out of the response with
    // hx-select, swaps it in place (hx-target/hx-swap), and updates the address bar
    // (hx-push-url) so back/forward and bookmarking behave. One URL, both paths.
    private void AddHxAttributes(RenderTreeBuilder builder, int seq, string url)
    {
        builder.AddAttribute(seq, "hx-get", url);
        builder.AddAttribute(seq + 1, "hx-target", $"#{ElementId}");
        builder.AddAttribute(seq + 2, "hx-select", $"#{ElementId}");
        builder.AddAttribute(seq + 3, "hx-swap", "outerHTML");
        builder.AddAttribute(seq + 4, "hx-push-url", "true");
        // Opt these links out of Blazor's enhanced navigation so htmx (or, with JS off,
        // a plain full navigation) owns the click — the two must not both intercept it.
        builder.AddAttribute(seq + 5, "data-enhance-nav", "false");
    }

    private static void BuildUnavailable(RenderTreeBuilder builder, string message)
    {
        builder.OpenElement(400, "p");
        builder.AddAttribute(401, "class", "dx-htmxdoc-empty");
        builder.AddContent(402, message);
        builder.CloseElement();
    }

    // Builds the endpoint URL with sheet/page query params. The base comes from
    // Endpoint (which may already carry a query string, e.g. "/htmx/doc?kind=excel");
    // sheet/page are appended with the correct separator. Values are invariant integers,
    // so no further encoding is required.
    private string BuildUrl(int sheet, int page)
    {
        string path = Endpoint;
        char sep = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{path}{sep}sheet={sheet}&page={page}");
    }

    // The embedded frame must always carry a non-empty accessible name (WCAG 4.1.2).
    private static string FrameTitle(string name) =>
        string.IsNullOrWhiteSpace(name) ? "PDF document" : name;

    private static string HeadingTag(int level) => level switch
    {
        1 => "h1",
        2 => "h2",
        3 => "h3",
        4 => "h4",
        5 => "h5",
        _ => "h6",
    };

    /// <summary>
    /// Decides whether a PDF <see cref="Source"/> is safe to place in an
    /// <c>&lt;embed src&gt;</c> / <c>&lt;a href&gt;</c>. Allows relative URLs and a
    /// scheme allowlist (http, https, blob, and <c>data:application/pdf</c>); rejects
    /// everything else — notably <c>javascript:</c>, <c>vbscript:</c>, <c>file:</c>, and
    /// HTML data URLs — to prevent script injection through the source value.
    /// </summary>
    public static bool IsSafeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string value = source.Trim();

        int colon = value.IndexOf(':');
        if (colon <= 0)
        {
            // No colon (relative path) is safe; a leading colon is not.
            return colon != 0;
        }

        string scheme = value[..colon];
        if (!IsSchemeToken(scheme))
        {
            // The colon is part of a relative path, not a scheme. Safe.
            return true;
        }

        switch (scheme.ToLowerInvariant())
        {
            case "http":
            case "https":
            case "blob":
                return true;
            case "data":
                return value[(colon + 1)..].TrimStart()
                    .StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private static bool IsSchemeToken(string s)
    {
        if (s.Length == 0 || !char.IsLetter(s[0]))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
