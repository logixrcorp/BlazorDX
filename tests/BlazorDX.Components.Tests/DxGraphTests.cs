using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// <see cref="DxGraph"/>: a single dynamic entry point dispatching to the right underlying chart
/// by <see cref="GraphKind"/>. Covers every kind's dispatch (does the right child component
/// render, with the right data forwarded), runtime Kind switching, and event forwarding — not
/// each underlying chart's own rendering logic, which is already covered by its own test file.
/// </summary>
public sealed class DxGraphTests : TestContext
{
    public DxGraphTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static IReadOnlyList<ChartPoint> Bars() =>
    [
        new(Category: "A", Y: 10),
        new(Category: "B", Y: 20),
    ];

    [Theory]
    [InlineData(GraphKind.Bar, "rect.dx-bar-rect")]
    [InlineData(GraphKind.Pie, "path.dx-pie-slice")]
    [InlineData(GraphKind.Scatter, "circle.dx-scatter-dot")]
    [InlineData(GraphKind.Funnel, "polygon.dx-funnel-stage")]
    [InlineData(GraphKind.Candlestick, "g.dx-candle")]
    [InlineData(GraphKind.Waterfall, "rect.dx-waterfall-rect")]
    [InlineData(GraphKind.Bubble, "circle.dx-bubble-dot")]
    [InlineData(GraphKind.Heatmap, "rect.dx-heatmap-cell")]
    [InlineData(GraphKind.Sparkline, "polyline.dx-sparkline-line")]
    public void Points_kinds_render_the_matching_underlying_chart(GraphKind kind, string selector)
    {
        IRenderedComponent<DxGraph> chart = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, kind)
            .Add(c => c.Points, Bars()));

        Assert.NotEmpty(chart.FindAll(selector));
    }

    [Fact]
    public void Line_and_Area_kinds_render_via_the_compute_backed_charts()
    {
        List<ChartPoint> series = [new(0, 0), new(1, 5), new(2, 2)];

        IRenderedComponent<DxGraph> line = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Line)
            .Add(c => c.Points, series));
        Assert.Single(line.FindAll("polyline.dx-chart-line"));

        IRenderedComponent<DxGraph> area = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Area)
            .Add(c => c.Points, series));
        Assert.Single(area.FindAll("polygon.dx-area-fill"));
    }

    [Fact]
    public void StackedBar_forwards_Categories_and_Stacked()
    {
        List<ChartPoint> points =
        [
            new(Category: "Q1", Y: 10, Series: "A"),
            new(Category: "Q1", Y: 5, Series: "B"),
        ];
        IRenderedComponent<DxGraph> chart = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.StackedBar)
            .Add(c => c.Categories, new[] { "Q1" })
            .Add(c => c.Points, points));

        Assert.Equal(2, chart.FindAll("rect.dx-bar-rect").Count);
    }

    [Fact]
    public void Radar_forwards_Axes()
    {
        List<ChartPoint> points = [new(Category: "Speed", Y: 3, Series: "A"), new(Category: "Power", Y: 5, Series: "A")];
        IRenderedComponent<DxGraph> chart = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Radar)
            .Add(c => c.Axes, new[] { "Speed", "Power" })
            .Add(c => c.Points, points));

        var svg = chart.Find("svg");
        Assert.Contains("dx-radar", svg.ClassName);
        // The aria-label reports the axis count DxRadarChart itself computed from Axes -- proving
        // the 2-item Axes list actually made it through DxGraph, not just an empty default.
        Assert.Equal("Radar chart of 1 series over 2 axes", svg.GetAttribute("aria-label"));
    }

    [Fact]
    public void Treemap_and_Sunburst_read_Root()
    {
        ChartTreeNode tree = new("All", Children: [new("Leaf", Value: 10)]);

        IRenderedComponent<DxGraph> treemap = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Treemap)
            .Add(c => c.Root, tree));
        Assert.Single(treemap.FindAll("rect.dx-treemap-cell"));

        IRenderedComponent<DxGraph> sunburst = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Sunburst)
            .Add(c => c.Root, tree));
        Assert.Single(sunburst.FindAll("path.dx-sunburst-arc"));
    }

    [Fact]
    public void RadialGauge_and_LinearGauge_read_Value_and_Max()
    {
        IRenderedComponent<DxGraph> radial = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.RadialGauge)
            .Add(c => c.Value, 72)
            .Add(c => c.Max, 200));
        Assert.Contains("dx-radial-gauge", radial.Find("svg").ClassName);
        Assert.Equal("72", radial.Find("text.dx-gauge-readout").TextContent);

        IRenderedComponent<DxGraph> linear = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.LinearGauge)
            .Add(c => c.Value, 30)
            .Add(c => c.Max, 60));
        Assert.Contains("dx-linear-gauge", linear.Find("svg").ClassName);
    }

    [Fact]
    public void Histogram_reads_RawValues_and_Bins()
    {
        double[] values = Enumerable.Range(0, 50).Select(i => (double)i).ToArray();
        IRenderedComponent<DxGraph> chart = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Histogram)
            .Add(c => c.RawValues, values)
            .Add(c => c.Bins, 5));

        Assert.Equal(5, chart.FindAll("rect.dx-bar-rect").Count);
    }

    [Fact]
    public void Switching_Kind_at_runtime_swaps_the_rendered_chart_with_the_same_Points()
    {
        IRenderedComponent<DxGraph> chart = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Bar)
            .Add(c => c.Points, Bars()));
        Assert.NotEmpty(chart.FindAll("rect.dx-bar-rect"));
        Assert.Empty(chart.FindAll("polyline.dx-chart-line"));

        chart.SetParametersAndRender(p => p.Add(c => c.Kind, GraphKind.Line));

        Assert.Empty(chart.FindAll("rect.dx-bar-rect"));
        Assert.Single(chart.FindAll("polyline.dx-chart-line"));
    }

    [Fact]
    public void OnPointSelected_forwards_through_to_the_underlying_chart()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxGraph> chart = RenderComponent<DxGraph>(p => p
            .Add(c => c.Kind, GraphKind.Bar)
            .Add(c => c.Points, Bars())
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("rect.dx-bar-rect")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal("B", selected!.Value.Point.Category);
    }
}
