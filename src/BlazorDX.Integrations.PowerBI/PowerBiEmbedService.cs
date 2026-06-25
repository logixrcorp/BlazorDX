using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// The default <see cref="IPowerBiEmbedService"/>: runs the documented two-call
/// Power BI embed flow over the injected typed <see cref="HttpClient"/>.
/// <list type="number">
///   <item><c>GET groups/{groupId}/reports/{reportId}</c> → embed URL + dataset.</item>
///   <item><c>POST groups/{groupId}/reports/{reportId}/GenerateToken</c> with
///   <c>{"accessLevel":"View"}</c> → the short-lived embed token + expiration.</item>
/// </list>
/// The Azure AD bearer token comes from the injected <see cref="IPowerBiTokenProvider"/>
/// and is applied as the <c>Authorization</c> header per request — server-side
/// only, never in a URL or to the browser (ADR 0010). JSON is read and written
/// only through the source-generated <see cref="PowerBiJsonContext"/> (ADR 0002).
/// Failures map to <see cref="PowerBiEmbedException"/> rather than escaping raw.
/// </summary>
internal sealed class PowerBiEmbedService : IPowerBiEmbedService
{
    private readonly HttpClient _httpClient;
    private readonly IPowerBiTokenProvider _tokenProvider;
    private readonly PowerBiOptions _options;

