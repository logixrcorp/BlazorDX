using System.Net;
using System.Text;
using System.Text.Json;
using BlazorDX.Integrations.PowerBI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Integrations.PowerBI.Tests;

/// <summary>
/// Asserts the exact wire details the service produces by capturing every outgoing
/// request through a recording <see cref="HttpMessageHandler"/> while still wiring
/// the production DI graph (typed client via the factory, scoped service, the
/// swappable token provider). This pins down the load-bearing contract:
/// the bearer header value, the relative paths, the GenerateToken POST body
/// (<c>{"accessLevel":"View"}</c>), and that the token never appears in a URL.
/// </summary>
public sealed class PowerBiWireContractTests
{
    private const string Workspace = "ws-contract";
    private const string ReportId = "11111111-1111-1111-1111-111111111111";
    private const string Token = "secret-bearer-value";

    [Fact]
    public async Task EmbedFlow_SendsBearerHeader_AndGenerateTokenBody_AndKeepsTokenOutOfUrl()
    {
        var recorder = new RecordingHandler();
        var service = BuildService(recorder);

        var config = await service.CreateEmbedConfigAsync(ReportId);

        Assert.False(string.IsNullOrEmpty(config.EmbedToken));

        // Two calls in order: GET report, then POST GenerateToken.
        Assert.Equal(2, recorder.Requests.Count);

        var get = recorder.Requests[0];
        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Equal(
            $"/v1.0/myorg/groups/{Workspace}/reports/{ReportId}",
            get.Uri!.AbsolutePath);

        var post = recorder.Requests[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal(
            $"/v1.0/myorg/groups/{Workspace}/reports/{ReportId}/GenerateToken",
            post.Uri!.AbsolutePath);

        // The bearer token from the provider is attached to BOTH requests...
        foreach (var req in recorder.Requests)
        {
            Assert.Equal("Bearer", req.AuthScheme);
            Assert.Equal(Token, req.AuthParameter);

            // ...and NEVER appears anywhere in the URL (server-side only, ADR 0010).
            Assert.DoesNotContain(Token, req.Uri!.ToString(), StringComparison.Ordinal);
        }

        // The GenerateToken POST body is exactly the documented {"accessLevel":"View"}.
        using var parsed = JsonDocument.Parse(post.Body!);
        var only = Assert.Single(parsed.RootElement.EnumerateObject());
        Assert.Equal("accessLevel", only.Name);
        Assert.Equal("View", only.Value.GetString());
    }

    [Fact]
    public async Task GenerateTokenFailure_AfterReportResolves_SurfacesAsEmbedException()
    {
        // Report GET succeeds; GenerateToken POST fails — the second-stage failure
        // must surface as a PowerBiEmbedException tagged to the GenerateToken stage,
        // not a raw crash.
        var recorder = new RecordingHandler
        {
            GenerateTokenStatus = HttpStatusCode.BadRequest,
            GenerateTokenBody = "{\"error\":{\"code\":\"InvalidRequest\"}}",
        };
        var service = BuildService(recorder);

        var ex = await Assert.ThrowsAsync<PowerBiEmbedException>(
            () => service.CreateEmbedConfigAsync(ReportId));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(PowerBiEmbedStage.GenerateToken, ex.Stage);
    }

    [Fact]
    public async Task ProviderFailure_IsMappedToEmbedException_NotRawProviderError()
    {
        var recorder = new RecordingHandler();
        var service = BuildService(recorder, new ThrowingTokenProvider());

        var ex = await Assert.ThrowsAsync<PowerBiEmbedException>(
            () => service.CreateEmbedConfigAsync(ReportId));

        Assert.Equal(PowerBiEmbedStage.None, ex.Stage);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        // The flow never reached the network.
        Assert.Empty(recorder.Requests);
    }

    private static IPowerBiEmbedService BuildService(
        RecordingHandler handler, IPowerBiTokenProvider? provider = null)
    {
        var services = new ServiceCollection();
        var builder = services.AddBlazorDXPowerBi(options =>
        {
            options.ApiBaseUrl = "https://api.powerbi.com";
            options.WorkspaceId = Workspace;
        });
        builder.UseTokenProvider(provider ?? new StaticPowerBiTokenProvider(Token));

        services.AddHttpClient(PowerBiServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IPowerBiEmbedService>();
    }

    private sealed class ThrowingTokenProvider : IPowerBiTokenProvider
    {
        public Task<string> GetAadTokenAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("MSAL is unavailable.");
    }

    /// <summary>
    /// Records each outgoing request and returns canned, contract-shaped responses:
    /// a report-info JSON for the GET and a token JSON for the GenerateToken POST
    /// (overridable to simulate failure).
    /// </summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        public HttpStatusCode GenerateTokenStatus { get; init; } = HttpStatusCode.OK;

        public string? GenerateTokenBody { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));

            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/GenerateToken", StringComparison.Ordinal))
            {
                var content = GenerateTokenBody
                    ?? "{\"token\":\"EMBED-TOKEN\",\"tokenId\":\"tid\",\"expiration\":\"2099-01-01T00:00:00Z\"}";
                return new HttpResponseMessage(GenerateTokenStatus)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json"),
                };
            }

            // Report-info GET.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"id\":\"{ReportId}\",\"name\":\"Quarterly Revenue\"," +
                    $"\"embedUrl\":\"https://app.powerbi.com/reportEmbed?reportId={ReportId}\"," +
                    "\"datasetId\":\"ds-1\"}",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? Uri,
        string? AuthScheme,
        string? AuthParameter,
        string? Body);
}
