using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A categorical bar chart rendered as scaled SVG rectangles with value and
/// category labels. Vertical columns by default; set <see cref="Horizontal"/> for
/// a horizontal bar chart. Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxBarChart : ComponentBase
{
    private const double Gap = 8;

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public bool Horizontal { get; set; }

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 240;

    [Parameter] public string? Class { get; set; }

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label", $"Bar chart with {Points.Count} categories");

        double max = Points.Count == 0 ? 1 : Math.Max(1e-9, Points.Max(b => b.Y));
        if (Horizontal)
        {
            BuildHorizontal(builder, max);
        }
        else
        {
            BuildVertical(builder, max);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildVertical(RenderTreeBuilder builder, double max)
    {
        double axis = Height - 22;        // leave room for category labels
        double slot = (double)Width / Math.Max(1, Points.Count);
        double barWidth = Math.Max(1, slot - Gap);

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint bar = Points[i];
            double h = bar.Y / max * (axis - 16);
            double x = (i * slot) + (Gap / 2);
            double y = axis - h;
            string color = bar.Color ?? Palette[i % Palette.Length];
            string label = bar.Category ?? string.Empty;

            Rect(builder, x, y, barWidth, h, color, $"{label}: {Num(bar.Y)}");
            Text(builder, x + (barWidth / 2), y - 4, "dx-bar-value", Num(bar.Y), "middle");
            Text(builder, x + (barWidth / 2), Height - 6, "dx-bar-label", label, "middle");
        }
    }

    private void BuildHorizontal(RenderTreeBuilder builder, double max)
    {
        double labelW = 96;
        double slot = (double)Height / Math.Max(1, Points.Count);
        double barHeight = Math.Max(1, slot - Gap);

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint bar = Points[i];
            double w = bar.Y / max * (Width - labelW - 40);
            double y = (i * slot) + (Gap / 2);
            string color = bar.Color ?? Palette[i % Palette.Length];
            string label = bar.Category ?? string.Empty;

            Text(builder, labelW - 6, y + (barHeight / 2) + 4, "dx-bar-label", label, "end");
            Rect(builder, labelW, y, w, barHeight, color, $"{label}: {Num(bar.Y)}");
            Text(builder, labelW + w + 4, y + (barHeight / 2) + 4, "dx-bar-value", Num(bar.Y), "start");
        }
    }

    private static void Rect(
        RenderTreeBuilder builder, double x, double y, double w, double h, string fill, string title)
    {
        builder.OpenElement(10, "rect");
        builder.AddAttribute(11, "class", "dx-bar-rect");
        builder.AddAttribute(12, "x", F(x));
        builder.AddAttribute(13, "y", F(y));
        builder.AddAttribute(14, "width", F(Math.Max(0, w)));
        builder.AddAttribute(15, "height", F(Math.Max(0, h)));
        builder.AddAttribute(16, "rx", "3");
        builder.AddAttribute(17, "fill", fill);
        builder.OpenElement(18, "title");
        builder.AddContent(19, title);
        builder.CloseElement();
        builder.CloseElement();
    }

    private static void Text(
        RenderTreeBuilder builder, double x, double y, string cssClass, string content, string anchor)
    {
        builder.OpenElement(20, "text");
        builder.AddAttribute(21, "class", cssClass);
        builder.AddAttribute(22, "x", F(x));
        builder.AddAttribute(23, "y", F(y));
        builder.AddAttribute(24, "text-anchor", anchor);
        builder.AddContent(25, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
