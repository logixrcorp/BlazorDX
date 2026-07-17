using System.Globalization;
using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The four July "Graphs" Tier-1 chart types: Waterfall, Bubble, Heatmap, Bullet. Each follows the
/// same progressive-enhancement selection contract as the original 10 (see DxChartEventsTests) —
/// these tests focus on what's actually new: each chart's own data semantics.
/// </summary>
public sealed class DxTier1ChartsTests : TestContext
{
    // ---- Waterfall: running-total math + an absolute "total" bar resets it ----

    [Fact]
    public void Waterfall_stays_a_decorative_img_with_no_handlers_wired()
    {
        IRenderedComponent<DxWaterfallChart> chart = RenderComponent<DxWaterfallChart>(p => p
            .Add(c => c.Points, new List<ChartPoint> { new(Category: "Start", Y2: 100), new(Category: "Add", Y: 20) }));

        Assert.Equal("img", chart.Find("svg").GetAttribute("role"));
        Assert.Empty(chart.FindAll("rect.dx-waterfall-rect[onclick]"));
    }

    [Fact]
    public void Waterfall_accumulates_deltas_and_a_total_bar_resets_the_running_total()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxWaterfallChart> chart = RenderComponent<DxWaterfallChart>(p => p
            .Add(c => c.Points, new List<ChartPoint>
            {
                new(Category: "Start", Y2: 100),     // total bar: 0 -> 100
                new(Category: "Gain", Y: 30),          // delta: 100 -> 130
                new(Category: "Loss", Y: -50),         // delta: 130 -> 80
                new(Category: "Reset", Y2: 200),       // total bar: 0 -> 200 (running total := 200)
            })
            .Add(c => c.OnPointSelected, e => selected = e));

        Assert.Equal(4, chart.FindAll("rect.dx-waterfall-rect").Count);
        Assert.Equal(3, chart.FindAll("line.dx-waterfall-connector").Count);   // one between each adjacent pair

        chart.FindAll("rect.dx-waterfall-rect")[3].Click();
        Assert.NotNull(selected);
        Assert.Equal("Reset", selected!.Value.Point.Category);
        Assert.Equal(200, selected.Value.Point.Y2);
    }

    // ---- Bubble: Y2 sizes the dot, scaled into [MinRadius, MaxRadius] ----

    [Fact]
    public void Bubble_scales_radius_from_Y2_and_defaults_to_MinRadius_when_unset()
    {
        IRenderedComponent<DxBubbleChart> chart = RenderComponent<DxBubbleChart>(p => p
            .Add(c => c.Points, new List<ChartPoint> { new(0, 0, Y2: 0), new(1, 1, Y2: 100), new(2, 2) })
            .Add(c => c.MinRadius, 5)
            .Add(c => c.MaxRadius, 30));

        var circles = chart.FindAll("circle.dx-bubble-dot");
        Assert.Equal(3, circles.Count);
        double R(int i) => double.Parse(circles[i].GetAttribute("r")!, CultureInfo.InvariantCulture);

        Assert.Equal(5, R(0), 3);      // Y2 = 0 -> min of the observed size range
        Assert.Equal(30, R(1), 3);     // Y2 = 100 -> max of the observed size range
        Assert.Equal(5, R(2), 3);      // no Y2 -> falls back to MinRadius
    }

    [Fact]
    public void Bubble_click_selects_the_point()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxBubbleChart> chart = RenderComponent<DxBubbleChart>(p => p
            .Add(c => c.Points, new List<ChartPoint> { new(0, 0), new(5, 10, Y2: 40) })
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("circle.dx-bubble-dot")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Value.Index);
        Assert.Equal(40, selected.Value.Point.Y2);
    }

    // ---- Heatmap: Series = row, Category = column, Y = value ----

    [Fact]
    public void Heatmap_renders_one_cell_per_point_and_labels_distinct_rows_and_columns()
    {
        IRenderedComponent<DxHeatmap> chart = RenderComponent<DxHeatmap>(p => p
            .Add(c => c.Points, new List<ChartPoint>
            {
                new(Category: "Mon", Series: "Team A", Y: 3),
                new(Category: "Tue", Series: "Team A", Y: 7),
                new(Category: "Mon", Series: "Team B", Y: 1),
            }));

        Assert.Equal(3, chart.FindAll("rect.dx-heatmap-cell").Count);
        Assert.Equal(2, chart.FindAll("text.dx-heatmap-label").Count(t => t.GetAttribute("text-anchor") == "start"));   // 2 rows
    }

    [Fact]
    public void Heatmap_click_selects_the_cell_by_Points_index()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxHeatmap> chart = RenderComponent<DxHeatmap>(p => p
            .Add(c => c.Points, new List<ChartPoint>
            {
                new(Category: "Mon", Series: "A", Y: 3),
                new(Category: "Tue", Series: "A", Y: 7),
            })
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("rect.dx-heatmap-cell")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Value.Index);
        Assert.Equal("Tue", selected.Value.Point.Category);
    }

    // ---- Bullet: measure bar width tracks Value/Max, target tick tracks Target/Max ----

    [Fact]
    public void Bullet_renders_one_row_per_point_with_a_bar_and_a_target_tick()
    {
        IRenderedComponent<DxBulletChart> chart = RenderComponent<DxBulletChart>(p => p
            .Add(c => c.Points, new List<BulletPoint>
            {
                new("Revenue", Value: 80, Target: 90, Max: 100),
                new("Signups", Value: 40, Target: 60, Max: 100, Ranges: [30, 70]),
            }));

        Assert.Equal(2, chart.FindAll("rect.dx-bullet-bar").Count);
        Assert.Equal(2, chart.FindAll("line.dx-bullet-target").Count);
        // Row 0 has no Ranges -> one plain track; row 1 has 2 thresholds -> 3 range bands.
        Assert.Single(chart.FindAll("rect.dx-bullet-track"));
        Assert.Equal(3, chart.FindAll("rect.dx-bullet-range-1, rect.dx-bullet-range-2").Count);
    }

    private static List<BulletPoint> BulletRows() =>
    [
        new("Revenue", Value: 80, Target: 90, Max: 100),
        new("Signups", Value: 40, Target: 60, Max: 100),
    ];

    [Fact]
    public void Bullet_row_click_selects_it()
    {
        BulletPointEventArgs? selected = null;
        IRenderedComponent<DxBulletChart> chart = RenderComponent<DxBulletChart>(p => p
            .Add(c => c.Points, BulletRows())
            .Add(c => c.OnPointSelected, e => selected = e));

        Assert.Equal("application", chart.Find("svg").GetAttribute("role"));

        chart.FindAll("g.dx-bullet-row")[1].Click();
        Assert.NotNull(selected);
        Assert.Equal("Signups", selected!.Value.Point.Label);
    }

    [Fact]
    public void Bullet_keyboard_arrow_seeds_the_first_row_then_enter_selects_it()
    {
        BulletPointEventArgs? selected = null;
        IRenderedComponent<DxBulletChart> chart = RenderComponent<DxBulletChart>(p => p
            .Add(c => c.Points, BulletRows())
            .Add(c => c.OnPointSelected, e => selected = e));

        var svg = chart.Find("svg");
        svg.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });   // seeds active at 0
        svg = chart.Find("svg");
        svg.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.NotNull(selected);
        Assert.Equal("Revenue", selected!.Value.Point.Label);
    }
}
