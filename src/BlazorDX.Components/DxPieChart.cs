using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A pie or donut chart rendered as SVG arc segments with a legend. Reuses the shared
/// <see cref="ChartPoint"/> data model (<see cref="ChartPoint.Category"/> + <see cref="ChartPoint.Y"/>).
/// Set <see cref="Donut"/> for a donut. Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxPieChart : ComponentBase
{
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public bool Donut { get; set; }

    [Parameter] public int Size { get; set; } = 240;

    [Parameter] public string? Class { get; set; }

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart dx-pie-chart {Class}".TrimEnd());

        double total = Points.Sum(s => Math.Max(0, s.Y));
        double cx = Size / 2.0;
        double cy = Size / 2.0;
        double r = (Size / 2.0) - 4;

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-pie-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Size} {Size}");
        builder.AddAttribute(5, "width", Size);
        builder.AddAttribute(6, "height", Size);
        builder.AddAttribute(7, "role", "img");
        builder.AddAttribute(8, "aria-label", $"Pie chart with {Points.Count} slices");

        if (total <= 0)
        {
            builder.CloseElement();
            builder.CloseElement();
            return;
        }

        double angle = -Math.PI / 2; // start at 12 o'clock
        for (int i = 0; i < Points.Count; i++)
        {
            double fraction = Math.Max(0, Points[i].Y) / total;
            double sweep = fraction * 2 * Math.PI;
            double end = angle + sweep;
            string color = Points[i].Color ?? Palette[i % Palette.Length];

            builder.OpenElement(10, "path");
            builder.AddAttribute(11, "class", "dx-pie-slice");
            builder.AddAttribute(12, "d", Arc(cx, cy, r, angle, end, fraction));
            builder.AddAttribute(13, "fill", color);
            builder.OpenElement(14, "title");
            builder.AddContent(15, $"{Points[i].Category}: {Pct(fraction)}");
            builder.CloseElement();
            builder.CloseElement();

            angle = end;
        }

        if (Donut)
        {
            builder.OpenElement(16, "circle");
            builder.AddAttribute(17, "class", "dx-pie-hole");
            builder.AddAttribute(18, "cx", F(cx));
            builder.AddAttribute(19, "cy", F(cy));
            builder.AddAttribute(20, "r", F(r * 0.58));
            builder.CloseElement();
        }

        builder.CloseElement();

        BuildLegend(builder, total);

        builder.CloseElement();
    }

    private void BuildLegend(RenderTreeBuilder builder, double total)
    {
        builder.OpenElement(30, "ul");
        builder.AddAttribute(31, "class", "dx-pie-legend");
        for (int i = 0; i < Points.Count; i++)
        {
            string color = Points[i].Color ?? Palette[i % Palette.Length];
            builder.OpenElement(32, "li");
            builder.SetKey(i);
            builder.AddAttribute(33, "class", "dx-pie-legend-item");

            builder.OpenElement(34, "span");
            builder.AddAttribute(35, "class", "dx-pie-swatch");
            builder.AddAttribute(36, "style", $"background:{color}");
            builder.AddAttribute(37, "aria-hidden", "true");
            builder.CloseElement();

            builder.AddContent(38, $"{Points[i].Category} — {Pct(Math.Max(0, Points[i].Y) / total)}");
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // A pie slice path; a full-circle slice is drawn as two arcs to avoid the
    // degenerate same-point arc that SVG renders as nothing.
    private static string Arc(double cx, double cy, double r, double start, double end, double fraction)
    {
        if (fraction >= 0.9999)
        {
            string top = Point(cx, cy, r, start);
            string opposite = Point(cx, cy, r, start + Math.PI);
            return $"M {top} A {F(r)} {F(r)} 0 1 1 {opposite} A {F(r)} {F(r)} 0 1 1 {top} Z";
        }

        string p1 = Point(cx, cy, r, start);
        string p2 = Point(cx, cy, r, end);
        int largeArc = end - start > Math.PI ? 1 : 0;
        return $"M {F(cx)} {F(cy)} L {p1} A {F(r)} {F(r)} 0 {largeArc} 1 {p2} Z";
    }

    private static string Point(double cx, double cy, double r, double angle) =>
        $"{F(cx + (r * Math.Cos(angle)))} {F(cy + (r * Math.Sin(angle)))}";

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(double fraction) =>
        (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
