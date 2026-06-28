using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BlazorDX.MockReportServer;

/// <summary>
/// Optional Basic-auth configuration for the mock. Real SSRS uses
/// NTLM/Negotiate; Basic is the practical local stand-in to exercise a client's
/// auth pass-through. When <see cref="Enabled"/> is false (the default) the
/// server is open for easy local use.
/// </summary>
public sealed class MockReportServerAuthOptions
{
    public bool Enabled { get; init; }

    public string Username { get; init; } = "report";

    public string Password { get; init; } = "viewer";

    public string Realm { get; init; } = "MockReportServer";
}

/// <summary>
/// Wires the mock SSRS endpoints onto any <see cref="IEndpointRouteBuilder"/>,
/// so the same emulation is available both as a standalone app and mounted
/// in-process by the demo. The two entry points mirror SSRS's two surfaces:
/// the <c>/ReportServer</c> URL Access endpoint and a slice of the REST API.
/// </summary>
public static class MockReportServerExtensions
{
    /// <summary>
    /// Maps the SSRS URL Access endpoint at <paramref name="prefix"/>
    /// (default <c>/ReportServer</c>), handling <c>rs:Command=Render</c> and
    /// <c>rs:Command=ListChildren</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapMockReportServer(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/ReportServer",
        ReportCatalog? catalog = null,
        MockReportServerAuthOptions? auth = null)
    {
        catalog ??= new ReportCatalog();
        auth ??= new MockReportServerAuthOptions();

        endpoints.MapGet(prefix, (HttpContext ctx) => HandleUrlAccess(ctx, catalog, auth));
        return endpoints;
    }

    /// <summary>
    /// Maps the REST parameter-metadata endpoints under <paramref name="prefix"/>
    /// (default <c>/reports</c>): the SSRS-shaped
    /// <c>/reports/api/v2.0/reports(path)/parameterdefinitions</c> and a clean
    /// <c>/reports/api/v2.0/reports(path)</c> probe, plus a friendly
    /// <c>/mock/parameters?report=/Sales/Monthly</c> alias.
    /// </summary>
    public static IEndpointRouteBuilder MapMockReportsRestApi(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/reports",
        ReportCatalog? catalog = null,
        MockReportServerAuthOptions? auth = null)
    {
        catalog ??= new ReportCatalog();
        auth ??= new MockReportServerAuthOptions();

        // SSRS REST v2.0 shape: reports(path)/parameterdefinitions. A catch-all
        // segment carries the whole tail because the report path embeds slashes
        // and parentheses; we parse the `reports(...)/parameterdefinitions` shape
        // in the handler. (ASP.NET routing forbids a catch-all sharing a segment
        // with literal text, so we cannot express the shape directly in the route.)
        endpoints.MapGet(prefix + "/api/v2.0/{**rest}",
            (HttpContext ctx, string rest) => RestV2(ctx, catalog, auth, rest));

        // Friendly alias used by the demo/tests: /mock/parameters?report=/Sales/Monthly.
        endpoints.MapGet("/mock/parameters",
            (HttpContext ctx, string? report) => ParameterDefinitions(ctx, catalog, auth, report ?? string.Empty));

        return endpoints;
    }

    /// <summary>
    /// Dispatches the REST v2.0 catch-all tail. Recognises
    /// <c>reports</c> (catalog listing), <c>reports(path)</c> (report probe), and
    /// <c>reports(path)/parameterdefinitions</c> (parameter metadata).
    /// </summary>
    private static IResult RestV2(HttpContext ctx, ReportCatalog catalog, MockReportServerAuthOptions auth, string rest)
    {
        if (!Authorized(ctx, auth, out var challenge))
        {
            return challenge!;
        }

        if (string.Equals(rest, "reports", StringComparison.OrdinalIgnoreCase))
        {
            var value = catalog.Reports.Select(r => new CatalogItem(r.Path, r.Title, "Report"));
            return Results.Json(new { value });
        }

        if (!rest.StartsWith("reports(", StringComparison.OrdinalIgnoreCase))
        {
            return SsrsErrors.NotFound("/" + rest);
        }

        var closeParen = rest.IndexOf(')');
        if (closeParen < 0)
        {
            return SsrsErrors.NotFound("/" + rest);
        }

        var path = rest["reports(".Length..closeParen];
        var tail = rest[(closeParen + 1)..].TrimStart('/');

        if (string.Equals(tail, "parameterdefinitions", StringComparison.OrdinalIgnoreCase) ||
            tail.Length == 0)
        {
            return ParameterDefinitions(ctx, catalog, auth, path);
        }

        return SsrsErrors.NotFound("/" + rest);
    }

    private static IResult HandleUrlAccess(HttpContext ctx, ReportCatalog catalog, MockReportServerAuthOptions auth)
    {
        if (!Authorized(ctx, auth, out var challenge))
        {
            return challenge!;
        }

        var query = ctx.Request.Query;
        var command = FirstOrDefault(query, "rs:Command") ?? "Render";

        var reportPath = ExtractReportPath(ctx.Request.QueryString.Value);

        if (command.Equals("ListChildren", StringComparison.OrdinalIgnoreCase))
        {
            var folder = reportPath ?? "/";
            var items = catalog.ListChildren(folder);
            return Results.Json(items);
        }

        if (!command.Equals("Render", StringComparison.OrdinalIgnoreCase))
        {
            return SsrsErrors.Command(command);
        }

        if (string.IsNullOrEmpty(reportPath))
        {
            return SsrsErrors.MissingReportPath();
        }

        var report = catalog.Find(reportPath);
        if (report is null)
        {
            return SsrsErrors.NotFound(reportPath);
        }

        var format = FirstOrDefault(query, "rs:Format") ?? "HTML5";
        if (!ReportRenderer.IsSupportedFormat(format))
        {
            return SsrsErrors.Format(format);
        }

        var toolbar = ParseBool(FirstOrDefault(query, "rc:Toolbar"), defaultValue: true);
        var showParameters = ParseBool(FirstOrDefault(query, "rc:Parameters"), defaultValue: true);

        if (!TryResolveParameters(report, query, out var values, out var paramError))
        {
            return SsrsErrors.Parameter(paramError!);
        }

        var rows = report.BuildRows(values);
        var renderRequest = new RenderRequest(report, values, rows, toolbar, showParameters);
        var result = ReportRenderer.Render(format, renderRequest);

        // HTML renders are meant to display inline in the viewer's <iframe>; passing a download
        // name would set Content-Disposition: attachment and the browser would download instead.
        // Binary export formats (PDF/Excel/CSV/Word) keep the filename so they download.
        bool inline = result.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
        return Results.File(result.Content, result.ContentType, inline ? null : result.FileName);
    }

    private static IResult ParameterDefinitions(
        HttpContext ctx, ReportCatalog catalog, MockReportServerAuthOptions auth, string rawPath)
    {
        if (!Authorized(ctx, auth, out var challenge))
        {
            return challenge!;
        }

        var path = NormalizePath(rawPath);
        if (string.IsNullOrEmpty(path))
        {
            return SsrsErrors.MissingReportPath();
        }

        var report = catalog.Find(path);
        if (report is null)
        {
            return SsrsErrors.NotFound(path);
        }

        var definitions = report.Parameters.Select(p => new
        {
            name = p.Name,
            type = p.Type.ToString(),
            prompt = p.Prompt,
            nullable = p.Nullable,
            multiValue = p.MultiValue,
            required = p.IsRequired,
            defaultValue = p.DefaultValue,
            validValues = p.ValidValues,
        });

        return Results.Json(new { report = report.Path, title = report.Title, value = definitions });
    }

    /// <summary>
    /// Resolves declared parameters from the query, applying defaults and
    /// enforcing required-ness and valid-value lists. Multi-value parameters
    /// keep every repeated occurrence of the key.
    /// </summary>
    private static bool TryResolveParameters(
        ReportDefinition report,
        IQueryCollection query,
        out IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        out string? error)
    {
        var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var p in report.Parameters)
        {
            var supplied = query.TryGetValue(p.Name, out var raw)
                ? raw.Where(v => v is not null).Select(v => v!).ToList()
                : new List<string>();

            if (supplied.Count == 0)
            {
                if (p.IsRequired)
                {
                    error = $"The value for the parameter '{p.Name}' is not specified. (rsParameterError)";
                    values = resolved;
                    return false;
                }

                if (p.DefaultValue is not null)
                {
                    supplied.Add(p.DefaultValue);
                }
                else
                {
                    // Nullable with no default: record an empty list.
                    resolved[p.Name] = Array.Empty<string>();
                    continue;
                }
            }

            if (!p.MultiValue && supplied.Count > 1)
            {
                error = $"The parameter '{p.Name}' does not accept multiple values. (rsParameterError)";
                values = resolved;
                return false;
            }

            foreach (var v in supplied)
            {
                if (!ValidateValue(p, v, out var valueError))
                {
                    error = valueError;
                    values = resolved;
                    return false;
                }
            }

            resolved[p.Name] = supplied;
        }

        values = resolved;
        return true;
    }

    private static bool ValidateValue(ReportParameter p, string value, out string? error)
    {
        error = null;

        if (p.ValidValues is { Count: > 0 } &&
            !p.ValidValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            error = $"The value '{value}' is not valid for parameter '{p.Name}'. (rsParameterError)";
            return false;
        }

        switch (p.Type)
        {
            case ReportParameterType.Integer when !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _):
                error = $"The value '{value}' for parameter '{p.Name}' is not a valid Integer. (rsParameterError)";
                return false;
            case ReportParameterType.Float when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                error = $"The value '{value}' for parameter '{p.Name}' is not a valid Float. (rsParameterError)";
                return false;
            case ReportParameterType.Boolean when !bool.TryParse(value, out _):
                error = $"The value '{value}' for parameter '{p.Name}' is not a valid Boolean. (rsParameterError)";
                return false;
            case ReportParameterType.DateTime when !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                error = $"The value '{value}' for parameter '{p.Name}' is not a valid DateTime. (rsParameterError)";
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Extracts the SSRS report path: the first query token that is a bare key
    /// with an empty value (e.g. <c>?/Sales/Monthly</c>). The URL-encoded form
    /// <c>?%2fSales%2fMonthly</c> is decoded too. Returns <c>null</c> if none.
    /// </summary>
    internal static string? ExtractReportPath(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return null;
        }

        var trimmed = queryString.StartsWith('?') ? queryString[1..] : queryString;
        foreach (var token in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            // The path token has no '=' (it is a key with empty value).
            if (token.Contains('='))
            {
                continue;
            }

            var decoded = Uri.UnescapeDataString(token);
            if (decoded.StartsWith('/'))
            {
                return NormalizePath(decoded);
            }
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var decoded = Uri.UnescapeDataString(path).Trim();
        if (decoded.Length == 0)
        {
            return string.Empty;
        }

        if (!decoded.StartsWith('/'))
        {
            decoded = "/" + decoded;
        }

        return decoded.Length > 1 ? decoded.TrimEnd('/') : decoded;
    }

    private static bool Authorized(HttpContext ctx, MockReportServerAuthOptions auth, out IResult? challenge)
    {
        challenge = null;
        if (!auth.Enabled)
        {
            return true;
        }

        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) &&
            TryDecodeBasic(header["Basic ".Length..], out var user, out var pass) &&
            string.Equals(user, auth.Username, StringComparison.Ordinal) &&
            string.Equals(pass, auth.Password, StringComparison.Ordinal))
        {
            return true;
        }

        ctx.Response.Headers.WWWAuthenticate = $"Basic realm=\"{auth.Realm}\", charset=\"UTF-8\"";
        challenge = Results.Content(
            $"{SsrsErrors.ItemNotFound.Replace("ItemNotFound", "AccessDenied")}: Authentication required.",
            "text/plain; charset=utf-8",
            statusCode: StatusCodes.Status401Unauthorized);
        return false;
    }

    private static bool TryDecodeBasic(string encoded, out string user, out string password)
    {
        user = string.Empty;
        password = string.Empty;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded.Trim()));
            var idx = decoded.IndexOf(':');
            if (idx < 0)
            {
                return false;
            }

            user = decoded[..idx];
            password = decoded[(idx + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? FirstOrDefault(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => defaultValue,
        };
    }
}
