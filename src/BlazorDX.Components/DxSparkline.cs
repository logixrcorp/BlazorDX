using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A compact, inline trend chart with no axes or labels — a line or bar
/// sparkline scaled to a small box. Pure SVG, no compute dependency (sparkline
/// series are small by nature). Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxSparkline : ComponentBase
{
    private const double Pad = 2;

    /// <summary>
    /// The trend points, in order. Only <see cref="ChartPoint.Y"/> is read — a sparkline lays its
    /// points out evenly by index, so <see cref="ChartPoint.X"/> is ignored.
    /// </summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>"line" (default) or "bar".</summary>
    [Parameter] public string Variant { get; set; } = "line";

    [Parameter] public int Width { get; set; } = 120;

    [Parameter] public int Height { get; set; } = 32;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "class", $"dx-sparkline {Class}".TrimEnd());
        builder.AddAttribute(2, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(3, "width", Width);
        builder.AddAttribute(4, "height", Height);
        builder.AddAttribute(5, "preserveAspectRatio", "none");
        builder.AddAttribute(6, "role", "img");
        builder.AddAttribute(7, "aria-label", $"Sparkline of {Points.Count} points");

        if (Points.Count > 0)
        {
            if (Variant == "bar")
            {
                BuildBars(builder);
            }
            else
            {
                BuildLine(builder);
            }
        }

        builder.CloseElement();
    }

    private void BuildLine(RenderTreeBuilder builder)
    {
        double min = Points.Min(p => p.Y);
        double max = Points.Max(p => p.Y);
        double span = max - min == 0 ? 1 : max - min;
        double stepX = Points.Count > 1 ? (Width - (2 * Pad)) / (Points.Count - 1) : 0;

        StringBuilder points = new(Points.Count * 10);
        for (int i = 0; i < Points.Count; i++)
        {
            double x = Pad + (i * stepX);
            double y = (Height - Pad) - ((Points[i].Y - min) / span * (Height - (2 * Pad)));
            points.Append(F(x)).Append(',').Append(F(y)).Append(' ');
        }

        builder.OpenElement(10, "polyline");
        builder.AddAttribute(11, "class", "dx-sparkline-line");
        builder.AddAttribute(12, "fill", "none");
        builder.AddAttribute(13, "vector-effect", "non-scaling-stroke");
        builder.AddAttribute(14, "points", points.ToString().TrimEnd());
        builder.CloseElement();
    }

    private void BuildBars(RenderTreeBuilder builder)
    {
        double max = Math.Max(1e-9, Points.Max(p => Math.Abs(p.Y)));
        double slot = (double)Width / Points.Count;
        double barWidth = Math.Max(1, slot - 1);

        for (int i = 0; i < Points.Count; i++)
        {
            double h = Math.Abs(Points[i].Y) / max * (Height - Pad);
            double x = i * slot;
            double y = Height - h;

            builder.OpenElement(15, "rect");
            builder.AddAttribute(16, "class", "dx-sparkline-bar");
            builder.AddAttribute(17, "x", F(x));
            builder.AddAttribute(18, "y", F(y));
            builder.AddAttribute(19, "width", F(barWidth));
            builder.AddAttribute(20, "height", F(Math.Max(0, h)));
            builder.CloseElement();
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
