using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>An (x, y) point for a scatter plot.</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>
/// A scatter plot: each (x, y) point is drawn as a dot, scaled to the data's
/// bounds. Pure SVG. Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxScatterChart : ComponentBase
{
    private const double Pad = 10;

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 280;

    [Parameter] public double Radius { get; set; } = 3.5;

    [Parameter] public string? Color { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Scatter plot of {Points.Count} points");

        if (Points.Count > 0)
        {
            BuildPoints(builder);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildPoints(RenderTreeBuilder builder)
    {
        double minX = Points.Min(p => p.X);
        double maxX = Points.Max(p => p.X);
        double minY = Points.Min(p => p.Y);
        double maxY = Points.Max(p => p.Y);
        double spanX = maxX - minX == 0 ? 1 : maxX - minX;
        double spanY = maxY - minY == 0 ? 1 : maxY - minY;

        foreach (ChartPoint point in Points)
        {
            double cx = Pad + ((point.X - minX) / spanX * (Width - (2 * Pad)));
            double cy = (Height - Pad) - ((point.Y - minY) / spanY * (Height - (2 * Pad)));

            builder.OpenElement(10, "circle");
            builder.AddAttribute(11, "class", "dx-scatter-dot");
            builder.AddAttribute(12, "cx", F(cx));
            builder.AddAttribute(13, "cy", F(cy));
            builder.AddAttribute(14, "r", F(Radius));
            if (Color is not null)
            {
                builder.AddAttribute(15, "fill", Color);
            }

            builder.CloseElement();
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
