using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A funnel chart: stacked, centered trapezoids whose widths are proportional to each
/// stage's value (reusing the shared <see cref="ChartPoint"/> model). Pure SVG; styling
/// via dx-chart.css.
/// </summary>
/// <remarks>Selection is a progressive enhancement — see <see cref="DxBarChart"/>'s remarks.</remarks>
public sealed class DxFunnelChart : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-funnel-{Guid.NewGuid():N}";

    /// <summary>The funnel stages, top to bottom (<see cref="ChartPoint.Category"/> + <see cref="ChartPoint.Y"/>).</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public int Width { get; set; } = 380;

    [Parameter] public int Height { get; set; } = 260;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    protected override void OnParametersSet() => selection.ClampTo(Points.Count);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        int n = Points.Count;
        double max = 1e-9;
        foreach (ChartPoint stage in Points)
        {
            max = Math.Max(max, stage.Y);
        }

        double cx = Width / 2.0;
        double usable = Width - 24;
        double bandH = n > 0 ? (double)Height / n : Height;

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-chart-svg dx-funnel {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {Width} {Height}"));
        builder.AddAttribute(4, "role", interactive ? "application" : "img");
        builder.AddAttribute(5, "aria-label", $"Funnel chart with {n} stages");

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
            double topW = Points[i].Y / max * usable;
            double botW = (i < n - 1 ? Points[i + 1].Y : Points[i].Y) / max * usable;
            double yTop = i * bandH;
            double yBot = (i + 1) * bandH;
            string color = Points[i].Color ?? Palette[i % Palette.Length];
            string label = $"{Points[i].Category}: {Points[i].Y:0.##}";

            string points = string.Create(CultureInfo.InvariantCulture,
                $"{cx - topW / 2:0.#},{yTop:0.#} {cx + topW / 2:0.#},{yTop:0.#} {cx + botW / 2:0.#},{yBot:0.#} {cx - botW / 2:0.#},{yBot:0.#}");

            string css = "dx-funnel-stage";
            if (interactive && selection.IsActive(i))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(i))
            {
                css += " dx-chart-mark-hovered";
            }

            builder.OpenElement(20, "polygon");
            builder.SetKey(i);
            builder.AddAttribute(21, "class", css);
            builder.AddAttribute(22, "points", points);
            builder.AddAttribute(23, "fill", color);
            builder.AddAttribute(24, "fill-opacity", "0.85");

            if (interactive)
            {
                int captured = i;
                builder.AddAttribute(25, "id", PointId(i));
                builder.AddAttribute(26, "aria-label", label);
                builder.AddAttribute(27, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(28, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(29, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            }

            builder.CloseElement();

            builder.OpenElement(30, "text");
            builder.AddAttribute(31, "x", Inv($"{cx:0.#}"));
            builder.AddAttribute(32, "y", Inv($"{yTop + bandH / 2:0.#}"));
            builder.AddAttribute(33, "text-anchor", "middle");
            builder.AddAttribute(34, "dominant-baseline", "middle");
            builder.AddAttribute(35, "font-size", "12");
            builder.AddAttribute(36, "fill", "#ffffff");
            builder.AddContent(37, label);
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
