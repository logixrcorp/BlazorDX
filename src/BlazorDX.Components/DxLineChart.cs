using System.Globalization;
using System.Text;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A line chart that renders a large (x, y) series as a single SVG polyline,
/// LTTB-downsampled by the compute backend (Rust in the browser) to keep the
/// visual shape while drawing only a few hundred points. Styling is CSS-variable
/// driven (see dx-layout.css).
/// </summary>
/// <remarks>
/// Zoom/pan is a progressive enhancement, opt in via <see cref="Zoomable"/> — wheel to zoom
/// (center-anchored), drag to pan, keyboard alternative (arrows pan, +/-/Ctrl+ArrowUp/Down zoom,
/// Home/0 reset). See <c>docs/adr/0017-chart-zoom-pan-strategy.md</c> for the design: it re-crops
/// the SVG <c>viewBox</c> rather than reprojecting points (this chart already sets
/// <c>preserveAspectRatio="none"</c> and <c>vector-effect="non-scaling-stroke"</c>, which is
/// exactly what a viewBox crop needs), never re-downsamples past the initial LTTB pass, and only
/// zooms the X axis — Y always covers the full dataset's range. With <see cref="Zoomable"/> false
/// (the default), rendering is byte-for-byte identical to before this feature existed.
/// </remarks>
public sealed class DxLineChart : ComponentBase
{
    private const double Padding = 8;

    private readonly ChartZoomPrimitive zoom = new();
    private readonly string chartId = $"dx-line-{Guid.NewGuid():N}";

    private double[] xValues = [];
    private double[] yValues = [];
    private int[] selected = [];
    private object? lastSeries;

    private bool isPanning;
    private double panStartClientX;
    private double panStartMeasuredWidth;
    private double panAppliedDelta;

    /// <summary>The series to plot, reading <see cref="ChartPoint.X"/> + <see cref="ChartPoint.Y"/>.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>Approximate number of points to draw.</summary>
    [Parameter] public int Threshold { get; set; } = 300;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 220;

    [Parameter] public string? Class { get; set; }

    /// <summary>Opts into wheel-zoom, drag-pan, and keyboard zoom/pan. Off by default — a chart
    /// doesn't start eating the page's mouse-wheel scroll gesture unless asked.</summary>
    [Parameter] public bool Zoomable { get; set; }

    /// <summary>Raised whenever the visible X range changes from a zoom/pan gesture or a reset.</summary>
    [Parameter] public EventCallback<ChartZoomChangedEventArgs> OnZoomChanged { get; set; }

    [Inject] private IGridCompute Compute { get; set; } = default!;

    [Inject] private IChartZoomInterop ChartZoomInterop { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        // Re-downsample only when the series changes (keyed on Points's identity).
        if (!ReferenceEquals(lastSeries, Points))
        {
            lastSeries = Points;
            xValues = new double[Points.Count];
            yValues = new double[Points.Count];
            for (int i = 0; i < Points.Count; i++)
            {
                xValues[i] = Points[i].X;
                yValues[i] = Points[i].Y;
            }

            selected = xValues.Length > 0
                ? await Compute.DownsampleAsync(xValues, yValues, Threshold)
                : [];

            zoom.SetDomain(
                xValues.Length > 0 ? xValues.Min() : 0,
                xValues.Length > 0 ? xValues.Max() : 0);

            StateHasChanged();
        }
    }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxChartResources>? s;

    private DxStrings<DxChartResources> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
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
        builder.AddAttribute(9, "role", Zoomable ? "application" : "img");
        builder.AddAttribute(10, "aria-label",
            zoomed
                ? $"Line chart of {selected.Length} downsampled points, zoomed in"
                : $"Line chart of {selected.Length} downsampled points");

        if (Zoomable)
        {
            builder.AddAttribute(11, "tabindex", "0");
            builder.AddAttribute(12, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, OnWheel));
            builder.AddEventPreventDefaultAttribute(13, "onwheel", true);
            builder.AddAttribute(14, "onpointerdown",
                EventCallback.Factory.Create<PointerEventArgs>(this, StartPanAsync));
            builder.AddAttribute(15, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(16, "onkeydown", true);
            builder.AddAttribute(17, "ondblclick", EventCallback.Factory.Create(this, ResetAsync));
        }

        builder.OpenElement(20, "polyline");
        builder.AddAttribute(21, "class", "dx-chart-line dx-chart-reveal");
        builder.AddAttribute(22, "fill", "none");
        builder.AddAttribute(23, "vector-effect", "non-scaling-stroke");
        builder.AddAttribute(24, "points", BuildPoints());
        builder.CloseElement();

        builder.CloseElement();

