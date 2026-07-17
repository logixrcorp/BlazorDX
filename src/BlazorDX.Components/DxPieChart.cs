using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A pie or donut chart rendered as SVG arc segments with a legend. Reuses the shared
/// <see cref="ChartPoint"/> data model (<see cref="ChartPoint.Category"/> + <see cref="ChartPoint.Y"/>).
/// Set <see cref="Donut"/> for a donut. Styling is token-driven (see dx-chart.css).
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement (see <see cref="DxBarChart"/>'s remarks for the exact
/// model). The legend is always click/keyboard-operable — clicking an entry hides that slice
/// (redistributing the circle over what remains) and raises <see cref="OnLegendToggled"/>; this
/// does not require wiring <see cref="OnPointSelected"/>.
/// </remarks>
public sealed class DxPieChart : ComponentBase
{
    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-pie-{Guid.NewGuid():N}";
    private readonly HashSet<string> hidden = new(StringComparer.Ordinal);

    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    [Parameter] public bool Donut { get; set; }

    [Parameter] public int Size { get; set; } = 240;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    /// <summary>Raised when a legend entry's visibility is toggled (the chart already hides the slice itself).</summary>
    [Parameter] public EventCallback<ChartLegendToggledEventArgs> OnLegendToggled { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    protected override void OnParametersSet() => selection.ClampTo(Visible().Count);

    // The slices actually drawn: everything not hidden via the legend, in original index order,
    // paired with each entry's original Points index (selection/events report the real index).
    private List<(int Index, ChartPoint Point)> Visible()
    {
        List<(int, ChartPoint)> visible = new(Points.Count);
        for (int i = 0; i < Points.Count; i++)
        {
            if (Points[i].Category is not { } cat || !hidden.Contains(cat))
            {
                visible.Add((i, Points[i]));
            }
        }

        return visible;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        List<(int Index, ChartPoint Point)> visible = Visible();

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart dx-pie-chart {Class}".TrimEnd());

        double total = visible.Sum(s => Math.Max(0, s.Point.Y));
        double cx = Size / 2.0;
        double cy = Size / 2.0;
        double r = (Size / 2.0) - 4;

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-pie-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Size} {Size}");
        builder.AddAttribute(5, "width", Size);
        builder.AddAttribute(6, "height", Size);
        builder.AddAttribute(7, "role", interactive ? "application" : "img");
        builder.AddAttribute(8, "aria-label", $"Pie chart with {visible.Count} slices");

        if (interactive)
        {
            builder.AddAttribute(9, "tabindex", "0");
            if (selection.HasActive)
            {
                builder.AddAttribute(10, "aria-activedescendant", PointId(selection.ActiveIndex));
            }

            builder.AddAttribute(11, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
            builder.AddEventPreventDefaultAttribute(12, "onkeydown", true);
        }

        if (total <= 0)
        {
            builder.CloseElement();
            builder.CloseElement();
            BuildLegend(builder, 0);
            builder.CloseElement();
            return;
        }

        double angle = -Math.PI / 2; // start at 12 o'clock
        foreach ((int index, ChartPoint point) in visible)
        {
            double fraction = Math.Max(0, point.Y) / total;
            double sweep = fraction * 2 * Math.PI;
            double end = angle + sweep;
            string color = point.Color ?? Palette[index % Palette.Length];
            string label = $"{point.Category}: {Pct(fraction)}";

            string css = "dx-pie-slice dx-chart-drawin";
            if (interactive && selection.IsActive(index))
            {
                css += " dx-chart-mark-active";
            }

            if (interactive && selection.IsHovered(index))
            {
                css += " dx-chart-mark-hovered";
            }

            builder.OpenElement(20, "path");
            builder.SetKey(index);
            builder.AddAttribute(21, "class", css);
            builder.AddAttribute(22, "d", Arc(cx, cy, r, angle, end, fraction));
            builder.AddAttribute(23, "fill", color);
            builder.AddAttribute(123, "style", $"animation-delay:{index * 30}ms");

            if (interactive)
            {
                int captured = index;
                builder.AddAttribute(24, "id", PointId(index));
                builder.AddAttribute(25, "aria-label", label);
                builder.AddAttribute(26, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
                builder.AddAttribute(27, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
                builder.AddAttribute(28, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            }

            builder.OpenElement(29, "title");
            builder.AddContent(30, label);
            builder.CloseElement();
            builder.CloseElement();

            angle = end;
        }

        if (Donut)
        {
            builder.OpenElement(31, "circle");
            builder.AddAttribute(32, "class", "dx-pie-hole");
            builder.AddAttribute(33, "cx", F(cx));
            builder.AddAttribute(34, "cy", F(cy));
            builder.AddAttribute(35, "r", F(r * 0.58));
            builder.CloseElement();
        }

        builder.CloseElement();

        BuildLegend(builder, total);

        builder.CloseElement();
    }

    private void BuildLegend(RenderTreeBuilder builder, double total)
    {
        builder.OpenElement(40, "ul");
        builder.AddAttribute(41, "class", "dx-pie-legend");
        for (int i = 0; i < Points.Count; i++)
        {
            string category = Points[i].Category ?? string.Empty;
            bool isHidden = hidden.Contains(category);
            string color = Points[i].Color ?? Palette[i % Palette.Length];

            builder.OpenElement(42, "li");
            builder.SetKey(i);
            builder.AddAttribute(43, "class", isHidden ? "dx-pie-legend-item dx-pie-legend-hidden" : "dx-pie-legend-item");

            builder.OpenElement(44, "button");
            builder.AddAttribute(45, "type", "button");
            builder.AddAttribute(46, "class", "dx-pie-legend-btn");
            builder.AddAttribute(47, "aria-pressed", isHidden ? "false" : "true");
            builder.AddAttribute(48, "onclick", EventCallback.Factory.Create(this, () => ToggleLegendAsync(category)));

            builder.OpenElement(49, "span");
            builder.AddAttribute(50, "class", "dx-pie-swatch");
            builder.AddAttribute(51, "style", $"background:{color}");
            builder.AddAttribute(52, "aria-hidden", "true");
            builder.CloseElement();

            string pct = !isHidden && total > 0 ? $" — {Pct(Math.Max(0, Points[i].Y) / total)}" : string.Empty;
            builder.AddContent(53, $"{category}{pct}");
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    // A pie slice path; a full-circle slice is drawn as two arcs to avoid the
    // degenerate same-point arc that SVG renders as nothing.
    private static string Arc(double cx, double cy, double r, double start, double end, double fraction)
    {
        if (fraction >= 0.9999)
        {
            string top = Point(cx, cy, r, start);
            string opposite = Point(cx, cy, r, start + Math.PI);
            return $"M {top} A {F(r)} {F(r)} 0 1 1 {opposite} A {F(r)} {F(r)} 0 1 1 {top} Z";
        }

        string p1 = Point(cx, cy, r, start);
        string p2 = Point(cx, cy, r, end);
        int largeArc = end - start > Math.PI ? 1 : 0;
        return $"M {F(cx)} {F(cy)} L {p1} A {F(r)} {F(r)} 0 {largeArc} 1 {p2} Z";
    }

    private static string Point(double cx, double cy, double r, double angle) =>
        $"{F(cx + (r * Math.Cos(angle)))} {F(cy + (r * Math.Sin(angle)))}";

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(double fraction) =>
        (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    // ---- Interaction ----

    private string PointId(int index) => $"{chartId}-p{index}";

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        int count = Visible().Count;
        if (selection.MoveActive(args.Key, count))
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

    private Task ToggleLegendAsync(string category)
    {
        bool nowVisible = hidden.Contains(category);
        if (nowVisible)
        {
            hidden.Remove(category);
        }
        else
        {
            hidden.Add(category);
        }

        return OnLegendToggled.HasDelegate
            ? OnLegendToggled.InvokeAsync(new ChartLegendToggledEventArgs(category, nowVisible))
            : Task.CompletedTask;
    }
}
