using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A candlestick (OHLC) chart: a high-low wick and an open-close body per candle,
/// coloured up or down. Reuses the shared <see cref="ChartPoint"/> model: <see cref="ChartPoint.Category"/>
/// is the candle's label and <see cref="ChartPoint.Y"/>/<see cref="ChartPoint.Y2"/>/
/// <see cref="ChartPoint.Y3"/>/<see cref="ChartPoint.Y4"/> are Open/High/Low/Close. Pure SVG;
/// styling via dx-chart.css.
/// </summary>
public sealed class DxCandlestickChart : ComponentBase
{
    /// <summary>The candles, left to right.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 280;

    /// <summary>Colour for candles that closed at or above their open.</summary>
    [Parameter] public string UpColor { get; set; } = "#16a34a";

    /// <summary>Colour for candles that closed below their open.</summary>
    [Parameter] public string DownColor { get; set; } = "#dc2626";

    [Parameter] public string? Class { get; set; }

    private const double Pad = 12;

    // Open/High/Low/Close for a point, with High/Low/Close defensively falling back to Open so a
    // malformed candle (e.g. constructed with only Y set) degenerates to a flat doji rather than
    // producing a nonsensical wick.
    private static (double Open, double High, double Low, double Close) Ohlc(ChartPoint p) =>
        (p.Y, p.Y2 ?? p.Y, p.Y3 ?? p.Y, p.Y4 ?? p.Y);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int n = Points.Count;
        double min = double.MaxValue;
        double max = double.MinValue;
        foreach (ChartPoint p in Points)
        {
            (_, double high, double low, _) = Ohlc(p);
            min = Math.Min(min, low);
            max = Math.Max(max, high);
        }

        if (n == 0)
        {
            min = 0;
            max = 1;
        }

        double span = Math.Max(1e-9, max - min);
        double plotH = Height - (2 * Pad);
        double slot = n > 0 ? (Width - (2 * Pad)) / n : Width;
        double bodyW = Math.Max(2, slot * 0.6);

        double Y(double v) => Height - Pad - ((v - min) / span * plotH);

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-chart-svg dx-candlestick {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {Width} {Height}"));
        builder.AddAttribute(4, "role", "img");
        builder.AddAttribute(5, "aria-label", $"Candlestick chart with {n} candles");

        for (int i = 0; i < n; i++)
        {
            (double open, double high, double low, double close) = Ohlc(Points[i]);
            double x = Pad + (i * slot) + (slot / 2);
            bool up = close >= open;
            string color = up ? UpColor : DownColor;

            // Wick (high → low).
            builder.OpenElement(6, "line");
            builder.SetKey(i);
            builder.AddAttribute(7, "x1", Inv($"{x:0.#}"));
            builder.AddAttribute(8, "y1", Inv($"{Y(high):0.#}"));
            builder.AddAttribute(9, "x2", Inv($"{x:0.#}"));
            builder.AddAttribute(10, "y2", Inv($"{Y(low):0.#}"));
            builder.AddAttribute(11, "stroke", color);
            builder.CloseElement();

            // Body (open ↔ close); ensure a minimum height so dojis stay visible.
            double top = Y(Math.Max(open, close));
            double bottom = Y(Math.Min(open, close));
            double h = Math.Max(1, bottom - top);
            builder.OpenElement(12, "rect");
            builder.AddAttribute(13, "x", Inv($"{x - (bodyW / 2):0.#}"));
            builder.AddAttribute(14, "y", Inv($"{top:0.#}"));
            builder.AddAttribute(15, "width", Inv($"{bodyW:0.#}"));
            builder.AddAttribute(16, "height", Inv($"{h:0.#}"));
            builder.AddAttribute(17, "fill", color);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
