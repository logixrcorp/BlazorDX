using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A categorical bar chart rendered as scaled SVG rectangles with value and
/// category labels. Vertical columns by default; set <see cref="Horizontal"/> for
/// a horizontal bar chart. Styling is token-driven (see dx-chart.css).
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement: wiring <see cref="OnPointSelected"/> and/or
/// <see cref="OnPointHovered"/> turns the chart into a keyboard-navigable widget
/// (<c>role="application"</c>, roving <c>aria-activedescendant</c>, arrow/Home/End, Enter/Space to
/// select) — mirroring the DataGrid/Scheduler/Calendar active-cell pattern via
/// <see cref="ChartSelectionPrimitive"/>. With neither wired, the chart stays exactly the
/// pre-existing decorative <c>role="img"</c> with no behavior change.
/// </remarks>
public sealed class DxBarChart : ComponentBase
{
    private const double Gap = 8;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-bar-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public bool Horizontal { get; set; }

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 240;

    [Parameter] public string? Class { get; set; }

    /// <summary>Raised on click or Enter/Space when a bar is the active (keyboard-focused) point.</summary>
    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    /// <summary>Raised when the pointer enters/leaves a bar (leaving reports index -1).</summary>
    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

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
        builder.AddAttribute(6, "aria-label", $"Bar chart with {Points.Count} categories");

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

        double max = Points.Count == 0 ? 1 : Math.Max(1e-9, Points.Max(b => b.Y));
        if (Horizontal)
        {
            BuildHorizontal(builder, max, interactive);
        }
        else
        {
            BuildVertical(builder, max, interactive);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildVertical(RenderTreeBuilder builder, double max, bool interactive)
    {
        double axis = Height - 22;        // leave room for category labels
        double slot = (double)Width / Math.Max(1, Points.Count);
        double barWidth = Math.Max(1, slot - Gap);

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint bar = Points[i];
            double h = bar.Y / max * (axis - 16);
            double x = (i * slot) + (Gap / 2);
            double y = axis - h;
            string color = bar.Color ?? Palette[i % Palette.Length];
            string label = bar.Category ?? string.Empty;

            Rect(builder, i, x, y, barWidth, h, color, $"{label}: {Num(bar.Y)}", interactive);
            Text(builder, x + (barWidth / 2), y - 4, "dx-bar-value", Num(bar.Y), "middle");
            Text(builder, x + (barWidth / 2), Height - 6, "dx-bar-label", label, "middle");
        }
    }

    private void BuildHorizontal(RenderTreeBuilder builder, double max, bool interactive)
    {
        double labelW = 96;
        double slot = (double)Height / Math.Max(1, Points.Count);
        double barHeight = Math.Max(1, slot - Gap);

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint bar = Points[i];
            double w = bar.Y / max * (Width - labelW - 40);
            double y = (i * slot) + (Gap / 2);
            string color = bar.Color ?? Palette[i % Palette.Length];
            string label = bar.Category ?? string.Empty;

            Text(builder, labelW - 6, y + (barHeight / 2) + 4, "dx-bar-label", label, "end");
            Rect(builder, i, labelW, y, w, barHeight, color, $"{label}: {Num(bar.Y)}", interactive);
            Text(builder, labelW + w + 4, y + (barHeight / 2) + 4, "dx-bar-value", Num(bar.Y), "start");
        }
    }

    private void Rect(
        RenderTreeBuilder builder, int index, double x, double y, double w, double h, string fill, string title,
        bool interactive)
    {
        string css = "dx-bar-rect";
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

        if (interactive)
        {
            int captured = index;
            builder.AddAttribute(38, "id", PointId(index));
            builder.AddAttribute(39, "aria-label", title);
            builder.AddAttribute(40, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddAttribute(41, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
            builder.AddAttribute(42, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
        }

        builder.OpenElement(43, "title");
        builder.AddContent(44, title);
        builder.CloseElement();
        builder.CloseElement();
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

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
