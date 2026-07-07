using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + downsampling for the line chart (managed compute backend).</summary>
public sealed class DxLineChartTests : TestContext
{
    public DxLineChartTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    [Fact]
    public void Renders_an_svg_polyline_with_about_threshold_points()
    {
        IReadOnlyList<ChartPoint> data = Enumerable.Range(0, 1000)
            .Select(i => new ChartPoint(X: i, Y: Math.Sin(i / 40.0))).ToList();

        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(parameters => parameters
            .Add(c => c.Points, data)
            .Add(c => c.Threshold, 100));

        var polyline = chart.Find("polyline");
        string[] coordinates = polyline.GetAttribute("points")!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, coordinates.Length); // downsampled to the threshold
    }

    [Fact]
    public void Caption_reports_downsampled_and_total_counts()
    {
        IReadOnlyList<ChartPoint> data = Enumerable.Range(0, 500)
            .Select(i => new ChartPoint(X: i, Y: i)).ToList();

        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(parameters => parameters
            .Add(c => c.Points, data)
            .Add(c => c.Threshold, 80));

        string caption = chart.Find(".dx-chart-caption").TextContent;
        Assert.Contains("80 of 500 points", caption);
        Assert.Contains("Managed C#", caption);
    }
}
