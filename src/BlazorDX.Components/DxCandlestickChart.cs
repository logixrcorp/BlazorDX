using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A candlestick (OHLC) chart: a high-low wick and an open-close body per candle,
/// coloured up or down. Reuses the shared <see cref="ChartPoint"/> model: <see cref="ChartPoint.Category"/>
/// is the candle's label and <see cref="ChartPoint.Y"/>/<see cref="ChartPoint.Y2"/>/
/// <see cref="ChartPoint.Y3"/>/<see cref="ChartPoint.Y4"/> are Open/High/Low/Close. Pure SVG;
/// styling via dx-chart.css.
/// </summary>
/// <remarks>Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks; the
/// interactive mark is the whole wick+body group for a candle.</remarks>
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

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-candle-{Guid.NewGuid():N}";

    private const double Pad = 12;

    // Open/High/Low/Close for a point, with High/Low/Close defensively falling back to Open so a
    // malformed candle (e.g. constructed with only Y set) degenerates to a flat doji rather than
    // producing a nonsensical wick.
    private static (double Open, double High, double Low, double Close) Ohlc(ChartPoint p) =>
        (p.Y, p.Y2 ?? p.Y, p.Y3 ?? p.Y, p.Y4 ?? p.Y);

    protected override void OnParametersSet() => selection.ClampTo(Points.Count);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
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
        builder.AddAttribute(4, "role", interactive ? "application" : "img");
        builder.AddAttribute(5, "aria-label", $"Candlestick chart with {n} candles");

        if (interactive)
        {
            builder.AddAttribute(6, "tabindex", "0");
            if (selection.HasActive)
            {
                builder.AddAttribute(7, "aria-activedescendant", PointId(selection.ActiveIndex));
            }

            builder.AddAttribute(8, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(9, "onkeydown", true);
        }

        for (int i = 0; i < n; i++)
        {
            (double open, double high, double low, double close) = Ohlc(Points[i]);
            double x = Pad + (i * slot) + (slot / 2);
            bool up = close >= open;
            string color = up ? UpColor : DownColor;
            string label = $"{Points[i].Category}: open {open:0.##}, high {high:0.##}, low {low:0.##}, close {close:0.##}";

            string groupCss = "dx-candle";
            if (interactive && selection.IsActive(i))
            {
                groupCss += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                groupCss += " dx-chart-mark-hovered";
            }

            builder.OpenElement(20, "g");
            builder.SetKey(i);
            builder.AddAttribute(21, "class", groupCss);

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(22, "id", PointId(i));
                builder.AddAttribute(23, "aria-label", label);
                builder.AddAttribute(24, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(25, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(26, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            }

            // Wick (high → low).
            builder.OpenElement(27, "line");
            builder.AddAttribute(28, "x1", Inv($"{x:0.#}"));
            builder.AddAttribute(29, "y1", Inv($"{Y(high):0.#}"));
            builder.AddAttribute(30, "x2", Inv($"{x:0.#}"));
            builder.AddAttribute(31, "y2", Inv($"{Y(low):0.#}"));
            builder.AddAttribute(32, "stroke", color);
            builder.CloseElement();

            // Body (open ↔ close); ensure a minimum height so dojis stay visible.
            double top = Y(Math.Max(open, close));
            double bottom = Y(Math.Min(open, close));
            double h = Math.Max(1, bottom - top);
            builder.OpenElement(33, "rect");
            builder.AddAttribute(34, "x", Inv($"{x - (bodyW / 2):0.#}"));
            builder.AddAttribute(35, "y", Inv($"{top:0.#}"));
            builder.AddAttribute(36, "width", Inv($"{bodyW:0.#}"));
            builder.AddAttribute(37, "height", Inv($"{h:0.#}"));
            builder.AddAttribute(38, "fill", color);

            if (!interactive)
            {
                builder.OpenElement(39, "title");
                builder.AddContent(40, label);
                builder.CloseElement();
            }

            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string Inv(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    // ---- Interaction ----

    private string PointId(int index) => $"{chartId}-p{index}";

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (selection.MoveActive(args.Key, Points.Count))
        {
            StateHasChanged();
            return;
        }

        if ((args.Key is "Enter" or " ") && selection.HasActive)
        {
            await SelectAsync(selection.ActiveIndex);
        }
    }

    private Task SelectAsync(int index)
    {
        selection.SetActive(index, Points.Count);
        return OnPointSelected.HasDelegate
            ? OnPointSelected.InvokeAsync(new ChartPointEventArgs(index, Points[index]))
            : Task.CompletedTask;
    }

    private Task HoverAsync(int index)
    {
        selection.SetHovered(index);
        ChartPoint point = index >= 0 && index < Points.Count ? Points[index] : default;
        return OnPointHovered.HasDelegate
            ? OnPointHovered.InvokeAsync(new ChartPointEventArgs(index, point))
            : Task.CompletedTask;
    }
}
