using System.Globalization;
using BlazorDX.Interop;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A scatter plot: each (x, y) point is drawn as a dot, scaled to the data's
/// bounds. Reuses the shared <see cref="ChartPoint"/> model (<see cref="ChartPoint.X"/> +
/// <see cref="ChartPoint.Y"/>). Pure SVG. Styling is token-driven (see dx-chart.css).
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks.
/// Rectangular (both-axes) zoom/pan is a separate progressive enhancement, opt in via
/// <see cref="Zoomable"/> — wheel to zoom (uniform on both axes, center-anchored),
/// drag to brush-zoom into a region, Shift+drag to pan, keyboard alternative. See
/// <c>docs/adr/0020-scatter-bubble-2d-zoom-strategy.md</c> for the design: unlike
/// <see cref="DxLineChart"/>/<see cref="DxAreaChart"/> (X-only, since Y always covers
/// the full dataset for those), a scatter plot's X and Y are both genuinely
/// continuous, so this crops the SVG <c>viewBox</c> on both axes via
/// <see cref="ChartRectZoomPrimitive"/>. When <see cref="Zoomable"/> and point
/// selection are both wired, plain arrow keys/Home/End keep navigating points as
/// before — zoom/pan then requires a modifier (Shift+Arrow pans, Ctrl+Arrow/+/-/Home
/// zooms/resets).
/// </remarks>
public sealed class DxScatterChart : ComponentBase
{
    private const double Pad = 10;
    private const double BrushThresholdPx = 6;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly ChartRectZoomPrimitive zoom = new();
    private readonly string chartId = $"dx-scatter-{Guid.NewGuid():N}";

    private bool isPanning;
    private bool isBrushing;
    private double gestureLeft, gestureTop, gestureWidth, gestureHeight;
    private double panStartClientX, panStartClientY, panAppliedDeltaX, panAppliedDeltaY;
    private double brushStartLocalX, brushStartLocalY, brushCurrentLocalX, brushCurrentLocalY;

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 280;

    [Parameter] public double Radius { get; set; } = 3.5;

    [Parameter] public string? Color { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    /// <summary>Opts into wheel-zoom, brush-drag-zoom, shift-drag-pan, and keyboard zoom/pan.
    /// Off by default — a chart doesn't start eating the page's mouse-wheel scroll gesture
    /// unless asked.</summary>
    [Parameter] public bool Zoomable { get; set; }

    /// <summary>Raised whenever the visible X or Y range changes from a zoom/pan gesture or a reset.</summary>
    [Parameter] public EventCallback<ChartZoomChanged2DEventArgs> OnZoomChanged2D { get; set; }

    [Inject] private IChartZoomInterop ChartZoomInterop { get; set; } = default!;

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    protected override void OnParametersSet()
    {
        selection.ClampTo(Points.Count);

        if (Points.Count > 0)
        {
            zoom.SetDomain(
                Points.Min(p => p.X), Points.Max(p => p.X),
                Points.Min(p => p.Y), Points.Max(p => p.Y));
        }
        else
        {
            zoom.SetDomain(0, 1, 0, 1);
        }
    }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxChartResources>? s;

    private DxStrings<DxChartResources> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        bool active = interactive || Zoomable;
        bool zoomed = zoom.IsZoomed;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "id", chartId);
        builder.AddAttribute(4, "class", Zoomable ? "dx-chart-svg dx-chart-zoomable" : "dx-chart-svg");
        builder.AddAttribute(5, "viewBox", ViewBox());
        builder.AddAttribute(6, "width", Width);
        builder.AddAttribute(7, "height", Height);
        builder.AddAttribute(8, "preserveAspectRatio", "none");
        builder.AddAttribute(9, "role", active ? "application" : "img");
        builder.AddAttribute(10, "aria-label",
            zoomed ? $"Scatter plot of {Points.Count} points, zoomed in" : $"Scatter plot of {Points.Count} points");

