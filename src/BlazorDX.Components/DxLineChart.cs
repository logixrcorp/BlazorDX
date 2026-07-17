using System.Globalization;
using System.Text;
using BlazorDX.Compute;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A line chart that renders a large (x, y) series as a single SVG polyline,
/// LTTB-downsampled by the compute backend (Rust in the browser) to keep the
/// visual shape while drawing only a few hundred points. Styling is CSS-variable
/// driven (see dx-layout.css).
/// </summary>
public sealed class DxLineChart : ComponentBase
{
    private const double Padding = 8;

    private double[] xValues = [];
    private double[] yValues = [];
    private int[] selected = [];
    private object? lastSeries;

    /// <summary>The series to plot, reading <see cref="ChartPoint.X"/> + <see cref="ChartPoint.Y"/>.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>Approximate number of points to draw.</summary>
    [Parameter] public int Threshold { get; set; } = 300;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 220;

    [Parameter] public string? Class { get; set; }

    [Inject] private IGridCompute Compute { get; set; } = default!;

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
        builder.AddAttribute(5, "width", Width);
        builder.AddAttribute(6, "height", Height);
        builder.AddAttribute(7, "preserveAspectRatio", "none");
        builder.AddAttribute(8, "role", "img");
        builder.AddAttribute(9, "aria-label", $"Line chart of {selected.Length} downsampled points");

        builder.OpenElement(10, "polyline");
        builder.AddAttribute(11, "class", "dx-chart-line dx-chart-reveal");
        builder.AddAttribute(12, "fill", "none");
        builder.AddAttribute(13, "vector-effect", "non-scaling-stroke");
        builder.AddAttribute(14, "points", BuildPoints());
        builder.CloseElement();

        builder.CloseElement();

        builder.OpenElement(15, "div");
        builder.AddAttribute(16, "class", "dx-chart-caption");
        builder.AddContent(17, $"{selected.Length:N0} of {Points.Count:N0} points · {Compute.Backend}");
        builder.CloseElement();

        builder.CloseElement();
    }

    private string BuildPoints()
    {
        if (selected.Length == 0)
        {
            return string.Empty;
        }

        double minX = xValues.Min();
        double maxX = xValues.Max();
        double minY = yValues.Min();
        double maxY = yValues.Max();
        double spanX = maxX - minX == 0 ? 1 : maxX - minX;
        double spanY = maxY - minY == 0 ? 1 : maxY - minY;

        StringBuilder points = new(selected.Length * 12);
        foreach (int index in selected)
        {
            double px = Padding + ((xValues[index] - minX) / spanX * (Width - (2 * Padding)));
            double py = (Height - Padding) - ((yValues[index] - minY) / spanY * (Height - (2 * Padding)));
            points.Append(px.ToString("0.#", CultureInfo.InvariantCulture));
            points.Append(',');
            points.Append(py.ToString("0.#", CultureInfo.InvariantCulture));
            points.Append(' ');
        }

        return points.ToString().TrimEnd();
    }
}
