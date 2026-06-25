using System.Net;

namespace BlazorDX.Integrations.Reporting;

/// <summary>
/// The default <see cref="IReportRenderer"/>: renders a report by GETting the
/// SSRS URL-Access endpoint over the injected <see cref="HttpClient"/> and
/// returning the bytes plus content type. The <see cref="HttpClient"/> is supplied
/// by <see cref="IHttpClientFactory"/> with its base address and any Basic-auth
/// header already configured (see <c>ReportingServiceCollectionExtensions</c>), so
/// this type only shapes the request URL and maps failures.
/// </summary>
internal sealed class SsrsUrlAccessReportClient : IReportRenderer
{
    private readonly HttpClient _httpClient;

    public SsrsUrlAccessReportClient(HttpClient httpClient) =>
        _httpClient = httpClient;

    public async Task<RenderedReport> RenderAsync(ReportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = SsrsUrlBuilder.BuildRenderQuery(request);
        var relativeUri = new Uri(query, UriKind.Relative);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .GetAsync(relativeUri, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ReportRenderException(
                $"Could not reach the report server for '{request.ReportPath}'.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                await ThrowForFailureAsync(response, request.ReportPath, ct).ConfigureAwait(false);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            return new RenderedReport(bytes, contentType, request.Format);
        }
    }

    /// <summary>
    /// Reads the failing response body, extracts any SSRS <c>rs*</c> code, and
    /// raises a descriptive <see cref="ReportRenderException"/>. Always throws.
    /// </summary>
    private static async Task ThrowForFailureAsync(
        HttpResponseMessage response, string reportPath, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            body = string.Empty;
        }

        var code = SsrsErrorCodes.Extract(body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ReportRenderException(
                $"The report server rejected the request for '{reportPath}' (401 Unauthorized). " +
                "Check the configured credentials.")
            {
                StatusCode = status,
                SsrsErrorCode = code,
            };
        }

        var detail = code is not null
            ? $"{code}: {SsrsErrorCodes.Summarize(body)}"
            : SsrsErrorCodes.Summarize(body);

        throw new ReportRenderException(
            $"Rendering '{reportPath}' failed ({status}). {detail}".TrimEnd())
        {
            StatusCode = status,
            SsrsErrorCode = code,
        };
    }
}
