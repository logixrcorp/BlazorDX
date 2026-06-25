using System.Text;
using BlazorDX.Integrations.Reporting;
using Xunit;

namespace BlazorDX.Integrations.Reporting.Tests;

/// <summary>
/// Basic-auth pass-through and URL-construction coverage: with the mock's auth on,
/// configured credentials get a 200 and missing credentials surface the 401 as a
/// <see cref="ReportRenderException"/> rather than crashing; and the URL-Access
/// query string is built in the exact SSRS shape.
/// </summary>
public sealed class ReportingAuthAndUrlTests
{
    [Fact]
    public async Task BasicAuth_WithValidCredentials_Renders()
    {
        using var host = ReportingTestHost.Create(
            authEnabled: true,
            configure: o => o.UseBasicAuth("report", "viewer"));

        var result = await host.Renderer.RenderAsync(
            new ReportRequest("/Sales/Monthly", ReportFormat.Html5, new[]
            {
                new KeyValuePair<string, string>("Region", "West"),
            }));

        Assert.Equal("text/html", result.ContentType);
        Assert.Contains("Monthly Sales", Encoding.UTF8.GetString(result.Content));
    }

    [Fact]
    public async Task BasicAuth_WithoutCredentials_Surfaces401AsReportRenderException()
    {
        using var host = ReportingTestHost.Create(authEnabled: true);

        var ex = await Assert.ThrowsAsync<ReportRenderException>(
            () => host.Renderer.RenderAsync(
                new ReportRequest("/Sales/Monthly", ReportFormat.Html5, new[]
                {
                    new KeyValuePair<string, string>("Region", "West"),
                })));

        Assert.Equal(401, ex.StatusCode);
    }

    [Theory]
    [InlineData(ReportFormat.Html5, "HTML5")]
    [InlineData(ReportFormat.Pdf, "PDF")]
    [InlineData(ReportFormat.Csv, "CSV")]
    [InlineData(ReportFormat.Image, "IMAGE")]
    public void BuildRenderQuery_HasSsrsShape_PathEncodingCommandFormatToolbar(
        ReportFormat format, string expectedToken)
    {
        var request = new ReportRequest(
            "/Sales/Monthly",
            format,
            new[]
            {
                new KeyValuePair<string, string>("Region", "West"),
                new KeyValuePair<string, string>("Region", "East"),
            },
            ShowToolbar: false);

        var query = SsrsUrlBuilder.BuildRenderQuery(request);

        // Report path is the leading token with %2f-encoded slashes.
        Assert.StartsWith("?%2fSales%2fMonthly&", query);
        Assert.Contains("rs:Command=Render", query);
        Assert.Contains("rs:Format=" + expectedToken, query);
        Assert.Contains("rc:Toolbar=false", query);
        // Multi-value parameter repeats its key.
        Assert.Equal(2, CountOccurrences(query, "Region=West") + CountOccurrences(query, "Region=East"));
        Assert.Contains("Region=West", query);
        Assert.Contains("Region=East", query);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