        if (active)
        {
            builder.AddAttribute(11, "tabindex", "0");
            if (interactive && selection.HasActive)
            {
                builder.AddAttribute(12, "aria-activedescendant", PointId(selection.ActiveIndex));
            }

            builder.AddAttribute(13, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(14, "onkeydown", true);
        }

        if (Zoomable)
        {
            builder.AddAttribute(15, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, OnWheel));
            builder.AddEventPreventDefaultAttribute(16, "onwheel", true);
            builder.AddAttribute(17, "ondblclick", EventCallback.Factory.Create(this, ResetAsync));

            builder.OpenElement(18, "rect");
            builder.AddAttribute(19, "class", "dx-chart-zoom-surface");
            builder.AddAttribute(20, "x", 0);
            builder.AddAttribute(21, "y", 0);
            builder.AddAttribute(22, "width", Width);
            builder.AddAttribute(23, "height", Height);
            builder.AddAttribute(24, "onpointerdown", EventCallback.Factory.Create<PointerEventArgs>(this, StartGestureAsync));
            builder.CloseElement();
        }

        if (Points.Count > 0)
        {
            BuildPoints(builder, interactive);
        }

        if (isBrushing)
        {
            (double x, double y, double w, double h) = BrushScreenRect();
            builder.OpenElement(25, "rect");
            builder.AddAttribute(26, "class", "dx-chart-brush");
            builder.AddAttribute(27, "x", F(x));
            builder.AddAttribute(28, "y", F(y));
            builder.AddAttribute(29, "width", F(w));
            builder.AddAttribute(30, "height", F(h));
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.CloseElement();

        if (isPanning || isBrushing)
        {
            // A full-window overlay so the gesture keeps tracking even if the cursor leaves the
            // (typically narrow) chart element — same technique as DxLineChart's drag-pan.
            builder.OpenElement(40, "div");
            builder.AddAttribute(41, "class", "dx-chart-pan-overlay");
            builder.AddAttribute(42, "onpointermove", EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerMoveAsync));
            builder.AddAttribute(43, "onpointerup", EventCallback.Factory.Create(this, EndGestureAsync));
            builder.AddAttribute(44, "onpointerleave", EventCallback.Factory.Create(this, EndGestureAsync));
            builder.CloseElement();
        }

