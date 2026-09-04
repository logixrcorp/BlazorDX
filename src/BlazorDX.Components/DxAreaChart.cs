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
/// An area chart: a single series rendered as a filled SVG polygon with a stroked
/// top edge. Like <see cref="DxLineChart"/> it offloads LTTB downsampling to the
/// compute backend (Rust in the browser) so a huge series draws as a few hundred
/// points. Styling is token-driven (see dx-chart.css).
/// </summary>
/// <remarks>
/// Zoom/pan works exactly like <see cref="DxLineChart"/> (see its remarks and
/// <c>docs/adr/0017-chart-zoom-pan-strategy.md</c>), with one difference: this chart lays out
/// points evenly by *index*, not <see cref="ChartPoint.X"/>, so the zoom domain is
/// <c>[0, pointCount - 1]</c> point-index units rather than real X values.
/// </remarks>
public sealed class DxAreaChart : ComponentBase
{
    private const double Padding = 8;

    private readonly ChartZoomPrimitive zoom = new();
    private readonly string chartId = $"dx-area-{Guid.NewGuid():N}";

    private double[] yValues = [];
    private int[] selected = [];
    private object? lastSeries;

    private bool isPanning;
    private double panStartClientX;
    private double panStartMeasuredWidth;
    private double panAppliedDelta;

    /// <summary>
    /// The series to plot, in order. Only <see cref="ChartPoint.Y"/> is read — points are laid
    /// out evenly by index (matching the prior Values-only behavior), so <see cref="ChartPoint.X"/>
    /// is ignored.
    /// </summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>Approximate number of points to draw.</summary>
    [Parameter] public int Threshold { get; set; } = 300;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 220;

    [Parameter] public string? Class { get; set; }

    /// <summary>Opts into wheel-zoom, drag-pan, and keyboard zoom/pan. Off by default — a chart
    /// doesn't start eating the page's mouse-wheel scroll gesture unless asked.</summary>
    [Parameter] public bool Zoomable { get; set; }

    /// <summary>Raised whenever the visible index range changes from a zoom/pan gesture or a reset.</summary>
    [Parameter] public EventCallback<ChartZoomChangedEventArgs> OnZoomChanged { get; set; }

    [Inject] private IGridCompute Compute { get; set; } = default!;

