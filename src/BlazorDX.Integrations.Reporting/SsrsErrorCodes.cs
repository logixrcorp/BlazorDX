namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// Recognizes the SSRS <c>rs*</c> error codes that appear in an error response
/// body and turns a verbose body into a short summary for an exception message.
/// Real SSRS returns a SOAP/HTML error page; the mock returns a compact
/// <c>rsCode: message</c> line. We scan for the code token in either shape rather
/// than parse the whole document.
/// </summary>
internal static class SsrsErrorCodes
{
    /// <summary>The report path did not resolve to a catalog item.</summary>
    public const string ItemNotFound = "rsItemNotFound";

    /// <summary>A required parameter was missing or a supplied value was invalid.</summary>
    public const string ParameterError = "rsParameterError";

    private static readonly string[] KnownCodes =
    {
        ItemNotFound,
        ParameterError,
        "rsUnknownFormat",
        "rsUnknownCommand",
        "rsAccessDenied",
    };

    /// <summary>
    /// Returns the first known <c>rs*</c> code found in <paramref name="body"/>, or
    /// <c>null</c> if none is present.
    /// </summary>
    public static string? Extract(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        foreach (var code in KnownCodes)
        {
            if (body.Contains(code, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>
    /// Condenses an error body to a single trimmed line for an exception message,
    /// capping length so a large HTML error page does not flood the message.
    /// </summary>
    public static string Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The server returned no error detail.";
        }

        var firstLine = body.Trim();
        var newline = firstLine.IndexOfAny(new[] { '\r', '\n' });
        if (newline >= 0)
        {
            firstLine = firstLine[..newline].Trim();
        }

        const int Cap = 240;
        return firstLine.Length > Cap ? firstLine[..Cap] + "…" : firstLine;
    }
}
