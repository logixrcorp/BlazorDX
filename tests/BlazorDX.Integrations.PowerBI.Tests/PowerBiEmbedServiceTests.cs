using BlazorDX.Integrations.PowerBI;
using Xunit;

namespace BlazorDX.Integrations.PowerBI.Tests;

/// <summary>
/// Exercises <see cref="IPowerBiEmbedService"/> over real HTTP against the in-memory
/// mock Power BI REST server. A passing test proves the two-call embed flow — report
/// lookup then GenerateToken — built the right paths, carried the bearer token, and
/// parsed the documented responses through the source-gen context end to end.
/// </summary>
public sealed class PowerBiEmbedServiceTests
{
    [Fact]
    public async Task CreateEmbedConfig_KnownReport_ReturnsEmbedUrlAndNonEmptyToken()
    {
        using var host = PowerBiTestHost.Create();

        var config = await host.EmbedService.CreateEmbedConfigAsync(PowerBiTestHost.KnownReportId);

        // EmbedUrl is the deterministic one the mock builds from group + report.
        Assert.Equal(
            $"https://app.powerbi.com/reportEmbed?reportId={PowerBiTestHost.KnownReportId}&groupId={PowerBiTestHost.WorkspaceId}",
            config.EmbedUrl);

        Assert.False(string.IsNullOrEmpty(config.EmbedToken));
        Assert.Equal(PowerBiTestHost.KnownReportId, config.ReportId);
        Assert.Equal(PowerBiTokenType.Embed, config.TokenType);

        // The mock returns a fixed far-future expiration; prove it round-trips.
        Assert.Equal(
            new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero),
            config.Expiration);
    }

    [Fact]
    public async Task GetReport_KnownReport_MapsNameDatasetAndEmbedUrl()
    {
        using var host = PowerBiTestHost.Create();

        var report = await host.EmbedService.GetReportAsync(PowerBiTestHost.KnownReportId);

        Assert.Equal(PowerBiTestHost.KnownReportId, report.Id);
        Assert.Equal("Quarterly Revenue", report.Name);
        Assert.Contains("reportEmbed", report.EmbedUrl, StringComparison.Ordinal);
        Assert.Equal("dataset-" + PowerBiTestHost.KnownReportId, report.DatasetId);
    }

    [Fact]
    public async Task CreateEmbedConfig_UnknownReport_ThrowsEmbedExceptionNotRawCrash()
    {
        using var host = PowerBiTestHost.Create();

        var ex = await Assert.ThrowsAsync<PowerBiEmbedException>(
            () => host.EmbedService.CreateEmbedConfigAsync("99999999-0000-0000-0000-000000000000"));

        Assert.Equal(404, ex.StatusCode);
        // The unknown report fails at the report-resolve step, before GenerateToken.
        Assert.Equal(PowerBiEmbedStage.ResolveReport, ex.Stage);
    }

    [Fact]
    public async Task CreateEmbedConfig_EmptyReportId_ThrowsArgumentException()
    {
        using var host = PowerBiTestHost.Create();

        await Assert.ThrowsAsync<ArgumentException>(
            () => host.EmbedService.CreateEmbedConfigAsync("   "));
    }
}
