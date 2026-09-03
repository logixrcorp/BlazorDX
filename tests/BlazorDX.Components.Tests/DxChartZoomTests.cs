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

    private sealed class FakeChartZoomInterop(double width) : IChartZoomInterop
    {
        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

        public ValueTask<double> MeasureWidthAsync(string elementId) => ValueTask.FromResult(width);

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
}