    public PowerBiEmbedService(
        HttpClient httpClient, IPowerBiTokenProvider tokenProvider, PowerBiOptions options)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options;
    }

    public async Task<PowerBiReportInfo> GetReportAsync(string reportId, CancellationToken ct = default)
    {
        ValidateReportId(reportId);
        var auth = await GetAuthHeaderAsync(ct).ConfigureAwait(false);
        return await ResolveReportAsync(reportId, auth, ct).ConfigureAwait(false);
    }

    public async Task<PowerBiEmbedConfig> CreateEmbedConfigAsync(
        string reportId, CancellationToken ct = default)
    {
        ValidateReportId(reportId);

        var auth = await GetAuthHeaderAsync(ct).ConfigureAwait(false);
        var report = await ResolveReportAsync(reportId, auth, ct).ConfigureAwait(false);
        var token = await GenerateTokenAsync(reportId, auth, ct).ConfigureAwait(false);

        return new PowerBiEmbedConfig(
            EmbedUrl: report.EmbedUrl,
            EmbedToken: token.Token ?? string.Empty,
            ReportId: report.Id,
            TokenType: PowerBiTokenType.Embed,
            Expiration: token.Expiration ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// GET groups/{groupId}/reports/{reportId} → report metadata. The relative path
    /// is built once here; the typed client's base address carries
    /// <c>/v1.0/myorg/</c>.
    /// </summary>
    private async Task<PowerBiReportInfo> ResolveReportAsync(
        string reportId, AuthenticationHeaderValue auth, CancellationToken ct)
    {
        var path = BuildReportPath(reportId);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative))
        {
            Headers = { Authorization = auth },
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PowerBiEmbedException(
                $"Could not reach the Power BI service for report '{reportId}'.", ex)
            {
                Stage = PowerBiEmbedStage.ResolveReport,
            };
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildFailureAsync(
                    response, PowerBiEmbedStage.ResolveReport, reportId, ct).ConfigureAwait(false);
            }

            var parsed = await ReadJsonAsync(
                response, PowerBiJsonContext.Default.PowerBiReportResponse,
                PowerBiEmbedStage.ResolveReport, reportId, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(parsed.EmbedUrl))
            {
                throw new PowerBiEmbedException(
                    $"The Power BI report response for '{reportId}' carried no embedUrl.")
                {
                    Stage = PowerBiEmbedStage.ResolveReport,
                };
            }

            return new PowerBiReportInfo(
                Id: string.IsNullOrEmpty(parsed.Id) ? reportId : parsed.Id,
                Name: parsed.Name ?? string.Empty,
                EmbedUrl: parsed.EmbedUrl,
                DatasetId: parsed.DatasetId);
        }
    }

    /// <summary>
    /// POST groups/{groupId}/reports/{reportId}/GenerateToken with body
    /// <c>{"accessLevel":"View"}</c> → embed token + expiration.
    /// </summary>
    private async Task<GenerateTokenResponse> GenerateTokenAsync(
        string reportId, AuthenticationHeaderValue auth, CancellationToken ct)
    {
        var path = BuildReportPath(reportId) + "/GenerateToken";

        // Serialize the body through the source-gen context (no reflection).
        var content = JsonContent.Create(
            new GenerateTokenRequest(),
            PowerBiJsonContext.Default.GenerateTokenRequest);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Headers = { Authorization = auth },
            Content = content,
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new PowerBiEmbedException(
                $"Could not reach the Power BI service to mint a token for '{reportId}'.", ex)
            {
                Stage = PowerBiEmbedStage.GenerateToken,
            };
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildFailureAsync(
                    response, PowerBiEmbedStage.GenerateToken, reportId, ct).ConfigureAwait(false);
            }

            var parsed = await ReadJsonAsync(
                response, PowerBiJsonContext.Default.GenerateTokenResponse,
                PowerBiEmbedStage.GenerateToken, reportId, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(parsed.Token))
            {
                throw new PowerBiEmbedException(
                    $"The Power BI GenerateToken response for '{reportId}' carried no token.")
                {
                    Stage = PowerBiEmbedStage.GenerateToken,
                };
            }

            return parsed;
        }
    }

    /// <summary>
    /// Acquires the AAD bearer token from the swappable provider and wraps it as a
    /// <c>Bearer</c> header. The provider's own failures are mapped to
    /// <see cref="PowerBiEmbedException"/> so the flow never crashes raw.
    /// </summary>
    private async Task<AuthenticationHeaderValue> GetAuthHeaderAsync(CancellationToken ct)
    {
        string token;
        try
        {
            token = await _tokenProvider.GetAadTokenAsync(ct).ConfigureAwait(false);
        }
        catch (PowerBiEmbedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PowerBiEmbedException(
                "The Azure AD token provider failed to supply a bearer token.", ex)
            {
                Stage = PowerBiEmbedStage.None,
            };
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new PowerBiEmbedException(
                "The Azure AD token provider returned an empty bearer token.")
            {
                Stage = PowerBiEmbedStage.None,
            };
        }

        return new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Builds the relative path <c>groups/{groupId}/reports/{reportId}</c> against
    /// the <c>/v1.0/myorg/</c> base. Both segments are URL-escaped so a stray
    /// character cannot break out of the path.
    /// </summary>
    private string BuildReportPath(string reportId)
    {
        var group = Uri.EscapeDataString(_options.WorkspaceId);
        var report = Uri.EscapeDataString(reportId);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"groups/{group}/reports/{report}");
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        PowerBiEmbedStage stage,
        string reportId,
        CancellationToken ct)
        where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try
        {
            var parsed = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
            if (parsed is null)
            {
                throw new PowerBiEmbedException(
                    $"The Power BI response for '{reportId}' was empty.") { Stage = stage };
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            throw new PowerBiEmbedException(
                $"The Power BI response for '{reportId}' was not valid JSON.", ex) { Stage = stage };
        }
    }

    /// <summary>
    /// Reads the failing response body and raises a descriptive
    /// <see cref="PowerBiEmbedException"/>. Always returns a throwable exception.
    /// </summary>
    private static async Task<PowerBiEmbedException> BuildFailureAsync(
        HttpResponseMessage response, PowerBiEmbedStage stage, string reportId, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var body = await SafeReadAsync(response, ct).ConfigureAwait(false);
        var detail = Summarize(body);

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                $"Power BI rejected the request for '{reportId}' (401 Unauthorized). " +
                "Check the Azure AD token from the configured token provider.",
            HttpStatusCode.NotFound =>
                $"Power BI report '{reportId}' was not found (404). " +
                "Check the report id and the configured workspace.",
            _ => $"The Power BI request for '{reportId}' failed ({status}). {detail}".TrimEnd(),
        };

        return new PowerBiEmbedException(message)
        {
            StatusCode = status,
            Stage = stage,
        };
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
    }

    /// <summary>Condenses an error body to a single capped line for an exception message.</summary>
    private static string Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var firstLine = body.Trim();
        var newline = firstLine.IndexOfAny(['\r', '\n']);
        if (newline >= 0)
        {
            firstLine = firstLine[..newline].Trim();
        }

        const int Cap = 240;
        return firstLine.Length > Cap ? firstLine[..Cap] + "…" : firstLine;
    }

    private static void ValidateReportId(string reportId)
    {
        if (string.IsNullOrWhiteSpace(reportId))
        {
            throw new ArgumentException("A report id is required.", nameof(reportId));
        }
    }
}
