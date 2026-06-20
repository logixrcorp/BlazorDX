using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A radial (circular) gauge: a 270° arc track with a value arc and a center
/// readout. Reports its value as an ARIA <c>meter</c>. Pure SVG. Styling is
/// token-driven (see dx-chart.css).
/// </summary>
public sealed class DxRadialGauge : ComponentBase
{
    private const double StartAngle = 135;   // down-left
    private const double Sweep = 270;        // to down-right

    [Parameter, EditorRequired] public double Value { get; set; }

    [Parameter] public double Min { get; set; }

    [Parameter] public double Max { get; set; } = 100;

    [Parameter] public int Size { get; set; } = 160;

    /// <summary>Optional label shown under the value.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Optional value-arc color override (defaults to the accent token).</summary>
    [Parameter] public string? Color { get; set; }

    /// <summary>Display format for the center value (e.g. "0", "0.0", "P0").</summary>
    [Parameter] public string Format { get; set; } = "0";

    [Parameter] public string? Class { get; set; }

    private double Fraction
    {
        get
        {
            double span = Max - Min;
            if (span <= 0)
            {
                return 0;
            }

            return Math.Clamp((Value - Min) / span, 0, 1);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        double cx = Size / 2.0;
        double cy = Size / 2.0;
        double r = (Size / 2.0) - 12;
        double stroke = 12;

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "class", $"dx-gauge dx-radial-gauge {Class}".TrimEnd());
        builder.AddAttribute(2, "viewBox", $"0 0 {Size} {Size}");
        builder.AddAttribute(3, "width", Size);
        builder.AddAttribute(4, "height", Size);
        builder.AddAttribute(5, "role", "meter");
        builder.AddAttribute(6, "aria-valuenow", F(Value));
        builder.AddAttribute(7, "aria-valuemin", F(Min));
        builder.AddAttribute(8, "aria-valuemax", F(Max));
        if (!string.IsNullOrEmpty(Label))
        {
            builder.AddAttribute(9, "aria-label", Label);
        }

        // Track (full sweep).
        Arc(builder, 10, "dx-gauge-track", cx, cy, r, StartAngle, StartAngle + Sweep, stroke, null);
        // Value arc.
        Arc(builder, 20, "dx-gauge-value", cx, cy, r, StartAngle, StartAngle + (Sweep * Fraction), stroke, Color);

        builder.OpenElement(30, "text");
        builder.AddAttribute(31, "class", "dx-gauge-readout");
        builder.AddAttribute(32, "x", F(cx));
        builder.AddAttribute(33, "y", F(cy + (string.IsNullOrEmpty(Label) ? 4 : 0)));
        builder.AddAttribute(34, "text-anchor", "middle");
        builder.AddContent(35, Value.ToString(Format, CultureInfo.InvariantCulture));
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Label))
        {
            builder.OpenElement(36, "text");
            builder.AddAttribute(37, "class", "dx-gauge-caption");
            builder.AddAttribute(38, "x", F(cx));
            builder.AddAttribute(39, "y", F(cy + 18));
            builder.AddAttribute(40, "text-anchor", "middle");
            builder.AddContent(41, Label);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static void Arc(
        RenderTreeBuilder builder, int seq, string cssClass,
        double cx, double cy, double r, double startDeg, double endDeg, double stroke, string? color)
    {
        if (endDeg <= startDeg)
        {
            return;
        }

        string p1 = Point(cx, cy, r, startDeg);
        string p2 = Point(cx, cy, r, endDeg);
        int largeArc = endDeg - startDeg > 180 ? 1 : 0;

        builder.OpenElement(seq, "path");
        builder.AddAttribute(seq + 1, "class", cssClass);
        builder.AddAttribute(seq + 2, "fill", "none");
        builder.AddAttribute(seq + 3, "stroke-width", F(stroke));
        builder.AddAttribute(seq + 4, "stroke-linecap", "round");
        builder.AddAttribute(seq + 5, "d", $"M {p1} A {F(r)} {F(r)} 0 {largeArc} 1 {p2}");
        if (color is not null)
        {
            builder.AddAttribute(seq + 6, "stroke", color);
        }

        builder.CloseElement();
    }

    private static string Point(double cx, double cy, double r, double deg)
    {
        double rad = deg * Math.PI / 180;
        return $"{F(cx + (r * Math.Cos(rad)))} {F(cy + (r * Math.Sin(rad)))}";
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
