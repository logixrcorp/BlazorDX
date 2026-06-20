using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Bar, area, pie, and histogram chart rendering (managed compute backend).</summary>
public sealed class DxChartsTests : TestContext
{
    public DxChartsTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static IReadOnlyList<ChartBar> Bars() =>
    [
        new ChartBar("A", 10),
        new ChartBar("B", 20),
        new ChartBar("C", 5),
    ];

    [Fact]
    public void BarChart_renders_a_rect_per_bar_with_labels()
    {
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(parameters => parameters
            .Add(c => c.Bars, Bars()));

        Assert.Equal(3, chart.FindAll("rect.dx-bar-rect").Count);
        Assert.Equal(3, chart.FindAll("text.dx-bar-label").Count);
        Assert.Contains("Bar chart with 3 categories", chart.Markup);
    }

    [Fact]
    public void BarChart_tallest_bar_has_greatest_height()
    {
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(parameters => parameters
            .Add(c => c.Bars, Bars()));

        var rects = chart.FindAll("rect.dx-bar-rect");
        double hB = double.Parse(rects[1].GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);
        double hC = double.Parse(rects[2].GetAttribute("height")!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(hB > hC);   // B=20 taller than C=5
    }

    [Fact]
    public void AreaChart_renders_fill_polygon_and_line()
    {
        double[] values = Enumerable.Range(0, 500).Select(i => Math.Sin(i / 30.0) + 2).ToArray();
        IRenderedComponent<DxAreaChart> chart = RenderComponent<DxAreaChart>(parameters => parameters
            .Add(c => c.Values, values)
            .Add(c => c.Threshold, 60));

        Assert.NotNull(chart.Find("polygon.dx-area-fill").GetAttribute("points"));
        Assert.NotEmpty(chart.Find("polyline").GetAttribute("points")!);
        Assert.Contains("Managed C#", chart.Find(".dx-chart-caption").TextContent);
    }

    [Fact]
    public void PieChart_renders_a_slice_and_legend_entry_per_category()
    {
        IRenderedComponent<DxPieChart> chart = RenderComponent<DxPieChart>(parameters => parameters
            .Add(c => c.Slices, Bars()));

        Assert.Equal(3, chart.FindAll("path.dx-pie-slice").Count);
        Assert.Equal(3, chart.FindAll(".dx-pie-legend-item").Count);
        Assert.Empty(chart.FindAll("circle.dx-pie-hole"));   // not a donut
    }

    [Fact]
    public void PieChart_donut_adds_a_center_hole()
    {
        IRenderedComponent<DxPieChart> chart = RenderComponent<DxPieChart>(parameters => parameters
            .Add(c => c.Slices, Bars())
            .Add(c => c.Donut, true));

        Assert.Single(chart.FindAll("circle.dx-pie-hole"));
    }

    [Fact]
    public void Histogram_renders_one_bar_per_bin_and_names_backend()
    {
        double[] values = Enumerable.Range(0, 1000).Select(i => (double)(i % 100)).ToArray();
        IRenderedComponent<DxHistogram> chart = RenderComponent<DxHistogram>(parameters => parameters
            .Add(c => c.Values, values)
            .Add(c => c.Bins, 10));

        Assert.Equal(10, chart.FindAll("rect.dx-bar-rect").Count);
        string caption = chart.Find(".dx-chart-caption").TextContent;
        Assert.Contains("10 bins", caption);
        Assert.Contains("Managed C#", caption);
    }
}
