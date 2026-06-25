using System.Text;
using BlazorDX.Integrations.Reporting;
using Xunit;

namespace BlazorDX.Integrations.Reporting.Tests;

/// <summary>
/// Exercises <see cref="SsrsUrlAccessReportClient"/> over real HTTP against the
/// in-memory mock SSRS server. Behaviour is asserted through the bytes the mock
/// returns (its HTML echoes parameter values), so a passing test proves the URL
/// the client built actually carried the right path, format, toolbar flag, and
/// (multi-value) parameters end to end.
/// </summary>
public sealed class SsrsUrlAccessReportClientTests
{
    [Fact]
    public async Task RenderHtml5_ReturnsHtmlWithTitleAndEchoedParameter()
    {
        using var host = ReportingTestHost.Create();
        var request = new ReportRequest(
            "/Sales/Monthly",
            ReportFormat.Html5,
            new[] { Param("Region", "West"), Param("Year", "2026") });

        var result = await host.Renderer.RenderAsync(request);

        Assert.Equal("text/html", result.ContentType);
        Assert.Equal(ReportFormat.Html5, result.Format);
        var html = Encoding.UTF8.GetString(result.Content);
        Assert.Contains("<h1>Monthly Sales</h1>", html);
        Assert.Contains("West", html);
        Assert.Contains("2026", html);
    }

    [Fact]
    public async Task RenderHtml5_MultiValueParameter_BothValuesReachServer()
    {
        using var host = ReportingTestHost.Create();
        // Repeated key => multi-value SSRS parameter.
        var request = new ReportRequest(
            "/Sales/Monthly",
            ReportFormat.Html5,
            new[] { Param("Region", "West"), Param("Region", "East") });

        var html = Encoding.UTF8.GetString((await host.Renderer.RenderAsync(request)).Content);

        // The mock builds one set of rows per region; both appearing proves both
        // repeated keys survived URL construction and reached the server.
        Assert.Contains("<td>West</td>", html);
        Assert.Contains("<td>East</td>", html);
    }

    [Fact]
    public async Task RenderHtml5_ToolbarFlag_IsHonoredOnTheWire()
    {
        using var host = ReportingTestHost.Create();

        var withToolbar = Encoding.UTF8.GetString((await host.Renderer.RenderAsync(
            new ReportRequest("/Sales/Monthly", ReportFormat.Html5,
                new[] { Param("Region", "West") }, ShowToolbar: true))).Content);
        Assert.Contains("ssrs-toolbar", withToolbar);

        var withoutToolbar = Encoding.UTF8.GetString((await host.Renderer.RenderAsync(
            new ReportRequest("/Sales/Monthly", ReportFormat.Html5,
                new[] { Param("Region", "West") }, ShowToolbar: false))).Content);
        Assert.DoesNotContain("ssrs-toolbar", withoutToolbar);
    }

    [Fact]
    public async Task RenderPdf_ReturnsPdfContentTypeAndBytes()
    {
        using var host = ReportingTestHost.Create();
        var request = new ReportRequest(
            "/Sales/Monthly", ReportFormat.Pdf, new[] { Param("Region", "East") });

        var result = await host.Renderer.RenderAsync(request);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(ReportFormat.Pdf, result.Format);
        var header = Encoding.ASCII.GetString(result.Content, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public async Task Render_OmittedOptionalParameter_ServerAppliesDefault()
    {
        using var host = ReportingTestHost.Create();
        // Year omitted; the mock applies its 2026 default.
        var request = new ReportRequest(
            "/Sales/Monthly", ReportFormat.Html5, new[] { Param("Region", "West") });

        var html = Encoding.UTF8.GetString((await host.Renderer.RenderAsync(request)).Content);
        Assert.Contains("2026", html);
    }

    [Fact]
    public async Task Render_UnknownReportPath_ThrowsReportRenderExceptionWithItemNotFound()
    {
        using var host = ReportingTestHost.Create();
        var request = new ReportRequest("/Nope/DoesNotExist", ReportFormat.Html5);

        var ex = await Assert.ThrowsAsync<ReportRenderException>(
            () => host.Renderer.RenderAsync(request));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal(SsrsErrorCodesPublic.ItemNotFound, ex.SsrsErrorCode);
    }

    [Fact]
    public async Task Render_MissingRequiredParameter_ThrowsWithParameterError()
    {
        using var host = ReportingTestHost.Create();
        // Region is required and omitted.
        var request = new ReportRequest("/Sales/Monthly", ReportFormat.Html5);

        var ex = await Assert.ThrowsAsync<ReportRenderException>(
            () => host.Renderer.RenderAsync(request));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(SsrsErrorCodesPublic.ParameterError, ex.SsrsErrorCode);
        Assert.Contains("Region", ex.Message, StringComparison.Ordinal);
    }

    private static KeyValuePair<string, string> Param(string key, string value) => new(key, value);

    // Mirror of the internal SSRS code constants for assertions (the client type is
    // internal but the codes are stable strings).
    private static class SsrsErrorCodesPublic
    {
        public const string ItemNotFound = "rsItemNotFound";
        public const string ParameterError = "rsParameterError";
    }
}
