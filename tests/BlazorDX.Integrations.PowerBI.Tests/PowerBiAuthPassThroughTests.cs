using System.Net;
using BlazorDX.Integrations.PowerBI;
using Xunit;

namespace BlazorDX.Integrations.PowerBI.Tests;

/// <summary>
/// Proves the AAD bearer token actually travels to Power BI: the mock returns 401
/// when no <c>Authorization: Bearer</c> header is present and 200 when it is. The
/// embed service (which supplies the token from its provider) succeeds, while a raw
/// client with no header is rejected by the same endpoints — so a passing pair
/// demonstrates the service's server-side bearer pass-through, with the token never
/// appearing in a URL.
/// </summary>
public sealed class PowerBiAuthPassThroughTests
{
    [Fact]
    public async Task MockGetReport_WithoutBearerHeader_Returns401()
    {
        using var host = PowerBiTestHost.Create();

        var response = await host.RawMockClient.GetAsync(
            $"/v1.0/myorg/groups/{PowerBiTestHost.WorkspaceId}/reports/{PowerBiTestHost.KnownReportId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MockGenerateToken_WithoutBearerHeader_Returns401()
    {
        using var host = PowerBiTestHost.Create();

        var response = await host.RawMockClient.PostAsync(
            $"/v1.0/myorg/groups/{PowerBiTestHost.WorkspaceId}/reports/{PowerBiTestHost.KnownReportId}/GenerateToken",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EmbedService_WithBearerFromProvider_Succeeds()
    {
        // The same endpoints that 401 above now return 200 because the service
        // attaches the bearer from its token provider.
        using var host = PowerBiTestHost.Create();

        var config = await host.EmbedService.CreateEmbedConfigAsync(PowerBiTestHost.KnownReportId);

        Assert.False(string.IsNullOrEmpty(config.EmbedToken));
    }
}
