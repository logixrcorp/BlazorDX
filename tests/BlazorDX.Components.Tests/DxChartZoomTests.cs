using System.Globalization;
using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Wheel/drag/keyboard zoom-pan for the two continuous-domain charts (line, area). Zoom/pan is a
/// progressive enhancement: with <c>Zoomable</c> false (the default), a chart must render exactly
/// as it did before this feature existed (role="img", no tabindex, static viewBox, no reset
/// button, no handlers) — covered per chart below, mirroring DxChartEventsTests's non-interactive
/// gating pattern for the discrete-mark charts.
/// </summary>
public sealed class DxChartZoomTests : TestContext
{
    public DxChartZoomTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
        Services.AddScoped<IChartZoomInterop, NullChartZoomInterop>();
    }

    private static IReadOnlyList<ChartPoint> Series(int count = 100) =>
        Enumerable.Range(0, count).Select(i => new ChartPoint(X: i, Y: Math.Sin(i / 10.0))).ToList();

    private static (double X0, double Width, double Height) ParseViewBox(string viewBox)
    {
        string[] parts = viewBox.Split(' ');
        return (
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    // ---- DxLineChart: non-interactive gating ----

    [Fact]
    public void Line_chart_stays_non_zoomable_by_default()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p.Add(c => c.Points, Series()));

        var svg = chart.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Null(svg.GetAttribute("tabindex"));
        Assert.Equal("0 0 640 220", svg.GetAttribute("viewBox"));
        Assert.DoesNotContain("dx-chart-zoomable", svg.GetAttribute("class"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    [Fact]
    public void Line_chart_becomes_zoomable_with_Zoomable_true()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        var svg = chart.Find("svg");
        Assert.Equal("application", svg.GetAttribute("role"));
        Assert.Equal("0", svg.GetAttribute("tabindex"));
        Assert.Contains("dx-chart-zoomable", svg.GetAttribute("class"));
    }

    // ---- Wheel (center-anchored, synchronous — no interop involved) ----

    [Fact]
    public void Wheel_zoom_in_then_out_narrows_then_restores_the_viewBox()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        (_, double fullWidth, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        (_, double narrowedWidth, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(narrowedWidth < fullWidth);

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = 100 });
        (double x0, double restoredWidth, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.Equal(fullWidth, restoredWidth, precision: 3);
        Assert.Equal(0, x0, precision: 3);
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset")); // back to unzoomed
    }

    [Fact]
    public void OnZoomChanged_is_raised_with_the_new_visible_range()
    {
        ChartZoomChangedEventArgs? raised = null;
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true)
            .Add(c => c.OnZoomChanged, e => raised = e));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });

        Assert.NotNull(raised);
        Assert.True(raised!.Value.IsZoomed);
        Assert.True(raised.Value.VisibleMax - raised.Value.VisibleMin < 99);
    }

    // ---- Drag-pan (needs a live width measurement — once per gesture, at pointerdown) ----

    private sealed class FakeChartZoomInterop(double width, double left = 0, double top = 0) : IChartZoomInterop
    {
        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

        public ValueTask<double> MeasureWidthAsync(string elementId) => ValueTask.FromResult(width);

        public ValueTask<(double Left, double Top)> MeasureOffsetAsync(string elementId) =>
            ValueTask.FromResult((left, top));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Dragging_pans_the_viewBox_while_zoomed_in()
    {
        Services.AddScoped<IChartZoomInterop>(_ => new FakeChartZoomInterop(640));
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        // Zoom in first — panning at full zoom-out has nowhere to go (both edges already touch).
        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        (double x0Before, _, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);

        Assert.Empty(chart.FindAll(".dx-chart-pan-overlay"));

        chart.Find("svg").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100 });
        Assert.Single(chart.FindAll(".dx-chart-pan-overlay"));

        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 170 });
        (double x0After, _, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.NotEqual(x0Before, x0After);

        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointerup", new PointerEventArgs { ClientX = 170 });
        Assert.Empty(chart.FindAll(".dx-chart-pan-overlay")); // gesture ends, overlay is gone

        // The pan persists after the gesture.
        (double x0Final, _, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.Equal(x0After, x0Final, precision: 3);
    }

    [Fact]
    public void Dragging_never_starts_when_the_width_measurement_is_unavailable()
    {
        // NullChartZoomInterop (registered in the constructor) reports width 0.
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100 });

        Assert.Empty(chart.FindAll(".dx-chart-pan-overlay")); // gesture silently doesn't start
    }

    // ---- Keyboard (arrows pan, +/-/Ctrl+Arrow zoom, Home/0 reset) ----

    [Fact]
    public void Keyboard_arrows_pan_after_zooming_in()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        (double x0Before, _, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowRight" });

        (double x0After, _, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(x0After > x0Before);
    }

    [Fact]
    public void Keyboard_plus_and_ctrl_arrow_up_both_zoom_in()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        (_, double fullWidth, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "+" });
        (_, double afterPlus, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(afterPlus < fullWidth);

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowUp", CtrlKey = true });
        (_, double afterCtrlUp, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(afterCtrlUp < afterPlus);
    }

    [Fact]
    public void Keyboard_home_resets_to_the_full_view()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "+" });
        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.NotEmpty(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Home" });

        Assert.Equal("0 0 640 220", chart.Find("svg").GetAttribute("viewBox"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    // ---- Reset button / double-click ----

    [Fact]
    public void Reset_button_appears_only_when_zoomed_and_clicking_it_restores_the_full_view()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        Assert.Single(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find(".dx-chart-zoom-reset").Click();

        Assert.Equal("0 0 640 220", chart.Find("svg").GetAttribute("viewBox"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    [Fact]
    public void Double_click_also_resets()
    {
        IRenderedComponent<DxLineChart> chart = RenderComponent<DxLineChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        Assert.NotEmpty(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find("svg").TriggerEvent("ondblclick", new MouseEventArgs());

        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    // ---- DxAreaChart: same wiring, lighter smoke coverage (shares the wiring, per the
    // discrete-mark charts' "shared wiring gets a smoke test, not full duplication" convention) ----

    [Fact]
    public void Area_chart_stays_non_zoomable_by_default()
    {
        IRenderedComponent<DxAreaChart> chart = RenderComponent<DxAreaChart>(p => p.Add(c => c.Points, Series()));

        var svg = chart.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Null(svg.GetAttribute("tabindex"));
        Assert.Equal("0 0 640 220", svg.GetAttribute("viewBox"));
    }

    [Fact]
    public void Area_chart_wheel_zoom_narrows_the_viewBox()
    {
        IRenderedComponent<DxAreaChart> chart = RenderComponent<DxAreaChart>(p => p
            .Add(c => c.Points, Series())
            .Add(c => c.Zoomable, true));

        (_, double fullWidth, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });

        (_, double narrowedWidth, _) = ParseViewBox(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(narrowedWidth < fullWidth);
        Assert.Equal("application", chart.Find("svg").GetAttribute("role"));
    }

    // ---- DxScatterChart: rectangular (both-axes) zoom/pan -- full coverage, since this is
    // the reference implementation for the both-axes gesture set (brush-zoom, shift-pan, the
    // interactive/zoomable keyboard fork). See docs/adr/0020-scatter-bubble-2d-zoom-strategy.md. ----

    // X domain [0,100], Y domain [0,50] -- round numbers so brush/pan math is exactly verifiable.
    private static IReadOnlyList<ChartPoint> RectPoints() =>
    [
        new ChartPoint(X: 0, Y: 0),
        new ChartPoint(X: 100, Y: 0),
        new ChartPoint(X: 0, Y: 50),
        new ChartPoint(X: 100, Y: 50),
        new ChartPoint(X: 50, Y: 25),
    ];

    private static (double X0, double Y0, double Width, double Height) ParseViewBox2D(string viewBox)
    {
        string[] parts = viewBox.Split(' ');
        return (
            double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture),
            double.Parse(parts[2], CultureInfo.InvariantCulture),
            double.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Scatter_chart_stays_non_zoomable_by_default()
    {
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p.Add(c => c.Points, RectPoints()));

        var svg = chart.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Null(svg.GetAttribute("tabindex"));
        Assert.Equal("0 0 640 280", svg.GetAttribute("viewBox"));
        Assert.DoesNotContain("dx-chart-zoomable", svg.GetAttribute("class"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-surface"));
    }

    [Fact]
    public void Scatter_chart_becomes_zoomable_with_Zoomable_true()
    {
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        var svg = chart.Find("svg");
        Assert.Equal("application", svg.GetAttribute("role"));
        Assert.Equal("0", svg.GetAttribute("tabindex"));
        Assert.Contains("dx-chart-zoomable", svg.GetAttribute("class"));
        Assert.Single(chart.FindAll(".dx-chart-zoom-surface"));
    }

    [Fact]
    public void Scatter_wheel_zoom_narrows_both_axes()
    {
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        (_, _, double fullW, double fullH) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });

        (_, _, double narrowedW, double narrowedH) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(narrowedW < fullW);
        Assert.True(narrowedH < fullH);
    }

    [Fact]
    public void Scatter_OnZoomChanged2D_is_raised_with_both_axes_ranges()
    {
        ChartZoomChanged2DEventArgs? raised = null;
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true)
            .Add(c => c.OnZoomChanged2D, e => raised = e));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });

        Assert.NotNull(raised);
        Assert.True(raised!.Value.IsZoomed);
        Assert.True(raised.Value.XVisibleMax - raised.Value.XVisibleMin < 100);
        Assert.True(raised.Value.YVisibleMax - raised.Value.YVisibleMin < 50);
    }

    [Fact]
    public void Brush_drag_zooms_to_the_dragged_rectangle()
    {
        Services.AddScoped<IChartZoomInterop>(_ => new FakeChartZoomInterop(640));
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        Assert.Empty(chart.FindAll(".dx-chart-brush"));

        chart.Find(".dx-chart-zoom-surface").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100, ClientY = 50 });
        Assert.Single(chart.FindAll(".dx-chart-pan-overlay"));

        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 300, ClientY = 150 });
        Assert.Single(chart.FindAll(".dx-chart-brush")); // live brush rect visible during the drag

        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointerup", new PointerEventArgs { ClientX = 300, ClientY = 150 });

        Assert.Empty(chart.FindAll(".dx-chart-pan-overlay"));
        Assert.Empty(chart.FindAll(".dx-chart-brush"));

        // Width=640/Height=280, X domain [0,100], Y domain [0,50] -- a drag from local
        // (100,50) to (300,150) converts to data X [15.625, 46.875], Y [23.214, 41.071],
        // which projects back to the exact viewBox "100 50 200 100".
        (double x0, double y0, double w, double h) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.Equal(100, x0, precision: 1);
        Assert.Equal(50, y0, precision: 1);
        Assert.Equal(200, w, precision: 1);
        Assert.Equal(100, h, precision: 1);
    }

    [Fact]
    public void A_near_zero_drag_does_not_zoom_treated_as_a_click()
    {
        Services.AddScoped<IChartZoomInterop>(_ => new FakeChartZoomInterop(640));
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        chart.Find(".dx-chart-zoom-surface").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100, ClientY = 50 });
        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 102, ClientY = 51 });
        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointerup", new PointerEventArgs { ClientX = 102, ClientY = 51 });

        Assert.Equal("0 0 640 280", chart.Find("svg").GetAttribute("viewBox"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    [Fact]
    public void Shift_drag_pans_after_zooming_in()
    {
        Services.AddScoped<IChartZoomInterop>(_ => new FakeChartZoomInterop(640));
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 }); // zoom in first
        (double x0Before, double y0Before, _, _) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find(".dx-chart-zoom-surface").TriggerEvent(
            "onpointerdown", new PointerEventArgs { ClientX = 100, ClientY = 100, ShiftKey = true });
        Assert.Single(chart.FindAll(".dx-chart-pan-overlay"));
        Assert.Empty(chart.FindAll(".dx-chart-brush")); // panning, not brushing

        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointermove", new PointerEventArgs { ClientX = 150, ClientY = 130 });
        (double x0After, double y0After, _, _) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        Assert.NotEqual(x0Before, x0After);
        Assert.NotEqual(y0Before, y0After);

        chart.Find(".dx-chart-pan-overlay").TriggerEvent("onpointerup", new PointerEventArgs());
        Assert.Empty(chart.FindAll(".dx-chart-pan-overlay"));
    }

    [Fact]
    public void Gesture_never_starts_when_the_width_measurement_is_unavailable()
    {
        // NullChartZoomInterop (registered in the constructor) reports width 0.
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        chart.Find(".dx-chart-zoom-surface").TriggerEvent("onpointerdown", new PointerEventArgs { ClientX = 100 });

        Assert.Empty(chart.FindAll(".dx-chart-pan-overlay"));
    }

    [Fact]
    public void Zooming_the_Y_axis_shows_the_correct_half_of_the_data()
    {
        // Panning toward higher Y (ArrowUp, non-interactive) must SHRINK the viewBox y0
        // (move it toward the top) -- getting the SVG Y-inversion backwards would show
        // the wrong half of the data.
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowUp", CtrlKey = true }); // zoom in
        (_, double y0Before, _, double hBefore) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowUp" }); // pan toward higher Y

        (_, double y0After, _, double hAfter) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        Assert.Equal(hBefore, hAfter, precision: 3); // span unchanged, just panned
        Assert.True(y0After < y0Before);
    }

    [Fact]
    public void Combined_interactive_and_zoomable_keeps_plain_arrows_for_selection_and_needs_a_modifier_for_zoom_pan()
    {
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true)
            .Add(c => c.OnPointSelected, _ => { }));

        // Plain ArrowRight still navigates/selects points -- unaffected by Zoomable.
        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.NotNull(chart.Find("svg").GetAttribute("aria-activedescendant"));
        (double x0Unzoomed, _, _, _) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.Equal(0, x0Unzoomed, precision: 3); // did not also pan

        // Zoom in via Ctrl+ArrowUp so there's room to pan.
        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowUp", CtrlKey = true });
        string? activeBefore = chart.Find("svg").GetAttribute("aria-activedescendant");
        (double x0Before, _, _, _) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        // Shift+ArrowRight pans X, and must NOT change the selected point.
        chart.Find("svg").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });

        string? activeAfter = chart.Find("svg").GetAttribute("aria-activedescendant");
        Assert.Equal(activeBefore, activeAfter);
        (double x0After, _, _, _) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(x0After > x0Before);
    }

    [Fact]
    public void Scatter_reset_button_appears_only_when_zoomed_and_clicking_it_restores_the_full_view()
    {
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        Assert.Single(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find(".dx-chart-zoom-reset").Click();

        Assert.Equal("0 0 640 280", chart.Find("svg").GetAttribute("viewBox"));
        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    [Fact]
    public void Scatter_double_click_also_resets()
    {
        IRenderedComponent<DxScatterChart> chart = RenderComponent<DxScatterChart>(p => p
            .Add(c => c.Points, RectPoints())
            .Add(c => c.Zoomable, true));

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });
        Assert.NotEmpty(chart.FindAll(".dx-chart-zoom-reset"));

        chart.Find("svg").TriggerEvent("ondblclick", new MouseEventArgs());

        Assert.Empty(chart.FindAll(".dx-chart-zoom-reset"));
    }

    // ---- DxBubbleChart: same wiring, lighter smoke coverage ----

    private static IReadOnlyList<ChartPoint> BubblePoints() =>
    [
        new ChartPoint(X: 0, Y: 0, Y2: 10),
        new ChartPoint(X: 100, Y: 50, Y2: 30),
    ];

    [Fact]
    public void Bubble_chart_stays_non_zoomable_by_default()
    {
        IRenderedComponent<DxBubbleChart> chart = RenderComponent<DxBubbleChart>(p => p.Add(c => c.Points, BubblePoints()));

        var svg = chart.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Null(svg.GetAttribute("tabindex"));
        Assert.Equal("0 0 640 320", svg.GetAttribute("viewBox"));
    }

    [Fact]
    public void Bubble_wheel_zoom_narrows_both_axes()
    {
        IRenderedComponent<DxBubbleChart> chart = RenderComponent<DxBubbleChart>(p => p
            .Add(c => c.Points, BubblePoints())
            .Add(c => c.Zoomable, true));

        (_, _, double fullW, double fullH) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });

        (_, _, double narrowedW, double narrowedH) = ParseViewBox2D(chart.Find("svg").GetAttribute("viewBox")!);
        Assert.True(narrowedW < fullW);
        Assert.True(narrowedH < fullH);
        Assert.Equal("application", chart.Find("svg").GetAttribute("role"));
    }

    [Fact]
    public void Bubble_radius_is_unaffected_by_zoom()
    {
        IRenderedComponent<DxBubbleChart> chart = RenderComponent<DxBubbleChart>(p => p
            .Add(c => c.Points, BubblePoints())
            .Add(c => c.Zoomable, true));

        string? rBefore = chart.FindAll("circle")[0].GetAttribute("r");

        chart.Find("svg").TriggerEvent("onwheel", new WheelEventArgs { DeltaY = -100 });

        string? rAfter = chart.FindAll("circle")[0].GetAttribute("r");
        Assert.Equal(rBefore, rAfter);
    }
}
