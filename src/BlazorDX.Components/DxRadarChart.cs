using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A radar (spider) chart: one polygon per <see cref="ChartSeries"/> over a shared
/// set of <see cref="Axes"/>. Pure SVG, no JS or DI; styling via dx-chart.css.
/// </summary>
public sealed class DxRadarChart : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    /// <summary>Axis labels (one per spoke); series values align to these by index.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<string> Axes { get; set; } = [];

    /// <summary>The series to plot; each must have one value per axis.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartSeries> Series { get; set; } = [];

    /// <summary>Axis maximum; 0 (default) auto-scales to the largest value.</summary>
    [Parameter] public double Max { get; set; }

    /// <summary>Number of concentric grid rings.</summary>
    [Parameter] public int Rings { get; set; } = 4;

    [Parameter] public int Width { get; set; } = 360;

    [Parameter] public int Height { get; set; } = 360;

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int n = Axes.Count;
        double cx = Width / 2.0;
        double cy = Height / 2.0;
        double radius = (Math.Min(Width, Height) / 2.0) - 44;   // leave room for labels
        double max = Max > 0 ? Max : Math.Max(1e-9, MaxValue());

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-chart-svg dx-radar {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {Width} {Height}"));
        builder.AddAttribute(4, "role", "img");
        builder.AddAttribute(5, "aria-label", $"Radar chart of {Series.Count} series over {n} axes");

        if (n >= 3)
        {
            // Concentric grid rings.
            for (int ring = 1; ring <= Rings; ring++)
            {
                double r = radius * ring / Rings;
                Polygon(builder, 6, RingPoints(cx, cy, r, n), "none", "var(--dx-border, #cbd5e1)", 1, 1);
            }

            // Spokes + axis labels.
            for (int i = 0; i < n; i++)
            {
                (double sx, double sy) = Point(cx, cy, radius, i, n);
                Line(builder, 10, cx, cy, sx, sy, "var(--dx-border, #cbd5e1)");
                (double lx, double ly) = Point(cx, cy, radius + 16, i, n);
                Text(builder, 14, lx, ly, Axes[i]);
            }

            // Series polygons.
            for (int s = 0; s < Series.Count; s++)
            {
                string color = Series[s].Color ?? Palette[s % Palette.Length];
                Polygon(builder, 20, SeriesPoints(cx, cy, radius, max, Series[s], n), color, color, 2, 0.18);
            }
        }

        builder.CloseElement();

        BuildLegend(builder);
    }

    private double MaxValue()
    {
        double max = 0;
        foreach (ChartSeries series in Series)
        {
            foreach (double v in series.Values)
            {
                max = Math.Max(max, v);
            }
        }

        return max;
    }

    private static (double X, double Y) Point(double cx, double cy, double r, int axisIndex, int n)
    {
        double angle = (-Math.PI / 2) + (axisIndex * 2 * Math.PI / n);
        return (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
    }

    private static string RingPoints(double cx, double cy, double r, int n)
    {
        StringBuilder sb = new();
        for (int i = 0; i < n; i++)
        {
            (double x, double y) = Point(cx, cy, r, i, n);
            Append(sb, x, y);
        }

        return sb.ToString().TrimEnd();
    }

    private static string SeriesPoints(double cx, double cy, double radius, double max, ChartSeries series, int n)
    {
        StringBuilder sb = new();
        for (int i = 0; i < n; i++)
        {
            double value = i < series.Values.Count ? series.Values[i] : 0;
            (double x, double y) = Point(cx, cy, radius * value / max, i, n);
            Append(sb, x, y);
        }

        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, double x, double y) =>
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#} "));

    private static void Polygon(RenderTreeBuilder builder, int seq, string points, string fill, string stroke, double strokeWidth, double fillOpacity)
    {
        builder.OpenElement(seq, "polygon");
        builder.AddAttribute(seq + 1, "points", points);
        builder.AddAttribute(seq + 2, "fill", fill);
        builder.AddAttribute(seq + 3, "fill-opacity", Inv($"{fillOpacity}"));
        builder.AddAttribute(seq + 4, "stroke", stroke);
        builder.AddAttribute(seq + 5, "stroke-width", Inv($"{strokeWidth}"));
        builder.CloseElement();
    }

    private static void Line(RenderTreeBuilder builder, int seq, double x1, double y1, double x2, double y2, string stroke)
    {
        builder.OpenElement(seq, "line");
        builder.AddAttribute(seq + 1, "x1", Inv($"{x1:0.#}"));
        builder.AddAttribute(seq + 2, "y1", Inv($"{y1:0.#}"));
        builder.AddAttribute(seq + 3, "x2", Inv($"{x2:0.#}"));
        builder.AddAttribute(seq + 4, "y2", Inv($"{y2:0.#}"));
        builder.AddAttribute(seq + 5, "stroke", stroke);
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, int seq, double x, double y, string content)
    {
        builder.OpenElement(seq, "text");
        builder.AddAttribute(seq + 1, "x", Inv($"{x:0.#}"));
        builder.AddAttribute(seq + 2, "y", Inv($"{y:0.#}"));
        builder.AddAttribute(seq + 3, "text-anchor", "middle");
        builder.AddAttribute(seq + 4, "dominant-baseline", "middle");
        builder.AddAttribute(seq + 5, "font-size", "11");
        builder.AddAttribute(seq + 6, "fill", "var(--dx-muted, #64748b)");
        builder.AddContent(seq + 7, content);
        builder.CloseElement();
    }

    private void BuildLegend(RenderTreeBuilder builder)
    {
        builder.OpenElement(40, "div");
        builder.AddAttribute(41, "class", "dx-pie-legend");
        for (int s = 0; s < Series.Count; s++)
        {
            string color = Series[s].Color ?? Palette[s % Palette.Length];
            builder.OpenElement(42, "span");
            builder.SetKey(s);
            builder.AddAttribute(43, "class", "dx-pie-legend-item");
            builder.OpenElement(44, "span");
            builder.AddAttribute(45, "class", "dx-pie-legend-swatch");
            builder.AddAttribute(46, "style", $"background:{color}");
            builder.CloseElement();
            builder.AddContent(47, Series[s].Name);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
