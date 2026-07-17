using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A bubble chart: a scatter plot with a third dimension encoded as dot radius. Reuses the shared
/// <see cref="ChartPoint"/> model — <see cref="ChartPoint.X"/> + <see cref="ChartPoint.Y"/> place
/// the bubble, and <see cref="ChartPoint.Y2"/> (when set) sizes it, linearly mapped across the
/// series' own min/max into <see cref="MinRadius"/>..<see cref="MaxRadius"/>. A point with no
/// <see cref="ChartPoint.Y2"/> draws at <see cref="MinRadius"/>. Pure SVG; styling via dx-chart.css.
/// </summary>
/// <remarks>Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks.</remarks>
public sealed class DxBubbleChart : ComponentBase
{
    private const double Pad = 14;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-bubble-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 320;

    [Parameter] public double MinRadius { get; set; } = 6;

    [Parameter] public double MaxRadius { get; set; } = 32;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

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
        builder.AddAttribute(6, "aria-label", $"Bubble chart of {Points.Count} points");

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

        if (Points.Count > 0)
        {
            BuildPoints(builder, interactive);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    private void BuildPoints(RenderTreeBuilder builder, bool interactive)
    {
        double minX = Points.Min(p => p.X);
        double maxX = Points.Max(p => p.X);
        double minY = Points.Min(p => p.Y);
        double maxY = Points.Max(p => p.Y);
        double spanX = maxX - minX == 0 ? 1 : maxX - minX;
        double spanY = maxY - minY == 0 ? 1 : maxY - minY;

        double minSize = Points.Min(p => p.Y2 ?? MinRadius);
        double maxSize = Points.Max(p => p.Y2 ?? MinRadius);
        double spanSize = maxSize - minSize == 0 ? 1 : maxSize - minSize;

        double area = Math.Max(1, Math.Min(Width, Height) - (2 * Pad) - (2 * MaxRadius));

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint point = Points[i];
            double cx = Pad + MaxRadius + ((point.X - minX) / spanX * area);
            double cy = (Height - Pad - MaxRadius) - ((point.Y - minY) / spanY * area);
            double size = point.Y2 ?? minSize;
            double radius = MinRadius + ((size - minSize) / spanSize * (MaxRadius - MinRadius));

            string css = "dx-bubble-dot dx-chart-drawin";
            if (interactive && selection.IsActive(i))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                css += " dx-chart-mark-hovered";
            }

            string color = point.Color ?? Palette[i % Palette.Length];
            string label = point.Y2 is { } sz
                ? $"{point.Category ?? $"({Num(point.X)}, {Num(point.Y)})"}: size {Num(sz)}"
                : point.Category ?? $"({Num(point.X)}, {Num(point.Y)})";

            builder.OpenElement(10, "circle");
            builder.SetKey(i);
            builder.AddAttribute(11, "class", css);
            builder.AddAttribute(12, "cx", F(cx));
            builder.AddAttribute(13, "cy", F(cy));
            builder.AddAttribute(14, "r", F(radius));
            builder.AddAttribute(15, "fill", color);
            builder.AddAttribute(16, "style", $"animation-delay:{i * 10}ms");

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(17, "id", PointId(i));
                builder.AddAttribute(18, "aria-label", label);
                builder.AddAttribute(19, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(20, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(21, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            }

            builder.OpenElement(22, "title");
            builder.AddContent(23, label);
            builder.CloseElement();
            builder.CloseElement();
        }
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
