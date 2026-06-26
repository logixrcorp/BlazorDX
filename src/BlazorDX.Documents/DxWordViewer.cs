using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Documents;

/// <summary>
/// Tier 2 styled, read-only word-processing document viewer over a parsed
/// <see cref="WordDocument"/>. Renders each block as a <b>real semantic element</b> —
/// <c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>, <c>&lt;p&gt;</c>, <c>&lt;strong&gt;</c>/
/// <c>&lt;em&gt;</c>, <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c>/<c>&lt;li&gt;</c>, and a
/// <c>&lt;table&gt;</c> with <c>&lt;th scope&gt;</c>/<c>&lt;td&gt;</c> — straight to the
/// Blazor render tree. Styling is CSS-variable driven (see dx-word.css).
/// </summary>
/// <remarks>
/// <para>
/// The rendering rule is the whole accessibility point of this component: every block
/// becomes a true semantic element and all text flows through
/// <see cref="RenderTreeBuilder.AddContent(int, string?)"/> (HTML-encoded). There is
/// no <c>MarkupString</c> and no hand-built HTML string anywhere — output is therefore
/// both XSS-safe and the most accessible form for assistive technology: a genuine
/// heading outline (WCAG 1.3.1 / 2.4.6), lists exposed as lists, table headers carrying
/// <c>scope</c>, and DOM order equal to the document's reading order (WCAG 1.3.2).
/// </para>
/// <para>
/// Unlike <see cref="DxSpreadsheetViewer"/>, the document is rendered <em>in full</em>
/// rather than virtualized: windowing top-level blocks would put a scroll container
/// between the document landmark and its headings/lists/tables, breaking the heading
/// outline and reading order that are this viewer's reason to exist. Correctness over
/// windowing for v1 (the prompt sanctions this). There is likewise no separate Tier 1
/// primitive — the content is static, so there is no headless state worth splitting
/// out; this single styled component is the whole surface.
/// </para>
/// </remarks>
public sealed class DxWordViewer : ComponentBase
{
    /// <summary>The parsed document to display. Blocks render in document order.</summary>
    [Parameter] public WordDocument? Document { get; set; }

    /// <summary>Optional extra CSS class on the viewer root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Accessible name for the document region (the <c>aria-label</c> on the
    /// focusable <c>role="document"</c> container). Defaults to "Document".
    /// </summary>
    [Parameter] public string Label { get; set; } = "Document";

    private IReadOnlyList<WordBlock> Blocks => Document?.Blocks ?? [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // A focusable document region: keyboard users can land on the document and
        // scroll it (WCAG 2.1.1); role="document" lets screen-reader users drop out of
        // any surrounding application mode into normal browse/reading mode.
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-word-viewer {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "document");
        builder.AddAttribute(3, "aria-label", Label);
        builder.AddAttribute(4, "tabindex", "0");

        if (Blocks.Count == 0)
        {
            builder.OpenElement(5, "p");
            builder.AddAttribute(6, "class", "dx-word-empty");
            builder.AddContent(7, "No document to display.");
            builder.CloseElement();
            builder.CloseElement();
            return;
        }

        foreach (WordBlock block in Blocks)
        {
            // One constant sequence-number region per block kind. Sibling blocks of the
            // same kind reuse the same constants — the diff is keyed by position, which
            // is stable because blocks render in document order and never reorder.
            switch (block)
            {
                case WordHeading heading:
                    BuildHeading(builder, heading);
                    break;
                case WordList list:
                    BuildList(builder, list);
                    break;
                case WordTable table:
                    BuildTable(builder, table);
                    break;
                case WordParagraph paragraph:
                    BuildParagraph(builder, paragraph);
                    break;
            }
        }

        builder.CloseElement(); // document region
    }

    private static void BuildHeading(RenderTreeBuilder builder, WordHeading heading)
    {
        int level = Math.Clamp(heading.Level, 1, 6);
        builder.OpenElement(10, HeadingTag(level));
        builder.AddAttribute(11, "class", "dx-word-heading");
        if (AlignStyle(heading.Alignment) is { } style)
        {
            builder.AddAttribute(12, "style", style);
        }

        BuildRuns(builder, heading.Runs);
        builder.CloseElement();
    }

    private static void BuildParagraph(RenderTreeBuilder builder, WordParagraph paragraph)
    {
        builder.OpenElement(20, "p");
        builder.AddAttribute(21, "class", "dx-word-para");
        if (AlignStyle(paragraph.Alignment) is { } style)
        {
            builder.AddAttribute(22, "style", style);
        }

        BuildRuns(builder, paragraph.Runs);
        builder.CloseElement();
    }

    private static string? AlignStyle(WordAlignment alignment) => alignment switch
    {
        WordAlignment.Center => "text-align:center",
        WordAlignment.End => "text-align:right",
        WordAlignment.Justify => "text-align:justify",
        _ => null,
    };

    private static void BuildList(RenderTreeBuilder builder, WordList list)
    {
        builder.OpenElement(30, list.Ordered ? "ol" : "ul");
        builder.AddAttribute(31, "class", "dx-word-list");
        for (int i = 0; i < list.Items.Count; i++)
        {
            builder.OpenElement(32, "li");
            builder.SetKey(i);
            builder.AddAttribute(33, "class", "dx-word-item");
            BuildRuns(builder, list.Items[i]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static void BuildTable(RenderTreeBuilder builder, WordTable table)
    {
        builder.OpenElement(40, "table");
        builder.AddAttribute(41, "class", "dx-word-table");

        IReadOnlyList<WordTableRow> rows = table.Rows;
        if (rows.Count == 0)
        {
            builder.CloseElement();
            return;
        }

        // First row is the header: a <thead> of <th scope="col"> (WCAG 1.3.1).
        builder.OpenElement(42, "thead");
        builder.OpenElement(43, "tr");
        builder.SetKey("head");
        foreach (WordTableCell cell in rows[0].Cells)
        {
            builder.OpenElement(44, "th");
            builder.AddAttribute(45, "scope", "col");
            BuildRuns(builder, cell.Runs);
            builder.CloseElement();
        }

        builder.CloseElement(); // tr
        builder.CloseElement(); // thead

        if (rows.Count > 1)
        {
            builder.OpenElement(46, "tbody");
            for (int r = 1; r < rows.Count; r++)
            {
                builder.OpenElement(47, "tr");
                builder.SetKey(r);
                foreach (WordTableCell cell in rows[r].Cells)
                {
                    builder.OpenElement(48, "td");
                    BuildRuns(builder, cell.Runs);
                    builder.CloseElement();
                }

                builder.CloseElement(); // tr
            }

            builder.CloseElement(); // tbody
        }

        builder.CloseElement(); // table
    }

    // Renders a run sequence. An unstyled run is bare text (no fake wrapper element);
    // bold wraps in <strong>, italic in <em>, both nest <strong><em>. All text goes
    // through AddContent so it is HTML-encoded.
    private static void BuildRuns(RenderTreeBuilder builder, IReadOnlyList<WordRun> runs)
    {
        for (int i = 0; i < runs.Count; i++)
        {
            WordRun run = runs[i];

            if (!run.Bold && !run.Italic)
            {
                builder.AddContent(60, run.Text);
                continue;
            }

            if (run.Bold)
            {
                builder.OpenElement(61, "strong");
            }

            if (run.Italic)
            {
                builder.OpenElement(62, "em");
            }

            builder.AddContent(63, run.Text);

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

    private static string HeadingTag(int level) => level switch
    {
        1 => "h1",
        2 => "h2",
        3 => "h3",
        4 => "h4",
        5 => "h5",
        _ => "h6",
    };
}
