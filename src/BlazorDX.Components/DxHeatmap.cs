using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A heatmap: a grid of colored cells, one per (row, column) pair. Reuses the shared
/// <see cref="ChartPoint"/> model — <see cref="ChartPoint.Series"/> is the row key,
/// <see cref="ChartPoint.Category"/> is the column key, and <see cref="ChartPoint.Y"/> is the
/// cell's value. Intensity is drawn as <c>fill-opacity</c> on the chart's accent color (normalized
/// min→max across every cell), not a hand-rolled color scale — theme-safe, and colour is never the
/// only signal (the value is always in the cell's tooltip/label). Pure SVG; styling via dx-chart.css.
/// </summary>
/// <remarks>Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks; the
/// interactive mark is one grid cell, in <c>Points</c> order.</remarks>
public sealed class DxHeatmap : ComponentBase
{
    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-heatmap-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 320;

    /// <summary>Show each cell's value as text (in addition to the tooltip).</summary>
    [Parameter] public bool ShowValues { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    protected override void OnParametersSet() => selection.ClampTo(Points.Count);

    private static List<string> DistinctInOrder(IEnumerable<string?> values)
    {
        List<string> list = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string? v in values)
        {
            string key = v ?? string.Empty;
            if (seen.Add(key))
            {
                list.Add(key);
            }
        }

        return list;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<string> rows = DistinctInOrder(Points.Select(p => p.Series));
        List<string> cols = DistinctInOrder(Points.Select(p => p.Category));

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", interactive ? "application" : "img");
        builder.AddAttribute(6, "aria-label", $"Heatmap with {rows.Count} rows and {cols.Count} columns");

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

        if (rows.Count > 0 && cols.Count > 0)
        {
            BuildGrid(builder, rows, cols, interactive);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildGrid(RenderTreeBuilder builder, List<string> rows, List<string> cols, bool interactive)
    {
        double rowLabelW = 88;
        double colLabelH = 16;
        double gridW = Width - rowLabelW;
        double gridH = Height - colLabelH;
        double cellW = gridW / cols.Count;
        double cellH = gridH / rows.Count;

        double min = Points.Count == 0 ? 0 : Points.Min(p => p.Y);
        double max = Points.Count == 0 ? 1 : Points.Max(p => p.Y);
        double span = max - min == 0 ? 1 : max - min;

        for (int r = 0; r < rows.Count; r++)
        {
            Text(builder, 4, colLabelH + (r * cellH) + (cellH / 2) + 4, "dx-heatmap-label", rows[r], "start");
        }

        for (int c = 0; c < cols.Count; c++)
        {
            Text(builder, rowLabelW + (c * cellW) + (cellW / 2), colLabelH - 4, "dx-heatmap-label", cols[c], "middle");
        }

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint point = Points[i];
            int r = rows.IndexOf(point.Series ?? string.Empty);
            int c = cols.IndexOf(point.Category ?? string.Empty);
            if (r < 0 || c < 0)
            {
                continue;
            }

            double x = rowLabelW + (c * cellW);
            double y = colLabelH + (r * cellH);
            double opacity = 0.12 + ((point.Y - min) / span * 0.88);
            string label = $"{rows[r]} / {cols[c]}: {Num(point.Y)}";

            string css = "dx-heatmap-cell dx-chart-drawin";
            if (interactive && selection.IsActive(i))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                css += " dx-chart-mark-hovered";
            }

            builder.OpenElement(30, "rect");
            builder.SetKey(i);
            builder.AddAttribute(31, "class", css);
            builder.AddAttribute(32, "x", F(x + 1));
            builder.AddAttribute(33, "y", F(y + 1));
            builder.AddAttribute(34, "width", F(Math.Max(0, cellW - 2)));
            builder.AddAttribute(35, "height", F(Math.Max(0, cellH - 2)));
            builder.AddAttribute(36, "fill", point.Color ?? "var(--dx-accent, #2563eb)");
            builder.AddAttribute(37, "fill-opacity", F(opacity));
            builder.AddAttribute(38, "style", $"animation-delay:{i * 10}ms");

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(39, "id", PointId(i));
                builder.AddAttribute(40, "aria-label", label);
                builder.AddAttribute(41, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(42, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(43, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            }

            builder.OpenElement(44, "title");
            builder.AddContent(45, label);
            builder.CloseElement();
            builder.CloseElement();

            if (ShowValues)
            {
                Text(builder, x + (cellW / 2), y + (cellH / 2) + 3, "dx-heatmap-cell-value", Num(point.Y), "middle");
            }
        }
    }

    private static void Text(
        RenderTreeBuilder builder, double x, double y, string cssClass, string content, string anchor)
    {
        builder.OpenElement(50, "text");
        builder.AddAttribute(51, "class", cssClass);
        builder.AddAttribute(52, "x", F(x));
        builder.AddAttribute(53, "y", F(y));
        builder.AddAttribute(54, "text-anchor", anchor);
        builder.AddContent(55, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

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
