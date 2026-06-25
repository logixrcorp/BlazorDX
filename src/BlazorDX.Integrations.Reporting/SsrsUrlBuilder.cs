using System.Globalization;
using System.Text;

namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// Builds SSRS URL-Access query strings. Kept separate from the HTTP client so
/// the exact wire format — report-path encoding, the <c>rs:</c>/<c>rc:</c> control
/// tokens, and multi-value parameter repeats — can be reasoned about and (next
/// turn) reused by the viewer. Pure and allocation-light; no I/O.
/// </summary>
internal static class SsrsUrlBuilder
{
    /// <summary>
    /// Builds the URL-Access relative URL for a render request:
    /// <c>?{/encoded/path}&amp;rs:Command=Render&amp;rs:Format=…&amp;rc:Toolbar=…&amp;{params}</c>.
    /// The report path is the first query token (a bare key with no value), exactly
    /// as SSRS expects; its slashes are <c>%2f</c>-encoded so a proxy cannot mangle
    /// them. Each parameter value is individually URL-encoded, and a multi-value
    /// parameter repeats its key once per value.
    /// </summary>
    public static string BuildRenderQuery(ReportRequest request)
    {
        var sb = new StringBuilder();
        sb.Append('?');
        sb.Append(EncodeReportPath(request.ReportPath));
        sb.Append("&rs:Command=Render");
        sb.Append("&rs:Format=");
        sb.Append(FormatToken(request.Format));
        sb.Append("&rc:Toolbar=");
        sb.Append(request.ShowToolbar ? "true" : "false");

        if (request.Parameters is not null)
        {
            foreach (var pair in request.Parameters)
            {
                sb.Append('&');
                sb.Append(Uri.EscapeDataString(pair.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
            }
        }

        return sb.ToString();
    }

    /// <summary>The <c>rs:Format</c> token SSRS expects for a <see cref="ReportFormat"/>.</summary>
    public static string FormatToken(ReportFormat format) => format switch
    {
        ReportFormat.Html5 => "HTML5",
        ReportFormat.Pdf => "PDF",
        ReportFormat.Csv => "CSV",
        ReportFormat.Image => "IMAGE",
        _ => throw new ArgumentOutOfRangeException(
            nameof(format), format, "Unsupported report format."),
    };

    /// <summary>
    /// Encodes a report catalog path as the leading URL-Access token. A leading
    /// slash is ensured, each path segment is percent-encoded, and the separating
    /// slashes become <c>%2f</c> so the whole path survives as one query token.
    /// </summary>
    internal static string EncodeReportPath(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new ArgumentException("A report path is required.", nameof(reportPath));
        }

        var trimmed = reportPath.Trim();
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            sb.Append("%2f");
            sb.Append(Uri.EscapeDataString(segment));
        }

        // A path with no real segments still needs the root slash.
        return sb.Length == 0 ? "%2f" : sb.ToString();
    }

    /// <summary>
    /// Builds the SSRS REST relative URL for parameter definitions:
    /// <c>/api/v2.0/reports({path})/parameterdefinitions</c>. The path is
    /// percent-encoded with <see cref="Uri.EscapeDataString(string)"/> — exactly the
    /// per-segment encoding the URL-Access builder uses (slashes become <c>%2F</c>)
    /// — so a path containing <c>)</c>, <c>(</c>, <c>'</c>, <c>;</c>, spaces, or
    /// other delimiters cannot break out of the parenthesised path token and malform
    /// the REST request. SSRS (and the mock) percent-decode the token back to the
    /// real catalog path. A leading slash is normalized in before encoding.
    /// </summary>
    public static string BuildParameterDefinitionsPath(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new ArgumentException("A report path is required.", nameof(reportPath));
        }

        var normalized = reportPath.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        var encoded = Uri.EscapeDataString(normalized);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"api/v2.0/reports({encoded})/parameterdefinitions");
    }
}
