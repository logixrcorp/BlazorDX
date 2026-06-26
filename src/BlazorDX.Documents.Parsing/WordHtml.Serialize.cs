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

            bool colored = !string.IsNullOrEmpty(run.Color) || !string.IsNullOrEmpty(run.Highlight);
            if (colored)
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

                sb.Append("\">");
            }

            AppendEscaped(sb, run.Text ?? string.Empty);

            if (colored)
            {
                sb.Append("</span>");
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
