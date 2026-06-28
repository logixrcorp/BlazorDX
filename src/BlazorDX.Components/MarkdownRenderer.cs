using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Components;

/// <summary>
/// Converts a safe subset of Markdown to HTML. Security is structural: every text
/// run is HTML-encoded <em>before</em> any formatting tags are inserted, so the
/// only tags in the output are the fixed structural set this renderer emits — user
/// input can never introduce a tag. Link targets are checked against a scheme
/// allow-list (so <c>javascript:</c> URLs are dropped). This is the one audited
/// place outside <c>BlazorDX.Security</c> that builds a <see cref="MarkupString"/>
/// from runtime data; correctness here is the safety guarantee.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex ItalicStar = new(@"\*([^*]+)\*", RegexOptions.Compiled);
    private static readonly Regex ItalicUnderscore = new("_([^_]+)_", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex UnorderedItem = new(@"^[-*]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedItem = new(@"^\d+\.\s+(.*)$", RegexOptions.Compiled);

    /// <summary>Renders Markdown to a sanitized <see cref="MarkupString"/> ready to display.</summary>
    public static MarkupString Render(string? markdown)
    {
        string html = ToHtml(markdown ?? string.Empty);

        // The single audited boundary: the HTML above is built only from encoded
        // text plus this renderer's fixed tag set, so it is safe by construction.
#pragma warning disable DX1001 // Audited: structurally-safe HTML from the Markdown renderer.
        return new MarkupString(html);
#pragma warning restore DX1001
    }

    private static string ToHtml(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        StringBuilder html = new();
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i];

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                i = AppendFencedCode(lines, i, html);
            }
            else if (Heading.Match(line) is { Success: true } heading)
            {
                int level = heading.Groups[1].Value.Length;
                html.Append("<h").Append(level).Append('>')
                    .Append(Inline(heading.Groups[2].Value))
                    .Append("</h").Append(level).Append('>');
                i++;
            }
            else if (UnorderedItem.IsMatch(line))
            {
                i = AppendList(lines, i, html, "ul", UnorderedItem);
            }
            else if (OrderedItem.IsMatch(line))
            {
                i = AppendList(lines, i, html, "ol", OrderedItem);
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                i++;
            }
            else
            {
                i = AppendParagraph(lines, i, html);
            }
        }

        return html.ToString();
    }

    private static int AppendFencedCode(string[] lines, int start, StringBuilder html)
    {
        int i = start + 1;
        StringBuilder code = new();
        while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
        {
            code.Append(WebUtility.HtmlEncode(lines[i])).Append('\n');
            i++;
        }

        html.Append("<pre><code>").Append(code).Append("</code></pre>");
        return i < lines.Length ? i + 1 : i;   // skip the closing fence
    }

    private static int AppendList(string[] lines, int start, StringBuilder html, string tag, Regex item)
    {
        html.Append('<').Append(tag).Append('>');
        int i = start;
        while (i < lines.Length && item.Match(lines[i]) is { Success: true } match)
        {
            html.Append("<li>").Append(Inline(match.Groups[1].Value)).Append("</li>");
            i++;
        }

        html.Append("</").Append(tag).Append('>');
        return i;
    }

    private static int AppendParagraph(string[] lines, int start, StringBuilder html)
    {
        List<string> parts = new();
        int i = start;
        while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i])
            && !lines[i].StartsWith("```", StringComparison.Ordinal)
            && !Heading.IsMatch(lines[i])
            && !UnorderedItem.IsMatch(lines[i])
            && !OrderedItem.IsMatch(lines[i]))
        {
            parts.Add(lines[i]);
            i++;
        }

        html.Append("<p>").Append(Inline(string.Join(" ", parts))).Append("</p>");
        return i;
    }

    // Encodes first, then layers formatting onto the encoded text so user input
    // can never inject markup. Splitting on backticks isolates code spans (their
    // contents stay literal) without needing placeholder sentinels.
    private static string Inline(string raw)
    {
        string[] segments = WebUtility.HtmlEncode(raw).Split('`');
        bool balanced = segments.Length % 2 == 1;   // even number of backticks
        StringBuilder result = new();

        for (int i = 0; i < segments.Length; i++)
        {
            bool isCode = i % 2 == 1 && (balanced || i < segments.Length - 1);
            if (isCode)
            {
                result.Append("<code>").Append(segments[i]).Append("</code>");
            }
            else
            {
                if (i > 0 && !balanced)
                {
                    result.Append('`');   // dangling backtick: render it literally
                }

                result.Append(FormatSpans(segments[i]));
            }
        }

        return result.ToString();
    }

    private static string FormatSpans(string encoded)
    {
        encoded = Link.Replace(encoded, m =>
        {
            string text = m.Groups[1].Value;
            string href = m.Groups[2].Value;
            return IsSafeHref(href)
                ? $"<a href=\"{href}\" rel=\"noopener noreferrer\">{text}</a>"
                : text;   // unsafe scheme: drop the link, keep the text
        });

        encoded = Bold.Replace(encoded, "<strong>$1</strong>");
        encoded = ItalicStar.Replace(encoded, "<em>$1</em>");
        encoded = ItalicUnderscore.Replace(encoded, "<em>$1</em>");
        return encoded;
    }

    // Allow only http(s)/mailto and relative (/, #, ./) targets. The href has
    // already been HTML-encoded, so ':' survives for this scheme check.
    private static bool IsSafeHref(string href)
    {
        string trimmed = href.TrimStart();

        // Scheme-relative ("//host" or the "/\" variant browsers also accept) resolves to an
        // off-site URL despite looking relative — exclude it before the relative allowance below.
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith('/') || trimmed.StartsWith('#') || trimmed.StartsWith("./", StringComparison.Ordinal))
        {
            return true;
        }

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }
}
