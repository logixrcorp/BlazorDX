using System.Globalization;
using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Scatter and stacked/grouped bar charts.</summary>
public sealed class DxChartsExtraTests : TestContext
{
    [Fact]
    public void Scatter_renders_a_dot_per_point()
    {
        IReadOnlyList<ChartPoint> points = [new(0, 0), new(5, 10), new(10, 2)];
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(parameters => parameters
            .Add(c => c.Points, points));

        Assert.Equal(3, chart.FindAll("circle.dx-scatter-dot").Count);
    }

    [Fact]
    public void Scatter_places_min_x_left_and_max_x_right()
    {
        IReadOnlyList<ChartPoint> points = [new(0, 5), new(100, 5)];
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(parameters => parameters
            .Add(c => c.Points, points)
            .Add(c => c.Width, 200));

        var dots = chart.FindAll("circle");
        double cx0 = double.Parse(dots[0].GetAttribute("cx")!, CultureInfo.InvariantCulture);
        double cx1 = double.Parse(dots[1].GetAttribute("cx")!, CultureInfo.InvariantCulture);
        Assert.True(cx1 > cx0);   // x=100 is to the right of x=0
    }

    private static IReadOnlyList<ChartSeries> Series() =>
    [
        new ChartSeries("A", [10, 20]),
        new ChartSeries("B", [5, 5]),
    ];

    [Fact]
    public void Stacked_bar_renders_a_rect_per_series_per_category_with_legend()
    {
        IRenderedComponent<DxStackedBarChart> chart = RenderComponent<DxStackedBarChart>(parameters => parameters
            .Add(c => c.Categories, new[] { "Q1", "Q2" })
            .Add(c => c.Series, Series()));

        Assert.Equal(4, chart.FindAll("rect.dx-bar-rect").Count);   // 2 series x 2 categories
        Assert.Equal(2, chart.FindAll(".dx-pie-legend-item").Count);
        Assert.Contains("Stacked bar chart", chart.Markup);
    }

    [Fact]
    public void Tallest_stack_uses_the_full_height()
    {
        IRenderedComponent<DxStackedBarChart> chart = RenderComponent<DxStackedBarChart>(parameters => parameters
            .Add(c => c.Categories, new[] { "Q1", "Q2" })
            .Add(c => c.Series, Series())
            .Add(c => c.Height, 200));

        // Q2 total (20+5=25) is the max stack -> its segments fill more than Q1's (10+5=15).
        var rects = chart.FindAll("rect.dx-bar-rect");
        double q1Total = Height(rects[0]) + Height(rects[1]);
        double q2Total = Height(rects[2]) + Height(rects[3]);
        Assert.True(q2Total > q1Total);
    }

    [Fact]
    public void Grouped_mode_reports_grouped_in_its_label()
    {
        IRenderedComponent<DxStackedBarChart> chart = RenderComponent<DxStackedBarChart>(parameters => parameters
            .Add(c => c.Categories, new[] { "Q1" })
            .Add(c => c.Series, Series())
            .Add(c => c.Stacked, false));

        Assert.Contains("Grouped bar chart", chart.Markup);
        Assert.Equal(2, chart.FindAll("rect.dx-bar-rect").Count);   // 2 side-by-side bars in one category
    }

    private static double Height(AngleSharp.Dom.IElement rect) =>
        double.Parse(rect.GetAttribute("height")!, CultureInfo.InvariantCulture);
}
