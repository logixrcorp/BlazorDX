using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A scatter plot: each (x, y) point is drawn as a dot, scaled to the data's
/// bounds. Reuses the shared <see cref="ChartPoint"/> model (<see cref="ChartPoint.X"/> +
/// <see cref="ChartPoint.Y"/>). Pure SVG. Styling is token-driven (see dx-chart.css).
/// </summary>
/// <remarks>Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks.</remarks>
public sealed class DxScatterChart : ComponentBase
{
    private const double Pad = 10;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-scatter-{Guid.NewGuid():N}";

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 280;

    [Parameter] public double Radius { get; set; } = 3.5;

    [Parameter] public string? Color { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

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
        builder.AddAttribute(6, "aria-label", $"Scatter plot of {Points.Count} points");

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

        for (int i = 0; i < Points.Count; i++)
        {
            ChartPoint point = Points[i];
            double cx = Pad + ((point.X - minX) / spanX * (Width - (2 * Pad)));
            double cy = (Height - Pad) - ((point.Y - minY) / spanY * (Height - (2 * Pad)));

            string css = "dx-scatter-dot";
            if (interactive && selection.IsActive(i))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                css += " dx-chart-mark-hovered";
            }

            builder.OpenElement(10, "circle");
            builder.SetKey(i);
            builder.AddAttribute(11, "class", css);
            builder.AddAttribute(12, "cx", F(cx));
            builder.AddAttribute(13, "cy", F(cy));
            builder.AddAttribute(14, "r", F(Radius));
            if ((point.Color ?? Color) is { } fill)
            {
                builder.AddAttribute(15, "fill", fill);
            }

            if (interactive)
            {
                int captured = i;
                string label = $"({Num(point.X)}, {Num(point.Y)})";
                builder.AddAttribute(16, "id", PointId(i));
                builder.AddAttribute(17, "aria-label", label);
                builder.AddAttribute(18, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(19, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(20, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));

                builder.OpenElement(21, "title");
                builder.AddContent(22, label);
                builder.CloseElement();
            }

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
