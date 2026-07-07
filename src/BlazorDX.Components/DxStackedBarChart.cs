using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A multi-series bar chart, stacked by default or grouped side-by-side when
/// <see cref="Stacked"/> is false. Categories run along the x axis; a legend names
/// the series. Reuses the shared <see cref="ChartPoint"/> model: each point's
/// <see cref="ChartPoint.Category"/> places it on the category axis and its
/// <see cref="ChartPoint.Series"/> groups it into a named series. Pure SVG.
/// Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxStackedBarChart : ComponentBase
{
    private const double Gap = 10;

    [Parameter, EditorRequired] public IReadOnlyList<string> Categories { get; set; } = [];

    /// <summary>The points to plot; grouped by <see cref="ChartPoint.Series"/>, placed by <see cref="ChartPoint.Category"/>.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>Stack the series (default) or draw them grouped side-by-side.</summary>
    [Parameter] public bool Stacked { get; set; } = true;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 260;

    [Parameter] public string? Class { get; set; }

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    // Distinct series names in first-seen order — the same order drives colour assignment and
    // the legend, so both stay stable across renders for a given Points list.
    private IReadOnlyList<string> SeriesNames()
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (ChartPoint p in Points)
        {
            if (p.Series is { Length: > 0 } s && seen.Add(s))
            {
                names.Add(s);
            }
        }

        return names;
    }

    private double Value(string series, string category)
    {
        foreach (ChartPoint p in Points)
        {
            if (p.Series == series && p.Category == category)
            {
                return p.Y;
            }
        }

        return 0;
    }

    private string ColorOf(int seriesIndex, string series)
    {
        foreach (ChartPoint p in Points)
        {
            if (p.Series == series && p.Color is not null)
            {
                return p.Color;
            }
        }

        return Palette[seriesIndex % Palette.Length];
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart dx-stacked-chart {Class}".TrimEnd());

        IReadOnlyList<string> names = SeriesNames();

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", "img");
        builder.AddAttribute(6, "aria-label",
            $"{(Stacked ? "Stacked" : "Grouped")} bar chart, {names.Count} series across {Categories.Count} categories");

        if (Categories.Count > 0 && names.Count > 0)
        {
            BuildBars(builder, names);
        }

        builder.CloseElement();

        BuildLegend(builder, names);

        builder.CloseElement();
    }

    private void BuildBars(RenderTreeBuilder builder, IReadOnlyList<string> names)
    {
        double axis = Height - 20;
        double max = Math.Max(1e-9, Maximum(names));
        double slot = (double)Width / Categories.Count;
        double barArea = Math.Max(1, slot - Gap);

        for (int c = 0; c < Categories.Count; c++)
        {
            double slotX = (c * slot) + (Gap / 2);
            string category = Categories[c];

            if (Stacked)
            {
                double y = axis;
                for (int s = 0; s < names.Count; s++)
                {
                    double h = Value(names[s], category) / max * (axis - 14);
                    y -= h;
                    Rect(builder, slotX, y, barArea, h, ColorOf(s, names[s]));
                }
            }
            else
            {
                double subWidth = barArea / names.Count;
                for (int s = 0; s < names.Count; s++)
                {
                    double h = Value(names[s], category) / max * (axis - 14);
                    Rect(builder, slotX + (s * subWidth), axis - h, Math.Max(1, subWidth - 1), h, ColorOf(s, names[s]));
                }
            }

            Text(builder, slotX + (barArea / 2), Height - 6, category);
        }
    }

    private double Maximum(IReadOnlyList<string> names)
    {
        double max = 0;
        for (int c = 0; c < Categories.Count; c++)
        {
            string category = Categories[c];
            if (Stacked)
            {
                double sum = 0;
                for (int s = 0; s < names.Count; s++)
                {
                    sum += Value(names[s], category);
                }

                max = Math.Max(max, sum);
            }
            else
            {
                for (int s = 0; s < names.Count; s++)
                {
                    max = Math.Max(max, Value(names[s], category));
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

    private void BuildLegend(RenderTreeBuilder builder, IReadOnlyList<string> names)
    {
        builder.OpenElement(30, "ul");
        builder.AddAttribute(31, "class", "dx-pie-legend");
        for (int s = 0; s < names.Count; s++)
        {
            builder.OpenElement(32, "li");
            builder.SetKey(s);
            builder.AddAttribute(33, "class", "dx-pie-legend-item");

            builder.OpenElement(34, "span");
            builder.AddAttribute(35, "class", "dx-pie-swatch");
            builder.AddAttribute(36, "style", $"background:{ColorOf(s, names[s])}");
            builder.AddAttribute(37, "aria-hidden", "true");
            builder.CloseElement();

            builder.AddContent(38, names[s]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
