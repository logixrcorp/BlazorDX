using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>A colored zone for <see cref="DxLinearGauge"/>: applies up to <paramref name="UpTo"/>.</summary>
/// <param name="UpTo">Upper bound (inclusive) of the zone, in value units.</param>
/// <param name="Color">CSS color for values at or below the bound.</param>
public readonly record struct GaugeZone(double UpTo, string Color);

/// <summary>
/// A horizontal (linear) gauge: a track with a value fill and an optional set of
/// colored threshold zones. Reports its value as an ARIA <c>meter</c>. Pure SVG.
/// Styling is token-driven (see dx-chart.css).
/// </summary>
public sealed class DxLinearGauge : ComponentBase
{
    [Parameter, EditorRequired] public double Value { get; set; }

    [Parameter] public double Min { get; set; }

    [Parameter] public double Max { get; set; } = 100;

    [Parameter] public int Width { get; set; } = 280;

    [Parameter] public int Height { get; set; } = 18;

    /// <summary>Optional zones (ascending by <see cref="GaugeZone.UpTo"/>) that color the fill.</summary>
    [Parameter] public IReadOnlyList<GaugeZone>? Zones { get; set; }

    [Parameter] public string? Color { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public string? Class { get; set; }

    private double Fraction
    {
        get
        {
            double span = Max - Min;
            return span <= 0 ? 0 : Math.Clamp((Value - Min) / span, 0, 1);
        }
    }

    private string FillColor()
    {
        if (Color is not null)
        {
            return Color;
        }

        if (Zones is { Count: > 0 })
        {
            foreach (GaugeZone zone in Zones)
            {
                if (Value <= zone.UpTo)
                {
                    return zone.Color;
                }
            }

            return Zones[^1].Color;
        }

        return "var(--dx-accent, #2563eb)";
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        double radius = Height / 2.0;
        double fillWidth = Fraction * Width;

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "class", $"dx-gauge dx-linear-gauge {Class}".TrimEnd());
        builder.AddAttribute(2, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(3, "width", Width);
        builder.AddAttribute(4, "height", Height);
        builder.AddAttribute(5, "role", "meter");
        builder.AddAttribute(6, "aria-valuenow", F(Value));
        builder.AddAttribute(7, "aria-valuemin", F(Min));
        builder.AddAttribute(8, "aria-valuemax", F(Max));
        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(9, "aria-label", AriaLabel);
        }

        builder.OpenElement(10, "rect");
        builder.AddAttribute(11, "class", "dx-gauge-track");
        builder.AddAttribute(12, "x", "0");
        builder.AddAttribute(13, "y", "0");
        builder.AddAttribute(14, "width", F(Width));
        builder.AddAttribute(15, "height", F(Height));
        builder.AddAttribute(16, "rx", F(radius));
        builder.CloseElement();

        if (fillWidth > 0)
        {
            builder.OpenElement(20, "rect");
            builder.AddAttribute(21, "class", "dx-gauge-fill");
            builder.AddAttribute(22, "x", "0");
            builder.AddAttribute(23, "y", "0");
            builder.AddAttribute(24, "width", F(fillWidth));
            builder.AddAttribute(25, "height", F(Height));
            builder.AddAttribute(26, "rx", F(radius));
            builder.AddAttribute(27, "fill", FillColor());
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