        if (Zoomable)
        {
            builder.OpenElement(50, "div");
            builder.AddAttribute(51, "class", "dx-chart-caption");

            if (zoomed)
            {
                builder.OpenElement(52, "button");
                builder.AddAttribute(53, "type", "button");
                builder.AddAttribute(54, "class", "dx-chart-zoom-reset");
                builder.AddAttribute(55, "onclick", EventCallback.Factory.Create(this, ResetAsync));
                builder.AddContent(56, S["ResetZoom", "Reset zoom"]);
                builder.CloseElement();
            }

            builder.CloseElement();

            if (zoomed)
            {
                builder.OpenElement(57, "div");
                builder.AddAttribute(58, "class", "dx-chart-sr");
                builder.AddAttribute(59, "role", "status");
                builder.AddAttribute(60, "aria-live", "polite");
                builder.AddContent(61, S["ZoomStatusXY", "Zoomed to X {0}–{1}, Y {2}–{3}",
                    F(zoom.X.VisibleMin), F(zoom.X.VisibleMax), F(zoom.Y.VisibleMin), F(zoom.Y.VisibleMax)]);
                builder.CloseElement();
            }
        }
    }

    private void BuildPoints(RenderTreeBuilder builder, bool interactive)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint point = Points[i];
            double cx = ProjectX(point.X);
            double cy = ProjectY(point.Y);

            string css = "dx-scatter-dot dx-chart-drawin";
            if (interactive && selection.IsActive(i))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                css += " dx-chart-mark-hovered";
            }

            builder.OpenElement(70, "circle");
            builder.SetKey(i);
            builder.AddAttribute(71, "class", css);
            builder.AddAttribute(72, "cx", F(cx));
            builder.AddAttribute(73, "cy", F(cy));
            builder.AddAttribute(74, "r", F(Radius));
            builder.AddAttribute(174, "style", $"animation-delay:{i * 6}ms");
            if ((point.Color ?? Color) is { } fill)
            {
                builder.AddAttribute(75, "fill", fill);
            }

            if (interactive)
            {
                int captured = i;
                string label = $"({Num(point.X)}, {Num(point.Y)})";
                builder.AddAttribute(76, "id", PointId(i));
                builder.AddAttribute(77, "aria-label", label);
                builder.AddAttribute(78, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(79, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(80, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));

                builder.OpenElement(81, "title");
                builder.AddContent(82, label);
                builder.CloseElement();
            }

            builder.CloseElement();
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Projection (mirrors DxLineChart.ProjectX/ViewBoxX, extended to both axes) ----

    /// <summary>Projects onto the padded plotting area over the FULL data domain — unchanged
    /// by zoom. Point coordinates are always computed against the full domain; only the
    /// viewBox crops.</summary>
    private double ProjectX(double x) =>
        Pad + ((x - zoom.X.DataMin) / (zoom.X.DataMax - zoom.X.DataMin) * (Width - (2 * Pad)));

    private double ProjectY(double y) =>
        (Height - Pad) - ((y - zoom.Y.DataMin) / (zoom.Y.DataMax - zoom.Y.DataMin) * (Height - (2 * Pad)));

    /// <summary>Projects onto the unpadded full SVG canvas, [0, Width] over the full domain —
    /// used only for the viewBox crop, so full zoom-out reduces exactly to "0 0 Width Height".</summary>
    private double ViewBoxX(double x) => (x - zoom.X.DataMin) / (zoom.X.DataMax - zoom.X.DataMin) * Width;

    /// <summary>Same, for Y — inverted to match <see cref="ProjectY"/>'s top-down flip (SVG y
    /// grows downward; a larger data Y must land at a SMALLER viewBox y).</summary>
    private double ViewBoxY(double y) => Height - ((y - zoom.Y.DataMin) / (zoom.Y.DataMax - zoom.Y.DataMin) * Height);

    private string ViewBox()
    {
        double x0 = ViewBoxX(zoom.X.VisibleMin);
        double w = ViewBoxX(zoom.X.VisibleMax) - x0;
        double y0 = ViewBoxY(zoom.Y.VisibleMax); // the larger data Y is the TOP edge
        double h = ViewBoxY(zoom.Y.VisibleMin) - y0;
        return $"{F(x0)} {F(y0)} {F(w)} {F(h)}";
    }

    // ---- Zoom / pan interaction ----

    private Task OnWheel(WheelEventArgs e)
    {
        if (e.DeltaY < 0)
        {
            zoom.ZoomIn();
        }
        else
        {
            zoom.ZoomOut();
        }

        return RaiseZoomChangedAsync();
    }

    private async Task StartGestureAsync(PointerEventArgs e)
    {
        double width = await ChartZoomInterop.MeasureWidthAsync(chartId);
        if (width <= 0)
        {
            // Measurement unavailable (server/prerender, or the element isn't in the DOM yet) —
            // the gesture simply doesn't start rather than computing a garbage pixel ratio.
            return;
        }

        (double left, double top) = await ChartZoomInterop.MeasureOffsetAsync(chartId);
        gestureWidth = width;
        gestureHeight = width * (Height / (double)Width);
        gestureLeft = left;
        gestureTop = top;

        if (e.ShiftKey)
        {
            isPanning = true;
            panStartClientX = e.ClientX;
            panStartClientY = e.ClientY;
            panAppliedDeltaX = 0;
            panAppliedDeltaY = 0;
        }
        else
        {
            isBrushing = true;
            brushStartLocalX = e.ClientX - left;
            brushStartLocalY = e.ClientY - top;
            brushCurrentLocalX = brushStartLocalX;
            brushCurrentLocalY = brushStartLocalY;
        }

        StateHasChanged();
    }

    private Task OnPointerMoveAsync(PointerEventArgs e)
    {
        if (isPanning)
        {
            double totalPixelDeltaX = e.ClientX - panStartClientX;
            double totalPixelDeltaY = e.ClientY - panStartClientY;
            // X: dragging right reveals earlier data (direct-manipulation "grab and drag").
            double totalDataDeltaX = -(totalPixelDeltaX * (zoom.X.VisibleSpan / gestureWidth));
            // Y: screen-down is data-Y-down too here (unlike X, no extra negation needed —
            // see ViewBoxY's inversion, which already flips the relationship the other way).
            double totalDataDeltaY = totalPixelDeltaY * (zoom.Y.VisibleSpan / gestureHeight);
            zoom.X.PanBy(totalDataDeltaX - panAppliedDeltaX);
            zoom.Y.PanBy(totalDataDeltaY - panAppliedDeltaY);
            panAppliedDeltaX = totalDataDeltaX;
            panAppliedDeltaY = totalDataDeltaY;
            StateHasChanged();
            return RaiseZoomChangedAsync();
        }

        if (isBrushing)
        {
            brushCurrentLocalX = e.ClientX - gestureLeft;
            brushCurrentLocalY = e.ClientY - gestureTop;
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private Task EndGestureAsync()
    {
        if (isPanning)
        {
            isPanning = false;
            StateHasChanged();
            return Task.CompletedTask;
        }

        if (!isBrushing)
        {
            return Task.CompletedTask;
        }

        isBrushing = false;

        // A near-zero-width/height brush is a click, not a real drag -- no-op rather than
        // zooming to a degenerate box.
        if (Math.Abs(brushCurrentLocalX - brushStartLocalX) < BrushThresholdPx
            && Math.Abs(brushCurrentLocalY - brushStartLocalY) < BrushThresholdPx)
        {
            StateHasChanged();
            return Task.CompletedTask;
        }

        double dataX0 = LocalToDataX(brushStartLocalX);
        double dataX1 = LocalToDataX(brushCurrentLocalX);
        double dataY0 = LocalToDataY(brushStartLocalY);
        double dataY1 = LocalToDataY(brushCurrentLocalY);
        zoom.ZoomToBox(Math.Min(dataX0, dataX1), Math.Max(dataX0, dataX1), Math.Min(dataY0, dataY1), Math.Max(dataY0, dataY1));

        StateHasChanged();
        return RaiseZoomChangedAsync();
    }

    // Converts a local (element-relative) drag pixel position into data-space, using the
    // CURRENT visible window (gestureWidth/gestureHeight span exactly the current crop).
    private double LocalToDataX(double localX) =>
        zoom.X.VisibleMin + (localX * (zoom.X.VisibleSpan / gestureWidth));

    private double LocalToDataY(double localY) =>
        zoom.Y.VisibleMax - (localY * (zoom.Y.VisibleSpan / gestureHeight));

    // The live brush rectangle's on-screen (SVG viewBox coordinate space) bounds, computed
    // through the same ViewBoxX/ViewBoxY absolute coordinates points and the crop already use.
    private (double X, double Y, double W, double H) BrushScreenRect()
    {
        double sx0 = ViewBoxX(LocalToDataX(brushStartLocalX));
        double sx1 = ViewBoxX(LocalToDataX(brushCurrentLocalX));
        double sy0 = ViewBoxY(LocalToDataY(brushStartLocalY));
        double sy1 = ViewBoxY(LocalToDataY(brushCurrentLocalY));

        double x = Math.Min(sx0, sx1);
        double y = Math.Min(sy0, sy1);
        return (x, y, Math.Abs(sx1 - sx0), Math.Abs(sy1 - sy0));
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (Interactive)
        {
            // Modifier-gated zoom/pan takes priority so a modified arrow isn't ALSO
            // treated as a (modifier-blind) selection move by ChartSelectionPrimitive.
            if (Zoomable && (args.ShiftKey || args.CtrlKey) && await TryHandleZoomKeyModifiedAsync(args))
            {
                return;
            }

            if (selection.MoveActive(args.Key, Points.Count))
            {
                StateHasChanged();
                return;
            }

            if ((args.Key is "Enter" or " ") && selection.HasActive)
            {
                await SelectAsync(selection.ActiveIndex);
            }

            return;
        }

        if (Zoomable)
        {
            await HandleZoomKeyPlainAsync(args);
        }
    }

    // Not interactive: plain arrows pan (Left/Right = X, Up/Down = Y), +/-/Ctrl+ArrowUp/Down
    // zoom, Home/0 reset -- the DxLineChart scheme extended to two axes.
    private Task HandleZoomKeyPlainAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowLeft": zoom.PanByFraction(-0.1, 0); break;
            case "ArrowRight": zoom.PanByFraction(0.1, 0); break;
            case "ArrowUp" when e.CtrlKey: zoom.ZoomIn(); break;
            case "ArrowUp": zoom.PanByFraction(0, 0.1); break;
            case "ArrowDown" when e.CtrlKey: zoom.ZoomOut(); break;
            case "ArrowDown": zoom.PanByFraction(0, -0.1); break;
            case "+" or "=": zoom.ZoomIn(); break;
            case "-" or "_": zoom.ZoomOut(); break;
            case "Home" or "0": zoom.Reset(); break;
            default: return Task.CompletedTask;
        }

        return RaiseZoomChangedAsync();
    }

    // Interactive: plain arrows/Home/End are reserved for selection.MoveActive (handled by
    // the caller). Zoom/pan needs a modifier: Shift+Arrow pans, Ctrl+ArrowUp/Down (or
    // Ctrl+/-) zooms, Ctrl+Home resets. Returns whether the key was handled.
    private async Task<bool> TryHandleZoomKeyModifiedAsync(KeyboardEventArgs e)
    {
        bool handled = true;

        if (e.ShiftKey)
        {
            switch (e.Key)
            {
                case "ArrowLeft": zoom.PanByFraction(-0.1, 0); break;
                case "ArrowRight": zoom.PanByFraction(0.1, 0); break;
                case "ArrowUp": zoom.PanByFraction(0, 0.1); break;
                case "ArrowDown": zoom.PanByFraction(0, -0.1); break;
                default: handled = false; break;
            }
        }
        else if (e.CtrlKey)
        {
            switch (e.Key)
            {
                case "ArrowUp" or "+" or "=": zoom.ZoomIn(); break;
                case "ArrowDown" or "-" or "_": zoom.ZoomOut(); break;
                case "Home" or "0": zoom.Reset(); break;
                default: handled = false; break;
            }
        }
        else
        {
            handled = false;
        }

        if (handled)
        {
            await RaiseZoomChangedAsync();
        }

        return handled;
    }

    private Task ResetAsync()
    {
        zoom.Reset();
        return RaiseZoomChangedAsync();
    }

    private Task RaiseZoomChangedAsync() =>
        OnZoomChanged2D.HasDelegate
            ? OnZoomChanged2D.InvokeAsync(new ChartZoomChanged2DEventArgs(
                zoom.X.VisibleMin, zoom.X.VisibleMax, zoom.Y.VisibleMin, zoom.Y.VisibleMax, zoom.IsZoomed))
            : Task.CompletedTask;

    // ---- Selection / hover (unchanged) ----

    private string PointId(int index) => $"{chartId}-p{index}";

    private Task SelectAsync(int index)
    {
        selection.SetActive(index, Points.Count);
        return OnPointSelected.HasDelegate
            ? OnPointSelected.InvokeAsync(new ChartPointEventArgs(index, Points[index]))
            : Task.CompletedTask;
    }

    private Task HoverAsync(int index)
    {
        selection.SetHovered(index);
        ChartPoint point = index >= 0 && index < Points.Count ? Points[index] : default;
        return OnPointHovered.HasDelegate
            ? OnPointHovered.InvokeAsync(new ChartPointEventArgs(index, point))
            : Task.CompletedTask;
    }
}
