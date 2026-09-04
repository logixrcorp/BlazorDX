using System.Globalization;
using BlazorDX.Interop;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A bubble chart: a scatter plot with a third dimension encoded as dot radius. Reuses the shared
/// <see cref="ChartPoint"/> model — <see cref="ChartPoint.X"/> + <see cref="ChartPoint.Y"/> place
/// the bubble, and <see cref="ChartPoint.Y2"/> (when set) sizes it, linearly mapped across the
/// series' own min/max into <see cref="MinRadius"/>..<see cref="MaxRadius"/>. A point with no
/// <see cref="ChartPoint.Y2"/> draws at <see cref="MinRadius"/>. Pure SVG; styling via dx-chart.css.
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks. Rectangular
/// (both-axes) zoom/pan is a separate progressive enhancement — see <see cref="DxScatterChart"/>'s
/// remarks for the full design (this chart follows the identical pattern). Bubble radius is always
/// computed from the full, unfiltered <see cref="Points"/> list and is never affected by zoom —
/// only a bubble's center position, and which bubbles fall inside the cropped view, change.
/// </remarks>
public sealed class DxBubbleChart : ComponentBase
{
    private const double Pad = 14;
    private const double BrushThresholdPx = 6;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly ChartRectZoomPrimitive zoom = new();
    private readonly string chartId = $"dx-bubble-{Guid.NewGuid():N}";

    private bool isPanning;
    private bool isBrushing;
    private double gestureLeft, gestureTop, gestureWidth, gestureHeight;
    private double panStartClientX, panStartClientY, panAppliedDeltaX, panAppliedDeltaY;
    private double brushStartLocalX, brushStartLocalY, brushCurrentLocalX, brushCurrentLocalY;

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 320;

    [Parameter] public double MinRadius { get; set; } = 6;

    [Parameter] public double MaxRadius { get; set; } = 32;

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

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

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
            zoomed ? $"Bubble chart of {Points.Count} points, zoomed in" : $"Bubble chart of {Points.Count} points");

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
                builder.AddContent(56, "Reset zoom");
                builder.CloseElement();
            }

            builder.CloseElement();

            if (zoomed)
            {
                builder.OpenElement(57, "div");
                builder.AddAttribute(58, "class", "dx-chart-sr");
                builder.AddAttribute(59, "role", "status");
                builder.AddAttribute(60, "aria-live", "polite");
                builder.AddContent(61,
                    $"Zoomed to X {F(zoom.X.VisibleMin)}–{F(zoom.X.VisibleMax)}, Y {F(zoom.Y.VisibleMin)}–{F(zoom.Y.VisibleMax)}");
                builder.CloseElement();
            }
        }
    }

    private void BuildPoints(RenderTreeBuilder builder, bool interactive)
    {
        // Radius is always computed from the FULL, unfiltered point list -- a third,
        // independent data dimension that zoom must never affect.
        double minSize = Points.Min(p => p.Y2 ?? MinRadius);
        double maxSize = Points.Max(p => p.Y2 ?? MinRadius);
        double spanSize = maxSize - minSize == 0 ? 1 : maxSize - minSize;

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint point = Points[i];
            double cx = ProjectX(point.X);
            double cy = ProjectY(point.Y);
            double size = point.Y2 ?? minSize;
            double radius = MinRadius + ((size - minSize) / spanSize * (MaxRadius - MinRadius));

            string css = "dx-bubble-dot dx-chart-drawin";
            if (interactive && selection.IsActive(i))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                css += " dx-chart-mark-hovered";
            }

            string color = point.Color ?? Palette[i % Palette.Length];
            string label = point.Y2 is { } sz
                ? $"{point.Category ?? $"({Num(point.X)}, {Num(point.Y)})"}: size {Num(sz)}"
                : point.Category ?? $"({Num(point.X)}, {Num(point.Y)})";

            builder.OpenElement(70, "circle");
            builder.SetKey(i);
            builder.AddAttribute(71, "class", css);
            builder.AddAttribute(72, "cx", F(cx));
            builder.AddAttribute(73, "cy", F(cy));
            builder.AddAttribute(74, "r", F(radius));
            builder.AddAttribute(75, "fill", color);
            builder.AddAttribute(76, "style", $"animation-delay:{i * 10}ms");

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(77, "id", PointId(i));
                builder.AddAttribute(78, "aria-label", label);
                builder.AddAttribute(79, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(80, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(81, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            }

            builder.OpenElement(82, "title");
            builder.AddContent(83, label);
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Projection (mirrors DxScatterChart, using the Pad+MaxRadius margin this chart
    // already reserves so bubbles don't clip the plot edge) ----

    private double ProjectX(double x)
    {
        double area = Math.Max(1, Math.Min(Width, Height) - (2 * Pad) - (2 * MaxRadius));
        return Pad + MaxRadius + ((x - zoom.X.DataMin) / (zoom.X.DataMax - zoom.X.DataMin) * area);
    }

    private double ProjectY(double y)
    {
        double area = Math.Max(1, Math.Min(Width, Height) - (2 * Pad) - (2 * MaxRadius));
        return (Height - Pad - MaxRadius) - ((y - zoom.Y.DataMin) / (zoom.Y.DataMax - zoom.Y.DataMin) * area);
    }

    private double ViewBoxX(double x) => (x - zoom.X.DataMin) / (zoom.X.DataMax - zoom.X.DataMin) * Width;

    private double ViewBoxY(double y) => Height - ((y - zoom.Y.DataMin) / (zoom.Y.DataMax - zoom.Y.DataMin) * Height);

    private string ViewBox()
    {
        double x0 = ViewBoxX(zoom.X.VisibleMin);
        double w = ViewBoxX(zoom.X.VisibleMax) - x0;
        double y0 = ViewBoxY(zoom.Y.VisibleMax);
        double h = ViewBoxY(zoom.Y.VisibleMin) - y0;
        return $"{F(x0)} {F(y0)} {F(w)} {F(h)}";
    }

    // ---- Zoom / pan interaction (identical to DxScatterChart) ----

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
            double totalDataDeltaX = -(totalPixelDeltaX * (zoom.X.VisibleSpan / gestureWidth));
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

    private double LocalToDataX(double localX) =>
        zoom.X.VisibleMin + (localX * (zoom.X.VisibleSpan / gestureWidth));

    private double LocalToDataY(double localY) =>
        zoom.Y.VisibleMax - (localY * (zoom.Y.VisibleSpan / gestureHeight));

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
