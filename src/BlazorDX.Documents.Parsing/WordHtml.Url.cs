namespace BlazorDX.Documents;

/// <summary>
/// Hyperlink URL handling for <see cref="WordHtml"/>: extracting an <c>href</c> from a raw
/// tag and reducing it to a safe scheme. Kept in its own partial so the main file stays
/// under the line cap.
/// </summary>
public static partial class WordHtml
{
    // Pulls the href value out of an opening tag's raw attribute text (e.g.
    // 'a href="https://x" title="y"'). Best-effort: quoted or bare, entity-decoded.
    private static string? ExtractHref(string rawTag)
    {
        int idx = rawTag.IndexOf("href", System.StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            // Require a word boundary so "data-href" / "xhref" don't match.
            bool boundary = idx == 0 || char.IsWhiteSpace(rawTag[idx - 1]);
            int p = idx + 4;
            while (p < rawTag.Length && char.IsWhiteSpace(rawTag[p]))
            {
                p++;
            }

            if (boundary && p < rawTag.Length && rawTag[p] == '=')
            {
                p++;
                while (p < rawTag.Length && char.IsWhiteSpace(rawTag[p]))
                {
                    p++;
                }

                if (p < rawTag.Length)
                {
                    char quote = rawTag[p];
                    if (quote is '"' or '\'')
                    {
                        int end = rawTag.IndexOf(quote, p + 1);
                        if (end > p)
                        {
                            return DecodeEntities(rawTag.AsSpan(p + 1, end - p - 1));
                        }
                    }
                    else
                    {
                        int end = p;
                        while (end < rawTag.Length && !char.IsWhiteSpace(rawTag[end]))
                        {
                            end++;
                        }

                        return DecodeEntities(rawTag.AsSpan(p, end - p));
                    }
                }
            }

            idx = rawTag.IndexOf("href", idx + 4, System.StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    // Only http/https/mailto URLs survive — javascript:, data:, and relative URLs are
    // rejected (an unsafe link becomes plain text, never a clickable hostile URL).
    private static string? SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string trimmed = url.Trim();
        return trimmed.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", System.StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : null;
    }
}
