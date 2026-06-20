using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>A single category in a bar or pie chart.</summary>
/// <param name="Label">Category name.</param>
/// <param name="Value">Magnitude (non-negative).</param>
/// <param name="Color">Optional CSS color override; otherwise a palette color is used.</param>
public readonly record struct ChartBar(string Label, double Value, string? Color = null);

/// <summary>
/// A categorical bar chart rendered as scaled SVG rectangles with value and
/// category labels. Vertical columns by default; set <see cref="Horizontal"/> for
/// a horizontal bar chart. Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxBarChart : ComponentBase
{
    private const double Gap = 8;

    [Parameter, EditorRequired] public IReadOnlyList<ChartBar> Bars { get; set; } = [];

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
        builder.AddAttribute(6, "aria-label", $"Bar chart with {Bars.Count} categories");

        double max = Bars.Count == 0 ? 1 : Math.Max(1e-9, Bars.Max(b => b.Value));
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
        double slot = (double)Width / Math.Max(1, Bars.Count);
        double barWidth = Math.Max(1, slot - Gap);

        for (int i = 0; i < Bars.Count; i++)
        {
            ChartBar bar = Bars[i];
            double h = bar.Value / max * (axis - 16);
            double x = (i * slot) + (Gap / 2);
            double y = axis - h;
            string color = bar.Color ?? Palette[i % Palette.Length];

            Rect(builder, x, y, barWidth, h, color, $"{bar.Label}: {Num(bar.Value)}");
            Text(builder, x + (barWidth / 2), y - 4, "dx-bar-value", Num(bar.Value), "middle");
            Text(builder, x + (barWidth / 2), Height - 6, "dx-bar-label", bar.Label, "middle");
        }
    }

    private void BuildHorizontal(RenderTreeBuilder builder, double max)
    {
        double labelW = 96;
        double slot = (double)Height / Math.Max(1, Bars.Count);
        double barHeight = Math.Max(1, slot - Gap);

        for (int i = 0; i < Bars.Count; i++)
        {
            ChartBar bar = Bars[i];
            double w = bar.Value / max * (Width - labelW - 40);
            double y = (i * slot) + (Gap / 2);
            string color = bar.Color ?? Palette[i % Palette.Length];

            Text(builder, labelW - 6, y + (barHeight / 2) + 4, "dx-bar-label", bar.Label, "end");
            Rect(builder, labelW, y, w, barHeight, color, $"{bar.Label}: {Num(bar.Value)}");
            Text(builder, labelW + w + 4, y + (barHeight / 2) + 4, "dx-bar-value", Num(bar.Value), "start");
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