    [Inject] private IChartZoomInterop ChartZoomInterop { get; set; } = default!;

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(lastSeries, Points))
        {
            lastSeries = Points;
            yValues = new double[Points.Count];
            double[] xs = new double[Points.Count];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = i;
                yValues[i] = Points[i].Y;
            }

            selected = yValues.Length > 0
                ? await Compute.DownsampleAsync(xs, yValues, Threshold)
                : [];

            // Zoom domain is point-index units, not real X — this chart lays out evenly by index.
            zoom.SetDomain(0, Math.Max(0, yValues.Length - 1));

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
        builder.AddAttribute(6, "preserveAspectRatio", "none");
        builder.AddAttribute(7, "role", Zoomable ? "application" : "img");
        builder.AddAttribute(8, "aria-label",
            zoomed
                ? $"Area chart of {selected.Length} downsampled points, zoomed in"
                : $"Area chart of {selected.Length} downsampled points");

        if (Zoomable)
        {
            builder.AddAttribute(9, "tabindex", "0");
            builder.AddAttribute(10, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, OnWheel));
            builder.AddEventPreventDefaultAttribute(11, "onwheel", true);
            builder.AddAttribute(12, "onpointerdown",
                EventCallback.Factory.Create<PointerEventArgs>(this, StartPanAsync));
            builder.AddAttribute(13, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(14, "onkeydown", true);
            builder.AddAttribute(15, "ondblclick", EventCallback.Factory.Create(this, ResetAsync));
        }

        (string line, string area) = BuildPaths();

        builder.OpenElement(16, "polygon");
        builder.AddAttribute(17, "class", "dx-area-fill dx-chart-reveal");
        builder.AddAttribute(18, "points", area);
        builder.CloseElement();

        builder.OpenElement(19, "polyline");
        builder.AddAttribute(20, "class", "dx-chart-line dx-chart-reveal");
        builder.AddAttribute(21, "fill", "none");
        builder.AddAttribute(22, "vector-effect", "non-scaling-stroke");
        builder.AddAttribute(23, "points", line);
        builder.CloseElement();

        builder.CloseElement();

        if (isPanning)
        {
            builder.OpenElement(24, "div");
            builder.AddAttribute(25, "class", "dx-chart-pan-overlay");
            builder.AddAttribute(26, "onpointermove",
                EventCallback.Factory.Create<PointerEventArgs>(this, OnPointerMovePan));
            builder.AddAttribute(27, "onpointerup", EventCallback.Factory.Create(this, EndPan));
            builder.AddAttribute(28, "onpointerleave", EventCallback.Factory.Create(this, EndPan));
            builder.CloseElement();
        }

        builder.OpenElement(29, "div");
        builder.AddAttribute(30, "class", "dx-chart-caption");
        builder.AddContent(31, S["PointsCaption", "{0:N0} of {1:N0} points · {2}", selected.Length, Points.Count, Compute.Backend]);

        if (zoomed)
        {
            builder.OpenElement(32, "button");
            builder.AddAttribute(33, "type", "button");
            builder.AddAttribute(34, "class", "dx-chart-zoom-reset");
            builder.AddAttribute(35, "onclick", EventCallback.Factory.Create(this, ResetAsync));
            builder.AddContent(36, S["ResetZoom", "Reset zoom"]);
            builder.CloseElement();
        }

        builder.CloseElement();

        if (zoomed)
        {
            builder.OpenElement(37, "div");
            builder.AddAttribute(38, "class", "dx-chart-sr");
            builder.AddAttribute(39, "role", "status");
            builder.AddAttribute(40, "aria-live", "polite");
            builder.AddContent(41, S["ZoomStatusPoint", "Zoomed to point {0}–{1} of {2}–{3}", F(zoom.VisibleMin), F(zoom.VisibleMax), F(zoom.DataMin), F(zoom.DataMax)]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private (string Line, string Area) BuildPaths()
    {
        if (selected.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        double minY = yValues.Min();
        double maxY = yValues.Max();
        double spanY = maxY - minY == 0 ? 1 : maxY - minY;
        double baseline = Height - Padding;

        StringBuilder line = new(selected.Length * 12);
        foreach (int index in selected)
        {
            double px = ProjectX(index);
            double py = baseline - ((yValues[index] - minY) / spanY * (Height - (2 * Padding)));
            line.Append(F(px)).Append(',').Append(F(py)).Append(' ');
        }

        string linePoints = line.ToString().TrimEnd();

        // Close the polygon down to the baseline at both ends for the fill. These use the FULL
        // domain's edges (index 0 and the last index), same as before this feature — when
        // zoomed in, they simply fall outside the cropped viewBox, which renders correctly.
        double firstX = ProjectX(0);
        double lastX = ProjectX(selected[^1]);
        string areaPoints = $"{F(firstX)},{F(baseline)} {linePoints} {F(lastX)},{F(baseline)}";
        return (linePoints, areaPoints);
    }

    /// <summary>Projects a point index onto the padded plotting area, over the full index domain —
    /// unchanged by zoom, exactly as before this feature existed.</summary>
    private double ProjectX(double index) =>
        Padding + ((index - zoom.DataMin) / (zoom.DataMax - zoom.DataMin) * (Width - (2 * Padding)));

    /// <summary>Projects a point index onto the *unpadded* full SVG canvas — used only for the
    /// viewBox crop, so full zoom-out exactly reduces to the original static viewBox.</summary>
    private double ViewBoxX(double index) => (index - zoom.DataMin) / (zoom.DataMax - zoom.DataMin) * Width;

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
