using BlazorDX.Integrations.Reporting;
using Xunit;

namespace BlazorDX.Integrations.Reporting.Tests;

/// <summary>
/// Exercises <see cref="SsrsRestParameterSource"/> over real HTTP against the mock
/// REST parameter-definitions endpoint, proving the source-generated JSON parse
/// maps the wire shape into the metadata model (no reflection involved).
/// </summary>
public sealed class SsrsRestParameterSourceTests
{
    [Fact]
    public async Task GetParameters_SalesMonthly_MapsRegionAndYearMetadata()
    {
        using var host = ReportingTestHost.Create();

        var parameters = await host.ParameterSource.GetParametersAsync("/Sales/Monthly");

        Assert.Equal(2, parameters.Count);

        var region = Assert.Single(parameters, p => p.Name == "Region");
        Assert.Equal(ReportParameterDataType.String, region.Type);
        Assert.True(region.MultiValue);
        Assert.True(region.Required);
        Assert.NotNull(region.ValidValues);
        Assert.Contains("West", region.ValidValues!);
        Assert.Contains("South", region.ValidValues!);

        var year = Assert.Single(parameters, p => p.Name == "Year");
        Assert.Equal(ReportParameterDataType.Integer, year.Type);
        Assert.Equal("2026", year.DefaultValue);
        Assert.False(year.Required);
    }

    [Fact]
    public async Task GetParameters_UnknownReport_ThrowsReportRenderException()
    {
        using var host = ReportingTestHost.Create();

        var ex = await Assert.ThrowsAsync<ReportRenderException>(
            () => host.ParameterSource.GetParametersAsync("/Nope/Missing"));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("rsItemNotFound", ex.SsrsErrorCode);
    }
}
