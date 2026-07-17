using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A waterfall chart: bars float from a running total to show how a sequence of positive/negative
/// deltas add up to a final value. Reuses the shared <see cref="ChartPoint"/> model — a point's
/// <see cref="ChartPoint.Y"/> is a delta added to the running total (positive or negative), unless
/// <see cref="ChartPoint.Y2"/> is set, in which case the point is an absolute "total" bar (drawn
/// from zero) that resets the running total to <see cref="ChartPoint.Y2"/>. Pure SVG; styling via
/// dx-chart.css.
/// </summary>
/// <remarks>Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks.</remarks>
public sealed class DxWaterfallChart : ComponentBase
{
    private const double Gap = 8;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-waterfall-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 260;

    /// <summary>Color for a positive delta bar.</summary>
    [Parameter] public string UpColor { get; set; } = "#16a34a";

    /// <summary>Color for a negative delta bar.</summary>
    [Parameter] public string DownColor { get; set; } = "#dc2626";

    /// <summary>Color for an absolute "total" bar (a point with <see cref="ChartPoint.Y2"/> set).</summary>
    [Parameter] public string TotalColor { get; set; } = "#2563eb";

    [Parameter] public bool Gradient { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    protected override void OnParametersSet() => selection.ClampTo(Points.Count);

    private readonly record struct Bar(double From, double To, bool IsTotal);

    private List<Bar> BuildBars()
    {
        List<Bar> bars = new(Points.Count);
        double running = 0;
        foreach (ChartPoint p in Points)
        {
            if (p.Y2 is { } total)
            {
                bars.Add(new Bar(0, total, true));
                running = total;
            }
            else
            {
                bars.Add(new Bar(running, running + p.Y, false));
                running += p.Y;
            }
        }

        return bars;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<Bar> bars = BuildBars();

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", interactive ? "application" : "img");
        builder.AddAttribute(6, "aria-label", $"Waterfall chart with {Points.Count} bars");

        if (interactive)
        {
            builder.AddAttribute(7, "tabindex", "0");
            if (selection.HasActive)
            {
                builder.AddAttribute(8, "aria-activedescendant", PointId(selection.ActiveIndex));
            }

            builder.AddAttribute(9, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(10, "onkeydown", true);
        }

        if (bars.Count > 0)
        {
            BuildChart(builder, bars, interactive);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildChart(RenderTreeBuilder builder, List<Bar> bars, bool interactive)
    {
        double min = Math.Min(0, bars.Min(b => Math.Min(b.From, b.To)));
        double max = Math.Max(0, bars.Max(b => Math.Max(b.From, b.To)));
        double span = Math.Max(1e-9, max - min);
        double labelSpace = 22;
        double plotH = Height - labelSpace;

        double Y(double v) => plotH - ((v - min) / span * plotH);

        double slot = (double)Width / bars.Count;
        double barWidth = Math.Max(1, slot - Gap);

        if (Gradient)
        {
            IEnumerable<string> colors = Points.Select((p, i) => p.Color ?? ColorFor(bars[i]));
            ChartGradients.RenderDefs(builder, 60, chartId, colors);
        }

        for (int i = 0; i < bars.Count; i++)
        {
            Bar bar = bars[i];
            double x = (i * slot) + (Gap / 2);
            double top = Y(Math.Max(bar.From, bar.To));
            double bottom = Y(Math.Min(bar.From, bar.To));
            double h = Math.Max(1, bottom - top);
            string color = Points[i].Color ?? ColorFor(bar);
            string fill = Gradient ? ChartGradients.Url(chartId, color) : color;
            string label = Points[i].Category ?? string.Empty;
            string title = bar.IsTotal
                ? $"{label}: total {Num(bar.To)}"
                : $"{label}: {(bar.To >= bar.From ? "+" : string.Empty)}{Num(bar.To - bar.From)} (running total {Num(bar.To)})";

            // Dashed connector from this bar's end to the next bar's start.
            if (i < bars.Count - 1)
            {
                Connector(builder, x + barWidth, Y(bar.To), x + slot + (Gap / 2), Y(bar.To));
            }

            Rect(builder, i, x, top, barWidth, h, fill, title, interactive);
            Text(builder, x + (barWidth / 2), Height - 4, label);
        }
    }

    private string ColorFor(Bar bar) => bar.IsTotal ? TotalColor : bar.To >= bar.From ? UpColor : DownColor;

    private void Connector(RenderTreeBuilder builder, double x1, double y, double x2, double y2)
    {
        builder.OpenElement(50, "line");
        builder.AddAttribute(51, "class", "dx-waterfall-connector");
        builder.AddAttribute(52, "x1", F(x1));
        builder.AddAttribute(53, "y1", F(y));
        builder.AddAttribute(54, "x2", F(x2));
        builder.AddAttribute(55, "y2", F(y2));
        builder.CloseElement();
    }

    private void Rect(
        RenderTreeBuilder builder, int index, double x, double y, double w, double h, string fill, string title,
        bool interactive)
    {
        string css = "dx-waterfall-rect dx-chart-drawin";
        if (interactive && selection.IsActive(index))
        {
            css += " dx-chart-mark-active";
        }

        if (interactive && selection.IsHovered(index))
        {
            css += " dx-chart-mark-hovered";
        }

        builder.OpenElement(30, "rect");
        builder.SetKey(index);
        builder.AddAttribute(31, "class", css);
        builder.AddAttribute(32, "x", F(x));
        builder.AddAttribute(33, "y", F(y));
        builder.AddAttribute(34, "width", F(Math.Max(0, w)));
        builder.AddAttribute(35, "height", F(Math.Max(0, h)));
        builder.AddAttribute(36, "rx", "3");
        builder.AddAttribute(37, "fill", fill);
        builder.AddAttribute(38, "style", $"animation-delay:{index * 25}ms");

        if (interactive)
        {
            int captured = index;
            builder.AddAttribute(39, "id", PointId(index));
            builder.AddAttribute(40, "aria-label", title);
            builder.AddAttribute(41, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddAttribute(42, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
            builder.AddAttribute(43, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
        }

        builder.OpenElement(44, "title");
        builder.AddContent(45, title);
        builder.CloseElement();
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, double x, double y, string content)
    {
        builder.OpenElement(70, "text");
        builder.AddAttribute(71, "class", "dx-bar-label");
        builder.AddAttribute(72, "x", F(x));
        builder.AddAttribute(73, "y", F(y));
        builder.AddAttribute(74, "text-anchor", "middle");
        builder.AddContent(75, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

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
