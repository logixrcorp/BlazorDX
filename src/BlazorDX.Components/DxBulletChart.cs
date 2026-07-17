using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A bullet chart (Stephen Few's KPI design): one row per <see cref="BulletPoint"/>, each a measure
/// bar against a target tick, optionally over qualitative range bands. Doesn't use
/// <see cref="ChartPoint"/> — see <see cref="BulletPoint"/> for why. Pure SVG; styling via
/// dx-chart.css.
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement (see <see cref="DxBarChart"/>'s remarks) — the
/// interactive mark is a whole row.
/// </remarks>
public sealed class DxBulletChart : ComponentBase
{
    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-bullet-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public IReadOnlyList<BulletPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int RowHeight { get; set; } = 40;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<BulletPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<BulletPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    private int Height => Math.Max(RowHeight, Points.Count * RowHeight);

    protected override void OnParametersSet() => selection.ClampTo(Points.Count);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart {Class}".TrimEnd());

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", interactive ? "application" : "img");
        builder.AddAttribute(6, "aria-label", $"Bullet chart with {Points.Count} KPIs");

        if (interactive)
        {
            builder.AddAttribute(7, "tabindex", "0");
            if (selection.HasActive)
            {
                builder.AddAttribute(8, "aria-activedescendant", RowId(selection.ActiveIndex));
            }

            builder.AddAttribute(9, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(10, "onkeydown", true);
        }

        for (int i = 0; i < Points.Count; i++)
        {
            BuildRow(builder, i, interactive);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildRow(RenderTreeBuilder builder, int index, bool interactive)
    {
        BulletPoint point = Points[index];
        double y0 = index * RowHeight;
        double labelW = 120;
        double trackX = labelW;
        double trackW = Math.Max(1, Width - labelW - 16);
        double trackY = y0 + (RowHeight * 0.3);
        double trackH = RowHeight * 0.4;
        double barY = y0 + (RowHeight * 0.36);
        double barH = RowHeight * 0.28;
        double max = Math.Max(1e-9, point.Max);

        double Scale(double v) => trackX + (Math.Clamp(v, 0, max) / max * trackW);

        string rowCss = "dx-bullet-row dx-chart-drawin";
        if (interactive && selection.IsActive(index))
        {
            rowCss += " dx-chart-mark-active";
        }

        if (interactive && selection.IsHovered(index))
        {
            rowCss += " dx-chart-mark-hovered";
        }

        string title = $"{point.Label}: {Num(point.Value)} of {Num(point.Max)}, target {Num(point.Target)}";

        builder.OpenElement(10, "g");
        builder.SetKey(index);
        builder.AddAttribute(11, "class", rowCss);
        builder.AddAttribute(12, "style", $"animation-delay:{index * 45}ms");

        if (interactive)
        {
            int captured = index;
            builder.AddAttribute(13, "id", RowId(index));
            builder.AddAttribute(14, "aria-label", title);
            builder.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddAttribute(16, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
            builder.AddAttribute(17, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
        }

        Text(builder, 18, 4, y0 + (RowHeight / 2.0) + 4, "dx-bullet-label", point.Label, "start");

        BuildTrack(builder, point, trackX, trackY, trackW, trackH, max);

        // Measure bar.
        builder.OpenElement(30, "rect");
        builder.AddAttribute(31, "class", "dx-bullet-bar");
        builder.AddAttribute(32, "x", F(trackX));
        builder.AddAttribute(33, "y", F(barY));
        builder.AddAttribute(34, "width", F(Scale(point.Value) - trackX));
        builder.AddAttribute(35, "height", F(barH));
        if (point.Color is { } color)
        {
            builder.AddAttribute(36, "fill", color);
        }

        builder.CloseElement();

        // Target tick.
        double tx = Scale(point.Target);
        builder.OpenElement(40, "line");
        builder.AddAttribute(41, "class", "dx-bullet-target");
        builder.AddAttribute(42, "x1", F(tx));
        builder.AddAttribute(43, "y1", F(trackY - 2));
        builder.AddAttribute(44, "x2", F(tx));
        builder.AddAttribute(45, "y2", F(trackY + trackH + 2));
        builder.CloseElement();

        builder.OpenElement(46, "title");
        builder.AddContent(47, title);
        builder.CloseElement();

        builder.CloseElement();
    }

    private static void BuildTrack(
        RenderTreeBuilder builder, BulletPoint point, double trackX, double trackY, double trackW, double trackH,
        double max)
    {
        if (point.Ranges is not { Count: > 0 } ranges)
        {
            builder.OpenElement(20, "rect");
            builder.AddAttribute(21, "class", "dx-bullet-track");
            builder.AddAttribute(22, "x", F(trackX));
            builder.AddAttribute(23, "y", F(trackY));
            builder.AddAttribute(24, "width", F(trackW));
            builder.AddAttribute(25, "height", F(trackH));
            builder.CloseElement();
            return;
        }

        double[] boundaries = [0, .. ranges, max];
        for (int i = 0; i < boundaries.Length - 1; i++)
        {
            double from = Math.Clamp(boundaries[i], 0, max);
            double to = Math.Clamp(boundaries[i + 1], 0, max);
            double x = trackX + (from / max * trackW);
            double w = Math.Max(0, (to - from) / max * trackW);

            builder.OpenElement(20, "rect");
            builder.SetKey(i);
            builder.AddAttribute(21, "class", i % 2 == 0 ? "dx-bullet-range-1" : "dx-bullet-range-2");
            builder.AddAttribute(22, "x", F(x));
            builder.AddAttribute(23, "y", F(trackY));
            builder.AddAttribute(24, "width", F(w));
            builder.AddAttribute(25, "height", F(trackH));
            builder.CloseElement();
        }
    }

    private static void Text(
        RenderTreeBuilder builder, int seq, double x, double y, string cssClass, string content, string anchor)
    {
        builder.OpenElement(seq, "text");
        builder.AddAttribute(seq + 1, "class", cssClass);
        builder.AddAttribute(seq + 2, "x", F(x));
        builder.AddAttribute(seq + 3, "y", F(y));
        builder.AddAttribute(seq + 4, "text-anchor", anchor);
        builder.AddContent(seq + 5, content);
        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Interaction ----

    private string RowId(int index) => $"{chartId}-p{index}";

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
            ? OnPointSelected.InvokeAsync(new BulletPointEventArgs(index, Points[index]))
            : Task.CompletedTask;
    }

    private Task HoverAsync(int index)
    {
        selection.SetHovered(index);
        BulletPoint point = index >= 0 && index < Points.Count ? Points[index] : default;
        return OnPointHovered.HasDelegate
            ? OnPointHovered.InvokeAsync(new BulletPointEventArgs(index, point))
            : Task.CompletedTask;
    }
}
