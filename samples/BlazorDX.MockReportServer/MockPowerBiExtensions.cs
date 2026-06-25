using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BlazorDX.MockReportServer;

/// <summary>
/// Emulates the slice of the Power BI REST API the embed-token flow uses, so
/// <c>PowerBiEmbedService</c> and its tests can validate against a faithful,
/// deterministic stand-in instead of a real Power BI tenant. Two endpoints under
/// the documented <c>/v1.0/myorg</c> prefix:
/// <list type="bullet">
///   <item><c>GET groups/{groupId}/reports/{reportId}</c> → report info with a
///   deterministic <c>embedUrl</c>.</item>
///   <item><c>POST groups/{groupId}/reports/{reportId}/GenerateToken</c> → a fake
///   embed token + tokenId + expiration.</item>
/// </list>
/// <para>
/// IMPORTANT: only the REST <em>contract</em> (shapes, status codes, the
/// <c>Authorization: Bearer</c> requirement) is emulated. The returned embed token
/// is a deterministic FAKE and is NOT usable against the real Power BI service.
/// </para>
/// Every endpoint requires an <c>Authorization: Bearer ...</c> header and returns
/// <c>401</c> without one, exercising a client's AAD-token pass-through. An unknown
/// report id returns <c>404</c>.
/// </summary>
public static class MockPowerBiExtensions
{
    /// <summary>The set of report ids the mock recognises, with their display names.</summary>
    private static readonly IReadOnlyDictionary<string, string> KnownReports =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["11111111-1111-1111-1111-111111111111"] = "Quarterly Revenue",
            ["22222222-2222-2222-2222-222222222222"] = "Operations Dashboard",
        };

    /// <summary>
    /// Maps the mock Power BI endpoints under <paramref name="prefix"/>
    /// (default <c>/v1.0/myorg</c>).
    /// </summary>
    public static IEndpointRouteBuilder MapMockPowerBiApi(
        this IEndpointRouteBuilder endpoints, string prefix = "/v1.0/myorg")
    {
        endpoints.MapGet(prefix + "/groups/{groupId}/reports/{reportId}",
            (HttpContext ctx, string groupId, string reportId) => GetReport(ctx, groupId, reportId));

        endpoints.MapPost(prefix + "/groups/{groupId}/reports/{reportId}/GenerateToken",
            (HttpContext ctx, string groupId, string reportId) => GenerateToken(ctx, groupId, reportId));

        return endpoints;
    }

    /// <summary>
    /// GET groups/{groupId}/reports/{reportId} → report info. The <c>embedUrl</c>
    /// is deterministic: <c>https://app.powerbi.com/reportEmbed?reportId=...&amp;groupId=...</c>.
    /// </summary>
    private static IResult GetReport(HttpContext ctx, string groupId, string reportId)
    {
        if (!Authorized(ctx, out var challenge))
        {
            return challenge!;
        }

        if (!KnownReports.TryGetValue(reportId, out var name))
        {
            return PowerBiNotFound(reportId);
        }

        var embedUrl = BuildEmbedUrl(groupId, reportId);
        return Results.Json(new
        {
            id = reportId,
            name,
            embedUrl,
            // Deterministic dataset id derived from the report id's first segment.
            datasetId = "dataset-" + reportId,
            webUrl = "https://app.powerbi.com/groups/" + groupId + "/reports/" + reportId,
        });
    }

    /// <summary>
    /// POST .../GenerateToken → a fake embed token. The token is deterministic and
    /// clearly marked fake; the expiration is a fixed hour ahead so assertions are
    /// stable.
    /// </summary>
    private static IResult GenerateToken(HttpContext ctx, string groupId, string reportId)
    {
        if (!Authorized(ctx, out var challenge))
        {
            return challenge!;
        }

        if (!KnownReports.ContainsKey(reportId))
        {
            return PowerBiNotFound(reportId);
        }

        // The real service mints an opaque JWT; we emit a deterministic, clearly
        // fake token so the contract is exercised without implying real validity.
        var token = "FAKE-EMBED-TOKEN." + reportId;
        var tokenId = "token-" + reportId;

        // A fixed, deterministic expiration (UTC, ISO 8601) so tests can assert it.
        var expiration = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero)
            .ToString("o", CultureInfo.InvariantCulture);

        return Results.Json(new
        {
            token,
            tokenId,
            expiration,
        });
    }

    private static string BuildEmbedUrl(string groupId, string reportId)
    {
        var report = Uri.EscapeDataString(reportId);
        var group = Uri.EscapeDataString(groupId);
        return $"https://app.powerbi.com/reportEmbed?reportId={report}&groupId={group}";
    }

    /// <summary>
    /// Requires <c>Authorization: Bearer &lt;token&gt;</c>. The mock does not
    /// validate the token value (it is a stand-in for AAD) — only that a non-empty
    /// bearer token is present, which is enough to prove a client passes it through.
    /// </summary>
    private static bool Authorized(HttpContext ctx, out IResult? challenge)
    {
        challenge = null;
        var header = ctx.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
            header["Bearer ".Length..].Trim().Length > 0)
        {
            return true;
        }

        // Power BI returns 401 with a PowerBINotAuthorizedException-style body.
        challenge = Results.Json(
            new { error = new { code = "TokenExpired", message = "Authorization required: a Bearer token is missing." } },
            statusCode: StatusCodes.Status401Unauthorized);
        return false;
    }

    /// <summary>Power BI-shaped 404 for an unknown report id.</summary>
    private static IResult PowerBiNotFound(string reportId) =>
        Results.Json(
            new { error = new { code = "ItemNotFound", message = $"Report '{reportId}' was not found." } },
            statusCode: StatusCodes.Status404NotFound);
}
