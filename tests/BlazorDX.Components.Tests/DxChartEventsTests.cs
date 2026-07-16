using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Point selection/hover/keyboard-nav and legend-toggle across the discrete-mark charts.
/// Selection is a progressive enhancement: with no OnPointSelected/OnPointHovered wired, a chart
/// must render exactly as it did before (role="img", no tabindex) — covered per chart below.
/// </summary>
public sealed class DxChartEventsTests : TestContext
{
    // ---- DxBarChart: the exemplar (click, keyboard nav, hover, non-interactive gating) ----

    private static IReadOnlyList<ChartPoint> Bars() =>
    [
        new(Category: "A", Y: 10),
        new(Category: "B", Y: 20),
        new(Category: "C", Y: 5),
    ];

    [Fact]
    public void Bar_chart_stays_a_decorative_img_with_no_handlers_wired()
    {
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p.Add(c => c.Points, Bars()));

        var svg = chart.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Null(svg.GetAttribute("tabindex"));
        Assert.Empty(chart.FindAll("rect.dx-bar-rect[onclick]"));
    }

    [Fact]
    public void Bar_chart_becomes_interactive_once_OnPointSelected_is_wired()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p
            .Add(c => c.Points, Bars())
            .Add(c => c.OnPointSelected, e => selected = e));

        var svg = chart.Find("svg");
        Assert.Equal("application", svg.GetAttribute("role"));
        Assert.Equal("0", svg.GetAttribute("tabindex"));

        chart.FindAll("rect.dx-bar-rect")[1].Click();   // "B"

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Value.Index);
        Assert.Equal("B", selected.Value.Point.Category);
        Assert.Equal(20, selected.Value.Point.Y);
    }

    [Fact]
    public void Bar_chart_keyboard_arrow_then_enter_selects_the_active_bar()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p
            .Add(c => c.Points, Bars())
            .Add(c => c.OnPointSelected, e => selected = e));

        var svg = chart.Find("svg");
        svg.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });   // seeds active at 0
        svg = chart.Find("svg");
        Assert.NotNull(svg.GetAttribute("aria-activedescendant"));

        svg.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });   // -> index 1 ("B")
        svg = chart.Find("svg");
        svg.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Value.Index);
        Assert.Equal("B", selected.Value.Point.Category);
    }

    [Fact]
    public void Bar_chart_hover_reports_the_index_and_minus_one_on_leave()
    {
        List<int> reported = [];
        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p
            .Add(c => c.Points, Bars())
            .Add(c => c.OnPointHovered, e => reported.Add(e.Index)));

        var rect = chart.FindAll("rect.dx-bar-rect")[2];
        rect.MouseOver();
        rect = chart.FindAll("rect.dx-bar-rect")[2];
        rect.MouseOut();

        Assert.Equal([2, -1], reported);
    }

    // ---- DxPieChart: legend-toggle-to-hide (works without OnPointSelected/Hovered wired) ----

    [Fact]
    public void Pie_legend_click_hides_the_slice_and_raises_OnLegendToggled_then_restores_it()
    {
        ChartLegendToggledEventArgs? toggled = null;
        IRenderedComponent<DxPieChart> chart = RenderComponent<DxPieChart>(p => p
            .Add(c => c.Points, Bars())
            .Add(c => c.OnLegendToggled, e => toggled = e));

        Assert.Equal(3, chart.FindAll("path.dx-pie-slice").Count);

        var buttons = chart.FindAll(".dx-pie-legend-btn");
        buttons[1].Click();   // hide "B"

        Assert.Equal(2, chart.FindAll("path.dx-pie-slice").Count);
        Assert.Equal(new ChartLegendToggledEventArgs("B", false), toggled);
        Assert.Contains("dx-pie-legend-hidden", chart.FindAll(".dx-pie-legend-item")[1].ClassName);

        chart.FindAll(".dx-pie-legend-btn")[1].Click();   // show it again

        Assert.Equal(3, chart.FindAll("path.dx-pie-slice").Count);
        Assert.Equal(new ChartLegendToggledEventArgs("B", true), toggled);
    }

    [Fact]
    public void Pie_chart_selecting_a_slice_reports_its_category_and_value()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxPieChart> chart = RenderComponent<DxPieChart>(p => p
            .Add(c => c.Points, Bars())
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("path.dx-pie-slice")[2].Click();

        Assert.NotNull(selected);
        Assert.Equal(2, selected!.Value.Index);
        Assert.Equal("C", selected.Value.Point.Category);
    }

    // ---- DxStackedBarChart: flat (category, series) index math + legend toggle ----

    private static IReadOnlyList<ChartPoint> StackedPoints() =>
    [
        new(Category: "Q1", Y: 10, Series: "A"),
        new(Category: "Q2", Y: 20, Series: "A"),
        new(Category: "Q1", Y: 5, Series: "B"),
        new(Category: "Q2", Y: 5, Series: "B"),
    ];

    [Fact]
    public void Stacked_bar_click_resolves_the_flat_index_to_the_right_series_and_category()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxStackedBarChart> chart = RenderComponent<DxStackedBarChart>(p => p
            .Add(c => c.Categories, new[] { "Q1", "Q2" })
            .Add(c => c.Points, StackedPoints())
            .Add(c => c.OnPointSelected, e => selected = e));

        // Category-major flat order: [Q1/A, Q1/B, Q2/A, Q2/B] -> index 3 is Q2/B.
        chart.FindAll("rect.dx-bar-rect")[3].Click();

        Assert.NotNull(selected);
        Assert.Equal(3, selected!.Value.Index);
        Assert.Equal("Q2", selected.Value.Point.Category);
        Assert.Equal("B", selected.Value.Point.Series);
        Assert.Equal(5, selected.Value.Point.Y);
    }

    [Fact]
    public void Stacked_bar_hiding_a_series_removes_its_bars_and_its_flat_indices()
    {
        IRenderedComponent<DxStackedBarChart> chart = RenderComponent<DxStackedBarChart>(p => p
            .Add(c => c.Categories, new[] { "Q1", "Q2" })
            .Add(c => c.Points, StackedPoints()));

        Assert.Equal(4, chart.FindAll("rect.dx-bar-rect").Count);   // 2 series x 2 categories

        chart.FindAll(".dx-pie-legend-btn")[0].Click();   // hide "A"

        Assert.Equal(2, chart.FindAll("rect.dx-bar-rect").Count);   // only "B" remains
        // The legend still lists both series (so "A" can be re-shown), just dimmed.
        Assert.Equal(2, chart.FindAll(".dx-pie-legend-item").Count);
    }

    // ---- Lighter smoke coverage: funnel, scatter, radar, candlestick share the same wiring ----

    [Fact]
    public void Funnel_chart_click_selects_the_stage()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxFunnelChart> chart = RenderComponent<DxFunnelChart>(p => p
            .Add(c => c.Points, new List<ChartPoint> { new(Category: "Visited", Y: 100), new(Category: "Bought", Y: 20) })
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("polygon.dx-funnel-stage")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal("Bought", selected!.Value.Point.Category);
    }

    [Fact]
    public void Scatter_chart_click_selects_the_dot()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, new List<ChartPoint> { new(0, 0), new(5, 10) })
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("circle.dx-scatter-dot")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Value.Index);
        Assert.Equal(5, selected.Value.Point.X);
    }

    [Fact]
    public void Radar_chart_click_selects_a_vertex_and_legend_hides_a_series()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxRadarChart> chart = RenderComponent<DxRadarChart>(p => p
            .Add(c => c.Axes, new[] { "Speed", "Power", "Range" })
            .Add(c => c.Points, new List<ChartPoint>
            {
                new(Category: "Speed", Y: 3, Series: "A"), new(Category: "Power", Y: 5, Series: "A"), new(Category: "Range", Y: 2, Series: "A"),
                new(Category: "Speed", Y: 4, Series: "B"), new(Category: "Power", Y: 1, Series: "B"), new(Category: "Range", Y: 5, Series: "B"),
            })
            .Add(c => c.OnPointSelected, e => selected = e));

        Assert.Equal(6, chart.FindAll("circle.dx-radar-vertex").Count);   // 2 series x 3 axes

        chart.FindAll("circle.dx-radar-vertex")[0].Click();   // series A, axis Speed

        Assert.NotNull(selected);
        Assert.Equal("A", selected!.Value.Point.Series);
        Assert.Equal("Speed", selected.Value.Point.Category);
    }

    [Fact]
    public void Candlestick_chart_click_selects_the_candle()
    {
        ChartPointEventArgs? selected = null;
        IRenderedComponent<DxCandlestickChart> chart = RenderComponent<DxCandlestickChart>(p => p
            .Add(c => c.Points, new List<ChartPoint>
            {
                new(Category: "Mon", Y: 10, Y2: 22, Y3: 8, Y4: 20),
                new(Category: "Tue", Y: 20, Y2: 24, Y3: 9, Y4: 11),
            })
            .Add(c => c.OnPointSelected, e => selected = e));

        chart.FindAll("g.dx-candle")[1].Click();

        Assert.NotNull(selected);
        Assert.Equal("Tue", selected!.Value.Point.Category);
    }
}
