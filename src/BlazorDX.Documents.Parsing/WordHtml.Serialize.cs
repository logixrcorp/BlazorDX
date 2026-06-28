using System;
using System.Globalization;
using System.Text;

namespace BlazorDX.Documents;

/// <summary>The model -> HTML serialization half of <see cref="WordHtml"/> (kept in its
/// own partial so the main file stays under the line cap).</summary>
public static partial class WordHtml
{
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
                case WordImage image:
                    AppendImage(sb, image);
                    break;
            }
        }

        return sb.ToString();
    }

    private static void AppendImage(StringBuilder sb, WordImage image)
    {
        sb.Append("<img src=\"data:").Append(image.ContentType).Append(";base64,")
          .Append(Convert.ToBase64String(image.Data)).Append("\" alt=\"");
        AppendEscaped(sb, image.AltText ?? string.Empty);
        sb.Append('"');
        if (image.Width > 0)
        {
            sb.Append(" width=\"").Append(image.Width.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        if (image.Height > 0)
        {
            sb.Append(" height=\"").Append(image.Height.ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        sb.Append(" />");
    }

    private static void AppendHeading(StringBuilder sb, WordHeading heading)
    {
        int level = Math.Clamp(heading.Level, 1, 6);
        string tag = "h" + level.ToString(CultureInfo.InvariantCulture);
        sb.Append('<').Append(tag)
          .Append(BlockStyle(heading.Alignment, heading.LineSpacing, heading.IndentLevel)).Append('>');
        AppendRuns(sb, heading.Runs);
        sb.Append("</").Append(tag).Append('>');
    }

    private static void AppendParagraph(StringBuilder sb, WordParagraph paragraph)
    {
        sb.Append("<p")
          .Append(BlockStyle(paragraph.Alignment, paragraph.LineSpacing, paragraph.IndentLevel)).Append('>');
        AppendRuns(sb, paragraph.Runs);
        sb.Append("</p>");
    }

    // A combined inline style attribute for a block's alignment, line spacing, and indent — or
    // "" when all are at their defaults.
    private static string BlockStyle(WordAlignment alignment, double? lineSpacing, int indentLevel)
    {
        string align = alignment switch
        {
            WordAlignment.Center => "text-align:center;",
            WordAlignment.End => "text-align:right;",
            WordAlignment.Justify => "text-align:justify;",
            _ => string.Empty,
        };
        string line = lineSpacing is > 0 and double m
            ? "line-height:" + m.ToString(CultureInfo.InvariantCulture) + ";"
            : string.Empty;
        string indent = indentLevel > 0
            ? "margin-left:" + (indentLevel * 0.5).ToString(CultureInfo.InvariantCulture) + "in;"
            : string.Empty;

        string style = align + line + indent;
        return style.Length == 0 ? string.Empty : " style=\"" + style + "\"";
    }

    private static void AppendList(StringBuilder sb, WordList list)
    {
        string tag = list.Ordered ? "ol" : "ul";
        int depth = 0;
        bool liOpen = false;
        sb.Append('<').Append(tag).Append('>'); // root list at level 0

        for (int i = 0; i < list.Items.Count; i++)
        {
            int level = list.LevelOf(i);
            if (level > depth)
            {
                // Going deeper: nested lists open INSIDE the still-open previous <li>.
                while (depth < level)
                {
                    sb.Append('<').Append(tag).Append('>');
                    depth++;
                }
            }
            else
            {
                if (liOpen)
                {
                    sb.Append("</li>");
                    liOpen = false;
                }

                while (depth > level)
                {
                    sb.Append("</").Append(tag).Append("></li>");
                    depth--;
                }
            }

            sb.Append("<li>");
            AppendRuns(sb, list.Items[i]);
            liOpen = true;
        }

        if (liOpen)
        {
            sb.Append("</li>");
        }

        while (depth > 0)
        {
            sb.Append("</").Append(tag).Append("></li>");
            depth--;
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
                if (cell.ColSpan == 0)
                {
                    continue; // covered by a merge to its left — not a real DOM cell
                }

                string shade = string.IsNullOrEmpty(cell.Shading)
                    ? string.Empty
                    : " style=\"background-color:" + cell.Shading + ";\"";
                string span = cell.ColSpan > 1
                    ? " colspan=\"" + cell.ColSpan.ToString(CultureInfo.InvariantCulture) + "\""
                    : string.Empty;
                if (r == 0)
                {
                    sb.Append("<th scope=\"col\"").Append(span).Append(shade).Append('>');
                }
                else
                {
                    sb.Append("<td").Append(span).Append(shade).Append('>');
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
            string? scriptTag = run.VerticalAlign switch
            {
                WordVerticalAlign.Superscript => "sup",
                WordVerticalAlign.Subscript => "sub",
                _ => null,
            };

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

            if (scriptTag is not null)
            {
                sb.Append('<').Append(scriptTag).Append('>');
            }

            bool styled = !string.IsNullOrEmpty(run.Color) || !string.IsNullOrEmpty(run.Highlight)
                || !string.IsNullOrEmpty(run.FontFamily) || run.FontSizePoints is > 0;
            if (styled)
            {
                sb.Append("<span style=\"");
                if (!string.IsNullOrEmpty(run.Color))
                {
                    sb.Append("color:").Append(run.Color).Append(';');
                }

                if (!string.IsNullOrEmpty(run.Highlight))
                {
                    sb.Append("background-color:").Append(run.Highlight).Append(';');
                }

                if (!string.IsNullOrEmpty(run.FontFamily))
                {
                    sb.Append("font-family:").Append(run.FontFamily).Append(';');
                }

                if (run.FontSizePoints is > 0 and double pt)
                {
                    sb.Append("font-size:").Append(pt.ToString(CultureInfo.InvariantCulture)).Append("pt;");
                }

                sb.Append("\">");
            }

            AppendEscaped(sb, run.Text ?? string.Empty);

            if (styled)
            {
                sb.Append("</span>");
            }

            if (scriptTag is not null)
            {
                sb.Append("</").Append(scriptTag).Append('>');
            }

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
}
