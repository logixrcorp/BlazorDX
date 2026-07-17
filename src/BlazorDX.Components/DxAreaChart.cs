using System.Globalization;
using System.Text;
using BlazorDX.Compute;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// An area chart: a single series rendered as a filled SVG polygon with a stroked
/// top edge. Like <see cref="DxLineChart"/> it offloads LTTB downsampling to the
/// compute backend (Rust in the browser) so a huge series draws as a few hundred
/// points. Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxAreaChart : ComponentBase
{
    private const double Padding = 8;

    private double[] yValues = [];
    private int[] selected = [];
    private object? lastSeries;

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

    [Inject] private IGridCompute Compute { get; set; } = default!;

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
            StateHasChanged();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "preserveAspectRatio", "none");
        builder.AddAttribute(6, "role", "img");
        builder.AddAttribute(7, "aria-label", $"Area chart of {selected.Length} downsampled points");

        (string line, string area) = BuildPaths();

        builder.OpenElement(8, "polygon");
        builder.AddAttribute(9, "class", "dx-area-fill dx-chart-reveal");
        builder.AddAttribute(10, "points", area);
        builder.CloseElement();

        builder.OpenElement(11, "polyline");
        builder.AddAttribute(12, "class", "dx-chart-line dx-chart-reveal");
        builder.AddAttribute(13, "fill", "none");
        builder.AddAttribute(14, "vector-effect", "non-scaling-stroke");
        builder.AddAttribute(15, "points", line);
        builder.CloseElement();

        builder.CloseElement();

        builder.OpenElement(16, "div");
        builder.AddAttribute(17, "class", "dx-chart-caption");
        builder.AddContent(18, $"{selected.Length:N0} of {Points.Count:N0} points · {Compute.Backend}");
        builder.CloseElement();

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
        double maxX = Math.Max(1, yValues.Length - 1);
        double baseline = Height - Padding;

        StringBuilder line = new(selected.Length * 12);
        foreach (int index in selected)
        {
            double px = Padding + (index / maxX * (Width - (2 * Padding)));
            double py = baseline - ((yValues[index] - minY) / spanY * (Height - (2 * Padding)));
            line.Append(F(px)).Append(',').Append(F(py)).Append(' ');
        }

        string linePoints = line.ToString().TrimEnd();

        // Close the polygon down to the baseline at both ends for the fill.
        double firstX = Padding;
        double lastX = Padding + (selected[^1] / maxX * (Width - (2 * Padding)));
        string areaPoints = $"{F(firstX)},{F(baseline)} {linePoints} {F(lastX)},{F(baseline)}";
        return (linePoints, areaPoints);
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
