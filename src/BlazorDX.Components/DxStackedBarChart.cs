using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>A named data series for a multi-series chart.</summary>
/// <param name="Name">Series label (shown in the legend).</param>
/// <param name="Values">One value per category, in category order.</param>
/// <param name="Color">Optional color override; otherwise a palette color.</param>
public readonly record struct ChartSeries(string Name, IReadOnlyList<double> Values, string? Color = null);

/// <summary>
/// A multi-series bar chart, stacked by default or grouped side-by-side when
/// <see cref="Stacked"/> is false. Categories run along the x axis; a legend names
/// the series. Pure SVG. Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxStackedBarChart : ComponentBase
{
    private const double Gap = 10;

    [Parameter, EditorRequired] public IReadOnlyList<string> Categories { get; set; } = [];

    [Parameter, EditorRequired] public IReadOnlyList<ChartSeries> Series { get; set; } = [];

    /// <summary>Stack the series (default) or draw them grouped side-by-side.</summary>
    [Parameter] public bool Stacked { get; set; } = true;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 260;

    [Parameter] public string? Class { get; set; }

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    private double Value(int seriesIndex, int category) =>
        category < Series[seriesIndex].Values.Count ? Series[seriesIndex].Values[category] : 0;

    private string ColorOf(int seriesIndex) => Series[seriesIndex].Color ?? Palette[seriesIndex % Palette.Length];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart dx-stacked-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label",
            $"{(Stacked ? "Stacked" : "Grouped")} bar chart, {Series.Count} series across {Categories.Count} categories");

        if (Categories.Count > 0 && Series.Count > 0)
        {
            BuildBars(builder);
        }

        builder.CloseElement();

        BuildLegend(builder);

        builder.CloseElement();
    }

    private void BuildBars(RenderTreeBuilder builder)
    {
        double axis = Height - 20;
        double max = Math.Max(1e-9, Maximum());
        double slot = (double)Width / Categories.Count;
        double barArea = Math.Max(1, slot - Gap);

        for (int c = 0; c < Categories.Count; c++)
        {
            double slotX = (c * slot) + (Gap / 2);

            if (Stacked)
            {
                double y = axis;
                for (int s = 0; s < Series.Count; s++)
                {
                    double h = Value(s, c) / max * (axis - 14);
                    y -= h;
                    Rect(builder, slotX, y, barArea, h, ColorOf(s));
                }
            }
            else
            {
                double subWidth = barArea / Series.Count;
                for (int s = 0; s < Series.Count; s++)
                {
                    double h = Value(s, c) / max * (axis - 14);
                    Rect(builder, slotX + (s * subWidth), axis - h, Math.Max(1, subWidth - 1), h, ColorOf(s));
                }
            }

            Text(builder, slotX + (barArea / 2), Height - 6, Categories[c]);
        }
    }

    private double Maximum()
    {
        double max = 0;
        for (int c = 0; c < Categories.Count; c++)
        {
            if (Stacked)
            {
                double sum = 0;
                for (int s = 0; s < Series.Count; s++)
                {
                    sum += Value(s, c);
                }

                max = Math.Max(max, sum);
            }
            else
            {
                for (int s = 0; s < Series.Count; s++)
                {
                    max = Math.Max(max, Value(s, c));
                }
            }
        }

        return max;
    }

    private static void Rect(RenderTreeBuilder builder, double x, double y, double w, double h, string fill)
    {
        builder.OpenElement(10, "rect");
        builder.AddAttribute(11, "class", "dx-bar-rect");
        builder.AddAttribute(12, "x", F(x));
        builder.AddAttribute(13, "y", F(y));
        builder.AddAttribute(14, "width", F(Math.Max(0, w)));
        builder.AddAttribute(15, "height", F(Math.Max(0, h)));
        builder.AddAttribute(16, "fill", fill);
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, double x, double y, string content)
    {
        builder.OpenElement(17, "text");
        builder.AddAttribute(18, "class", "dx-bar-label");
        builder.AddAttribute(19, "x", F(x));
        builder.AddAttribute(20, "y", F(y));
        builder.AddAttribute(21, "text-anchor", "middle");
        builder.AddContent(22, content);
        builder.CloseElement();
    }

    private void BuildLegend(RenderTreeBuilder builder)
    {
        builder.OpenElement(30, "ul");
        builder.AddAttribute(31, "class", "dx-pie-legend");
        for (int s = 0; s < Series.Count; s++)
        {
            builder.OpenElement(32, "li");
            builder.SetKey(s);
            builder.AddAttribute(33, "class", "dx-pie-legend-item");

            builder.OpenElement(34, "span");
            builder.AddAttribute(35, "class", "dx-pie-swatch");
            builder.AddAttribute(36, "style", $"background:{ColorOf(s)}");
            builder.AddAttribute(37, "aria-hidden", "true");
            builder.CloseElement();

            builder.AddContent(38, Series[s].Name);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
