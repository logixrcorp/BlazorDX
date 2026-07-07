using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A funnel chart: stacked, centered trapezoids whose widths are proportional to each
/// stage's value (reusing the shared <see cref="ChartPoint"/> model). Pure SVG; styling
/// via dx-chart.css.
/// </summary>
public sealed class DxFunnelChart : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    /// <summary>The funnel stages, top to bottom (<see cref="ChartPoint.Category"/> + <see cref="ChartPoint.Y"/>).</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 380;

    [Parameter] public int Height { get; set; } = 260;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int n = Points.Count;
        double max = 1e-9;
        foreach (ChartPoint stage in Points)
        {
            max = Math.Max(max, stage.Y);
        }

        double cx = Width / 2.0;
        double usable = Width - 24;
        double bandH = n > 0 ? (double)Height / n : Height;

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-chart-svg dx-funnel {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {Width} {Height}"));
        builder.AddAttribute(4, "role", "img");
        builder.AddAttribute(5, "aria-label", $"Funnel chart with {n} stages");

        for (int i = 0; i < n; i++)
        {
            double topW = Points[i].Y / max * usable;
            double botW = (i < n - 1 ? Points[i + 1].Y : Points[i].Y) / max * usable;
            double yTop = i * bandH;
            double yBot = (i + 1) * bandH;
            string color = Points[i].Color ?? Palette[i % Palette.Length];

            string points = string.Create(CultureInfo.InvariantCulture,
                $"{cx - topW / 2:0.#},{yTop:0.#} {cx + topW / 2:0.#},{yTop:0.#} {cx + botW / 2:0.#},{yBot:0.#} {cx - botW / 2:0.#},{yBot:0.#}");

            builder.OpenElement(6, "polygon");
            builder.SetKey(i);
            builder.AddAttribute(7, "points", points);
            builder.AddAttribute(8, "fill", color);
            builder.AddAttribute(9, "fill-opacity", "0.85");
            builder.CloseElement();

            builder.OpenElement(10, "text");
            builder.AddAttribute(11, "x", Inv($"{cx:0.#}"));
            builder.AddAttribute(12, "y", Inv($"{yTop + bandH / 2:0.#}"));
            builder.AddAttribute(13, "text-anchor", "middle");
            builder.AddAttribute(14, "dominant-baseline", "middle");
            builder.AddAttribute(15, "font-size", "12");
            builder.AddAttribute(16, "fill", "#ffffff");
            builder.AddContent(17, $"{Points[i].Category}: {Points[i].Y:0.##}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
