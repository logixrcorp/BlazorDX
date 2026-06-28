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
                case WordImage image:
                    BuildImage(builder, image);
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
        if (BlockStyle(heading.Alignment, heading.LineSpacing, heading.IndentLevel) is { } style)
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
        if (BlockStyle(paragraph.Alignment, paragraph.LineSpacing, paragraph.IndentLevel) is { } style)
        {
            builder.AddAttribute(22, "style", style);
        }

        BuildRuns(builder, paragraph.Runs);
        builder.CloseElement();
    }

    private static string? BlockStyle(WordAlignment alignment, double? lineSpacing, int indentLevel)
    {
        string align = alignment switch
        {
            WordAlignment.Center => "text-align:center;",
            WordAlignment.End => "text-align:right;",
            WordAlignment.Justify => "text-align:justify;",
            _ => string.Empty,
        };
        string line = lineSpacing is > 0 and double m
            ? $"line-height:{m.ToString(System.Globalization.CultureInfo.InvariantCulture)};"
            : string.Empty;
        string indent = indentLevel > 0
            ? $"margin-left:{(indentLevel * 0.5).ToString(System.Globalization.CultureInfo.InvariantCulture)}in;"
            : string.Empty;

        string style = align + line + indent;
        return style.Length == 0 ? null : style;
    }

    private static void BuildImage(RenderTreeBuilder builder, WordImage image)
    {
        builder.OpenElement(40, "img");
        builder.AddAttribute(41, "class", "dx-word-image");
        builder.AddAttribute(42, "src",
            $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Data)}");
        // alt is always present (empty for decorative) so the <img> has an accessible name.
        builder.AddAttribute(43, "alt", image.AltText ?? string.Empty);
        if (image.Width > 0)
        {
            builder.AddAttribute(44, "width", image.Width);
        }

        if (image.Height > 0)
        {
            builder.AddAttribute(45, "height", image.Height);
        }

        builder.CloseElement();
    }

    private sealed class ListNode
    {
        public IReadOnlyList<WordRun> Runs { get; init; } = [];

        public List<ListNode> Children { get; } = [];
    }

    private static void BuildList(RenderTreeBuilder builder, WordList list)
    {
        RenderListNodes(builder, list.Ordered, BuildListTree(list));
    }

    // Turns the flat (items + indent levels) into a tree of nodes; a missing/zero level is
    // a root, and a level can't skip more than one past the current depth (clamped).
    private static List<ListNode> BuildListTree(WordList list)
    {
        List<ListNode> roots = [];
        List<ListNode> ancestors = []; // ancestors[d] = the open node at depth d

        for (int i = 0; i < list.Items.Count; i++)
        {
            int level = Math.Max(0, list.LevelOf(i));
            if (level > ancestors.Count)
            {
                level = ancestors.Count;
            }

            ListNode node = new() { Runs = list.Items[i] };
            if (level == 0)
            {
                roots.Add(node);
            }
            else
            {
                ancestors[level - 1].Children.Add(node);
            }

            if (ancestors.Count > level)
            {
                ancestors.RemoveRange(level, ancestors.Count - level);
            }

            ancestors.Add(node); // ancestors[level] = node
        }

        return roots;
    }

    // Recursively renders a list level; a node's children become a nested <ul>/<ol> inside
    // its <li>. Each nested level goes in its own region so sequence numbers stay isolated.
    private static void RenderListNodes(RenderTreeBuilder builder, bool ordered, List<ListNode> nodes)
    {
        builder.OpenElement(30, ordered ? "ol" : "ul");
        builder.AddAttribute(31, "class", "dx-word-list");
        foreach (ListNode node in nodes)
        {
            builder.OpenElement(32, "li");
            builder.SetKey(node);
            builder.AddAttribute(33, "class", "dx-word-item");
            BuildRuns(builder, node.Runs);
            if (node.Children.Count > 0)
            {
                builder.OpenRegion(34);
                RenderListNodes(builder, ordered, node.Children);
                builder.CloseRegion();
            }

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
            if (cell.ColSpan == 0)
            {
                continue; // covered by a merge to its left
            }

            builder.OpenElement(44, "th");
            builder.AddAttribute(45, "scope", "col");
            if (cell.ColSpan > 1)
            {
                builder.AddAttribute(51, "colspan", cell.ColSpan);
            }

            if (!string.IsNullOrEmpty(cell.Shading))
            {
                builder.AddAttribute(49, "style", $"background-color:{cell.Shading};");
            }

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
                    if (cell.ColSpan == 0)
                    {
                        continue; // covered by a merge to its left
                    }

                    builder.OpenElement(48, "td");
                    if (cell.ColSpan > 1)
                    {
                        builder.AddAttribute(52, "colspan", cell.ColSpan);
                    }

                    if (!string.IsNullOrEmpty(cell.Shading))
                    {
                        builder.AddAttribute(50, "style", $"background-color:{cell.Shading};");
                    }

                    BuildRuns(builder, cell.Runs);
                    builder.CloseElement();
                }

                builder.CloseElement(); // tr
            }

            builder.CloseElement(); // tbody
        }

        builder.CloseElement(); // table
    }

    // Renders a run sequence with its formatting: hyperlink (scheme-guarded), bold, italic,
    // underline, strike, and color/highlight. All text goes through AddContent so it is
    // HTML-encoded. Sequence numbers are constant per source position (reused each run).
    private static void BuildRuns(RenderTreeBuilder builder, IReadOnlyList<WordRun> runs)
    {
        foreach (WordRun run in runs)
        {
            // A .docx hyperlink URL is untrusted (the file is attacker-controlled) and the
            // viewer has no sanitizer, so only safe schemes become a clickable link.
            string? href = SafeHref(run.Href);
            bool link = href is not null;
            string? colorStyle = ColorStyle(run);
            string? scriptTag = run.VerticalAlign switch
            {
                WordVerticalAlign.Superscript => "sup",
                WordVerticalAlign.Subscript => "sub",
                _ => null,
            };

            if (link)
            {
                builder.OpenElement(60, "a");
                builder.AddAttribute(61, "href", href);
                builder.AddAttribute(62, "rel", "noopener noreferrer");
            }

            if (run.Bold)
            {
                builder.OpenElement(63, "strong");
            }

            if (run.Italic)
            {
                builder.OpenElement(64, "em");
            }

            if (run.Underline)
            {
                builder.OpenElement(65, "u");
            }

            if (run.Strike)
            {
                builder.OpenElement(66, "s");
            }

            if (scriptTag is not null)
            {
                builder.OpenElement(70, scriptTag);
            }

            if (colorStyle is not null)
            {
                builder.OpenElement(67, "span");
                builder.AddAttribute(68, "style", colorStyle);
            }

            builder.AddContent(69, run.Text);

            if (colorStyle is not null)
            {
                builder.CloseElement();
            }

            if (scriptTag is not null)
            {
                builder.CloseElement();
            }

            if (run.Strike)
            {
                builder.CloseElement();
            }

            if (run.Underline)
            {
                builder.CloseElement();
            }

            if (run.Italic)
            {
                builder.CloseElement();
            }

            if (run.Bold)
            {
                builder.CloseElement();
            }

            if (link)
            {
                builder.CloseElement();
            }
        }
    }

    private static string? ColorStyle(WordRun run)
    {
        bool color = !string.IsNullOrEmpty(run.Color);
        bool highlight = !string.IsNullOrEmpty(run.Highlight);
        bool family = !string.IsNullOrEmpty(run.FontFamily);
        bool size = run.FontSizePoints is > 0;
        if (!color && !highlight && !family && !size)
        {
            return null;
        }

        return (color ? $"color:{run.Color};" : string.Empty)
            + (highlight ? $"background-color:{run.Highlight};" : string.Empty)
            + (family ? $"font-family:{run.FontFamily};" : string.Empty)
            + (size ? $"font-size:{run.FontSizePoints!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt;" : string.Empty);
    }

    // Only http/https/mailto URLs are clickable; anything else is dropped (rendered as
    // plain formatted text by the caller, never a hostile href).
    private static string? SafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        string trimmed = href.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : null;
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
