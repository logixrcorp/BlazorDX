using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A funnel chart: stacked, centered trapezoids whose widths are proportional to each
/// stage's value (reusing <see cref="ChartBar"/>). Pure SVG; styling via dx-chart.css.
/// </summary>
public sealed class DxFunnelChart : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    /// <summary>The funnel stages, top to bottom.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartBar> Stages { get; set; } = [];

    [Parameter] public int Width { get; set; } = 380;

    [Parameter] public int Height { get; set; } = 260;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int n = Stages.Count;
        double max = 1e-9;
        foreach (ChartBar stage in Stages)
        {
            max = Math.Max(max, stage.Value);
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
            double topW = Stages[i].Value / max * usable;
            double botW = (i < n - 1 ? Stages[i + 1].Value : Stages[i].Value) / max * usable;
            double yTop = i * bandH;
            double yBot = (i + 1) * bandH;
            string color = Stages[i].Color ?? Palette[i % Palette.Length];

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
            builder.AddContent(17, $"{Stages[i].Label}: {Stages[i].Value:0.##}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