        if (isPanning)
        {
            // A full-window overlay so the pan gesture keeps tracking even if the cursor leaves
            // the (typically narrow) chart element — same technique as DxDataGrid's column resize.
            builder.OpenElement(25, "div");
            builder.AddAttribute(26, "class", "dx-chart-pan-overlay");
            builder.AddAttribute(27, "onpointermove",
                EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerMovePan));
            builder.AddAttribute(28, "onpointerup", EventCallback.Factory.Create(this, EndPan));
            builder.AddAttribute(29, "onpointerleave", EventCallback.Factory.Create(this, EndPan));
            builder.CloseElement();
        }

        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "class", "dx-chart-caption");
        builder.AddContent(32, S["PointsCaption", "{0:N0} of {1:N0} points · {2}", selected.Length, Points.Count, Compute.Backend]);

        if (zoomed)
        {
            builder.OpenElement(33, "button");
            builder.AddAttribute(34, "type", "button");
            builder.AddAttribute(35, "class", "dx-chart-zoom-reset");
            builder.AddAttribute(36, "onclick", EventCallback.Factory.Create(this, ResetAsync));
            builder.AddContent(37, S["ResetZoom", "Reset zoom"]);
            builder.CloseElement();
        }

        builder.CloseElement();

        if (zoomed)
        {
            builder.OpenElement(38, "div");
            builder.AddAttribute(39, "class", "dx-chart-sr");
            builder.AddAttribute(40, "role", "status");
            builder.AddAttribute(41, "aria-live", "polite");
            builder.AddContent(42, S["ZoomStatus", "Zoomed to {0}–{1} of {2}–{3}", F(zoom.VisibleMin), F(zoom.VisibleMax), F(zoom.DataMin), F(zoom.DataMax)]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private string BuildPoints()
    {
        if (selected.Length == 0)
        {
            return string.Empty;
        }

        double minY = yValues.Min();
        double maxY = yValues.Max();
        double spanY = maxY - minY == 0 ? 1 : maxY - minY;

        StringBuilder points = new(selected.Length * 12);
        foreach (int index in selected)
        {
            double px = ProjectX(xValues[index]);
            double py = (Height - Padding) - ((yValues[index] - minY) / spanY * (Height - (2 * Padding)));
            points.Append(px.ToString("0.#", CultureInfo.InvariantCulture));
            points.Append(',');
            points.Append(py.ToString("0.#", CultureInfo.InvariantCulture));
            points.Append(' ');
        }

        return points.ToString().TrimEnd();
    }

    /// <summary>Projects a data-space X onto the padded plotting area, [Padding, Width - Padding]
    /// over the FULL data domain — unchanged by zoom, exactly as before this feature existed.
    /// Point coordinates are always computed against the full domain; only the viewBox crops.</summary>
    private double ProjectX(double x) =>
        Padding + ((x - zoom.DataMin) / (zoom.DataMax - zoom.DataMin) * (Width - (2 * Padding)));

    /// <summary>Projects a data-space X onto the *unpadded* full SVG canvas, [0, Width] over the
    /// full data domain — used only for the viewBox crop, so that at full zoom-out the viewBox
    /// exactly reduces to "0 0 Width Height" (unpadded), matching this chart's original static
    /// viewBox byte-for-byte.</summary>
    private double ViewBoxX(double x) => (x - zoom.DataMin) / (zoom.DataMax - zoom.DataMin) * Width;

    private string ViewBox()
    {
        double x0 = ViewBoxX(zoom.VisibleMin);
        double w = ViewBoxX(zoom.VisibleMax) - x0;
        return $"{F(x0)} 0 {F(w)} {Height}";
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

    private async Task StartPanAsync(PointerEventArgs e)
    {
        double width = await ChartZoomInterop.MeasureWidthAsync(chartId);
        if (width <= 0)
        {
            // Measurement unavailable (server/prerender, or the element isn't in the DOM yet) —
            // the gesture simply doesn't start rather than computing a garbage pixel-to-data ratio.
            return;
        }

        isPanning = true;
        panStartClientX = e.ClientX;
        panStartMeasuredWidth = width;
        panAppliedDelta = 0;
        StateHasChanged();
    }

    private Task OnPointerMovePan(PointerEventArgs e)
    {
        if (!isPanning)
        {
            return Task.CompletedTask;
        }

        double totalPixelDelta = e.ClientX - panStartClientX;
        // Dragging right reveals earlier data — the direct-manipulation "grab and drag the
        // content" convention, so the sign is negated.
        double totalDataDelta = -(totalPixelDelta * (zoom.VisibleSpan / panStartMeasuredWidth));
        zoom.PanBy(totalDataDelta - panAppliedDelta);
        panAppliedDelta = totalDataDelta;
        StateHasChanged();
        return RaiseZoomChangedAsync();
    }

    private void EndPan()
    {
        isPanning = false;
        StateHasChanged();
    }

    private async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowLeft":
                zoom.PanByFraction(-0.1);
                break;
            case "ArrowRight":
                zoom.PanByFraction(0.1);
                break;
            case "+" or "=":
                zoom.ZoomIn();
                break;
            case "ArrowUp" when e.CtrlKey:
                zoom.ZoomIn();
                break;
            case "-" or "_":
                zoom.ZoomOut();
                break;
            case "ArrowDown" when e.CtrlKey:
                zoom.ZoomOut();
                break;
            case "Home" or "0":
                zoom.Reset();
                break;
            default:
                return;
        }

        await RaiseZoomChangedAsync();
    }

    private Task ResetAsync()
    {
        zoom.Reset();
        return RaiseZoomChangedAsync();
    }

    private Task RaiseZoomChangedAsync() =>
        OnZoomChanged.HasDelegate
            ? OnZoomChanged.InvokeAsync(new ChartZoomChangedEventArgs(zoom.VisibleMin, zoom.VisibleMax, zoom.IsZoomed))
            : Task.CompletedTask;

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
