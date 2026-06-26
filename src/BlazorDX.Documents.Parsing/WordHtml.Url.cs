namespace BlazorDX.Documents;

/// <summary>
/// Attribute helpers for <see cref="WordHtml"/>: extracting an <c>href</c> (reduced to a
/// safe scheme) and a block's alignment from a raw tag. Kept in its own partial so the
/// main file stays under the line cap.
/// </summary>
public static partial class WordHtml
{
    // Reads a block's alignment from a raw opening tag: the CSS `text-align` (what
    // execCommand justify* emits) or a legacy `align` attribute. Defaults to Start.
    private static WordAlignment ParseAlignment(string rawTag)
    {
        foreach (string key in AlignKeys)
        {
            int idx = rawTag.IndexOf(key, System.StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                int p = idx + key.Length;
                while (p < rawTag.Length && rawTag[p] is ' ' or ':' or '=' or '"' or '\'')
                {
                    p++;
                }

                int end = p;
                while (end < rawTag.Length && char.IsLetter(rawTag[end]))
                {
                    end++;
                }

                if (end > p)
                {
                    switch (rawTag.Substring(p, end - p).ToLowerInvariant())
                    {
                        case "center": return WordAlignment.Center;
                        case "right":
                        case "end": return WordAlignment.End;
                        case "justify": return WordAlignment.Justify;
                        case "left":
                        case "start": return WordAlignment.Start;
                    }
                }

                idx = rawTag.IndexOf(key, idx + key.Length, System.StringComparison.OrdinalIgnoreCase);
            }
        }

        return WordAlignment.Start;
    }

    // "text-align" is tried before "align" so a CSS rule wins over a legacy attribute.
    private static readonly string[] AlignKeys = ["text-align", "align"];

    // Reads a CSS property's value from a raw tag's style and normalizes it to #rrggbb.
    // Returns null for unsupported (named colors) or absent values.
    private static string? ParseCssColor(string rawTag, string property)
    {
        int idx = 0;
        while ((idx = rawTag.IndexOf(property, idx, System.StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            // Start-of-declaration boundary so "color" doesn't match inside "background-color".
            bool boundary = idx == 0 || rawTag[idx - 1] is ' ' or ';' or '"' or '\'' or '{' or '>';
            int p = idx + property.Length;
            while (p < rawTag.Length && rawTag[p] == ' ')
            {
                p++;
            }

            if (boundary && p < rawTag.Length && rawTag[p] == ':')
            {
                p++;
                while (p < rawTag.Length && rawTag[p] == ' ')
                {
                    p++;
                }

                int end = p;
                while (end < rawTag.Length && rawTag[end] is not (';' or '"' or '\'' or '}'))
                {
                    end++;
                }

                return NormalizeColor(rawTag.Substring(p, end - p).Trim());
            }

            idx = p;
        }

        return null;
    }

    // Normalizes #rgb / #rrggbb / rgb(r,g,b[,a]) to lowercase #rrggbb; null otherwise.
    private static string? NormalizeColor(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value[0] == '#')
        {
            string hex = value[1..];
            if (hex.Length == 3)
            {
                hex = new string([hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]]);
            }

            return hex.Length == 6 && IsHex(hex) ? "#" + hex.ToLowerInvariant() : null;
        }

        if (value.StartsWith("rgb", System.StringComparison.OrdinalIgnoreCase))
        {
            int open = value.IndexOf('(');
            int close = value.IndexOf(')');
            if (open >= 0 && close > open)
            {
                string[] parts = value[(open + 1)..close].Split(',');
                if (parts.Length >= 3
                    && int.TryParse(parts[0].Trim(), out int r)
                    && int.TryParse(parts[1].Trim(), out int g)
                    && int.TryParse(parts[2].Trim(), out int b))
                {
                    return $"#{Clamp(r):x2}{Clamp(g):x2}{Clamp(b):x2}";
                }
            }
        }

        return null;
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    private static bool IsHex(string s)
    {
        foreach (char c in s)
        {
            if (!System.Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ExtractHref(string rawTag) => ReadAttribute(rawTag, "href");

    // Pulls a named attribute's value out of an opening tag's raw attribute text (e.g.
    // 'img src="data:…" alt="x"'). Best-effort: quoted or bare, entity-decoded, with a word
    // boundary so "src" doesn't match inside "data-src".
    private static string? ReadAttribute(string rawTag, string name)
    {
        int idx = rawTag.IndexOf(name, System.StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool boundary = idx == 0 || char.IsWhiteSpace(rawTag[idx - 1]);
            int p = idx + name.Length;
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

            idx = rawTag.IndexOf(name, idx + name.Length, System.StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    // Builds a WordImage from an <img> tag's raw text. Only base64 data: URLs are accepted
    // (a self-contained doc; external/remote src is ignored). Returns null if unusable.
    private static WordImage? ParseImage(string rawTag)
    {
        string? src = ReadAttribute(rawTag, "src");
        if (src is null || !src.StartsWith("data:", System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int comma = src.IndexOf(',');
        if (comma < 0)
        {
            return null;
        }

        string meta = src[5..comma]; // between "data:" and ","
        if (!meta.Contains("base64", System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int semi = meta.IndexOf(';');
        string contentType = (semi >= 0 ? meta[..semi] : meta).Trim();

        byte[] data;
        try
        {
            data = System.Convert.FromBase64String(src[(comma + 1)..].Trim());
        }
        catch (System.FormatException)
        {
            return null;
        }

        if (data.Length == 0)
        {
            return null;
        }

        string? alt = ReadAttribute(rawTag, "alt");
        return new WordImage(
            data,
            contentType.Length > 0 ? contentType : "image/png",
            string.IsNullOrEmpty(alt) ? null : alt,
            ParseLeadingInt(ReadAttribute(rawTag, "width")),
            ParseLeadingInt(ReadAttribute(rawTag, "height")));
    }

    // Reads the leading integer of a value (so "200" and "200px" both yield 200); 0 if none.
    private static int ParseLeadingInt(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int i = 0;
        while (i < value.Length && char.IsDigit(value[i]))
        {
            i++;
        }

        return i > 0 && int.TryParse(value[..i], out int n) ? n : 0;
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
