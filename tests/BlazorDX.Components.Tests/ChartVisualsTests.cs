using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The shared "futuristic" visual upgrade — entrance animation classes and the opt-in
/// <c>Gradient</c> fill — applied across the chart family (see ChartGradients.cs, dx-chart.css).
/// </summary>
public sealed class ChartVisualsTests : TestContext
{
    public ChartVisualsTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static IReadOnlyList<ChartPoint> Bars() =>
    [
        new(Category: "A", Y: 10),
        new(Category: "B", Y: 20),
    ];

    [Fact]
    public void Bar_rects_carry_the_drawin_class_by_default()
    {
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p.Add(c => c.Points, Bars()));

        Assert.All(chart.FindAll("rect.dx-bar-rect"), r => Assert.Contains("dx-chart-drawin", r.ClassName));
    }

    [Fact]
    public void Gradient_fills_bars_with_a_generated_gradient_url_and_emits_defs()
    {
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p
            .Add(c => c.Points, Bars())
            .Add(c => c.Gradient, true));

        Assert.Equal(2, chart.FindAll("defs linearGradient").Count);   // 2 bars, 2 distinct palette colors
        var rect = chart.Find("rect.dx-bar-rect");
        Assert.StartsWith("url(#", rect.GetAttribute("fill"));
    }

    [Fact]
    public void Without_Gradient_bars_keep_a_flat_color_fill()
    {
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p.Add(c => c.Points, Bars()));

        Assert.Empty(chart.FindAll("defs linearGradient"));
        var rect = chart.Find("rect.dx-bar-rect");
        Assert.DoesNotContain("url(", rect.GetAttribute("fill"));
    }

    [Fact]
    public void Line_chart_path_carries_the_reveal_class()
    {
        List<ChartPoint> points = [new(0, 0), new(1, 5), new(2, 2)];
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p.Add(c => c.Points, points));

        Assert.Contains("dx-chart-reveal", chart.Find("polyline.dx-chart-line").ClassName);
    }
}
