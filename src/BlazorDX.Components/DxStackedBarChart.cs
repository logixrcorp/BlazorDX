using System.Globalization;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A multi-series bar chart, stacked by default or grouped side-by-side when
/// <see cref="Stacked"/> is false. Categories run along the x axis; a legend names
/// the series. Reuses the shared <see cref="ChartPoint"/> model: each point's
/// <see cref="ChartPoint.Category"/> places it on the category axis and its
/// <see cref="ChartPoint.Series"/> groups it into a named series. Pure SVG.
/// Styling is token-driven (see dx-chart.css).
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement (see <see cref="DxBarChart"/>'s remarks). The legend is
/// always click/keyboard-operable — clicking a series name hides its bars across every category
/// and raises <see cref="OnLegendToggled"/>.
/// </remarks>
public sealed class DxStackedBarChart : ComponentBase
{
    private const double Gap = 10;

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-stacked-{Guid.NewGuid():N}";
    private readonly HashSet<string> hiddenSeries = new(StringComparer.Ordinal);

    [Parameter, EditorRequired] public IReadOnlyList<string> Categories { get; set; } = [];

    /// <summary>The points to plot; grouped by <see cref="ChartPoint.Series"/>, placed by <see cref="ChartPoint.Category"/>.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>Stack the series (default) or draw them grouped side-by-side.</summary>
    [Parameter] public bool Stacked { get; set; } = true;

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 260;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    [Parameter] public EventCallback<ChartLegendToggledEventArgs> OnLegendToggled { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    // Distinct series names in first-seen order — the same order drives colour assignment and
    // the legend, so both stay stable across renders for a given Points list.
    private IReadOnlyList<string> AllSeriesNames()
    {
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (ChartPoint p in Points)
        {
            if (p.Series is { Length: > 0 } s && seen.Add(s))
            {
                names.Add(s);
            }
        }

        return names;
    }

    // The series actually drawn (a legend click can hide one), in the same stable order.
    private IReadOnlyList<string> VisibleSeriesNames() =>
        AllSeriesNames().Where(n => !hiddenSeries.Contains(n)).ToList();

    private double Value(string series, string category)
    {
        foreach (ChartPoint p in Points)
        {
            if (p.Series == series && p.Category == category)
            {
                return p.Y;
            }
        }

        return 0;
    }

    private string ColorOf(int seriesIndex, string series)
    {
        foreach (ChartPoint p in Points)
        {
            if (p.Series == series && p.Color is not null)
            {
                return p.Color;
            }
        }

        return Palette[seriesIndex % Palette.Length];
    }

    protected override void OnParametersSet() => selection.ClampTo(Categories.Count * VisibleSeriesNames().Count);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-chart dx-stacked-chart {Class}".TrimEnd());

        IReadOnlyList<string> all = AllSeriesNames();
        IReadOnlyList<string> visible = VisibleSeriesNames();

        builder.OpenElement(2, "svg");
        builder.AddAttribute(3, "class", "dx-chart-svg");
        builder.AddAttribute(4, "viewBox", $"0 0 {Width} {Height}");
        builder.AddAttribute(5, "role", interactive ? "application" : "img");
        builder.AddAttribute(6, "aria-label",
            $"{(Stacked ? "Stacked" : "Grouped")} bar chart, {visible.Count} series across {Categories.Count} categories");

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

        if (Categories.Count > 0 && visible.Count > 0)
        {
            BuildBars(builder, visible, interactive);
        }

        builder.CloseElement();

        BuildLegend(builder, all);

        builder.CloseElement();
    }

    private void BuildBars(RenderTreeBuilder builder, IReadOnlyList<string> names, bool interactive)
    {
        double axis = Height - 20;
        double max = Math.Max(1e-9, Maximum(names));
        double slot = (double)Width / Categories.Count;
        double barArea = Math.Max(1, slot - Gap);

        for (int c = 0; c < Categories.Count; c++)
        {
            double slotX = (c * slot) + (Gap / 2);
            string category = Categories[c];

            if (Stacked)
            {
                double y = axis;
                for (int s = 0; s < names.Count; s++)
                {
                    double h = Value(names[s], category) / max * (axis - 14);
                    y -= h;
                    int index = (c * names.Count) + s;
                    string label = $"{names[s]}, {category}: {Num(Value(names[s], category))}";
                    Rect(builder, index, slotX, y, barArea, h, ColorOf(s, names[s]), label, interactive);
                }
            }
            else
            {
                double subWidth = barArea / names.Count;
                for (int s = 0; s < names.Count; s++)
                {
                    double h = Value(names[s], category) / max * (axis - 14);
                    int index = (c * names.Count) + s;
                    string label = $"{names[s]}, {category}: {Num(Value(names[s], category))}";
                    Rect(builder, index, slotX + (s * subWidth), axis - h, Math.Max(1, subWidth - 1), h,
                        ColorOf(s, names[s]), label, interactive);
                }
            }

            Text(builder, slotX + (barArea / 2), Height - 6, category);
        }
    }

    private double Maximum(IReadOnlyList<string> names)
    {
        double max = 0;
        for (int c = 0; c < Categories.Count; c++)
        {
            string category = Categories[c];
            if (Stacked)
            {
                double sum = 0;
                for (int s = 0; s < names.Count; s++)
                {
                    sum += Value(names[s], category);
                }

                max = Math.Max(max, sum);
            }
            else
            {
                for (int s = 0; s < names.Count; s++)
                {
                    max = Math.Max(max, Value(names[s], category));
                }
            }
        }

        return max;
    }

    private void Rect(
        RenderTreeBuilder builder, int index, double x, double y, double w, double h, string fill, string label,
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

        builder.OpenElement(10, "rect");
        builder.SetKey(index);
        builder.AddAttribute(11, "class", css);
        builder.AddAttribute(12, "x", F(x));
        builder.AddAttribute(13, "y", F(y));
        builder.AddAttribute(14, "width", F(Math.Max(0, w)));
        builder.AddAttribute(15, "height", F(Math.Max(0, h)));
        builder.AddAttribute(16, "fill", fill);

        if (interactive)
        {
            int captured = index;
            builder.AddAttribute(17, "id", PointId(index));
            builder.AddAttribute(18, "aria-label", label);
            builder.AddAttribute(19, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddAttribute(20, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
            builder.AddAttribute(21, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
        }

        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, double x, double y, string content)
    {
        builder.OpenElement(25, "text");
        builder.AddAttribute(26, "class", "dx-bar-label");
        builder.AddAttribute(27, "x", F(x));
        builder.AddAttribute(28, "y", F(y));
        builder.AddAttribute(29, "text-anchor", "middle");
        builder.AddContent(30, content);
        builder.CloseElement();
    }

    private void BuildLegend(RenderTreeBuilder builder, IReadOnlyList<string> names)
    {
        builder.OpenElement(40, "ul");
        builder.AddAttribute(41, "class", "dx-pie-legend");
        for (int s = 0; s < names.Count; s++)
        {
            bool isHidden = hiddenSeries.Contains(names[s]);
            builder.OpenElement(42, "li");
            builder.SetKey(s);
            builder.AddAttribute(43, "class", isHidden ? "dx-pie-legend-item dx-pie-legend-hidden" : "dx-pie-legend-item");

            builder.OpenElement(44, "button");
            builder.AddAttribute(45, "type", "button");
            builder.AddAttribute(46, "class", "dx-pie-legend-btn");
            builder.AddAttribute(47, "aria-pressed", isHidden ? "false" : "true");
            string name = names[s];
            builder.AddAttribute(48, "onclick", EventCallback.Factory.Create(this, () => ToggleLegendAsync(name)));

            builder.OpenElement(49, "span");
            builder.AddAttribute(50, "class", "dx-pie-swatch");
            builder.AddAttribute(51, "style", $"background:{ColorOf(s, names[s])}");
            builder.AddAttribute(52, "aria-hidden", "true");
            builder.CloseElement();

            builder.AddContent(53, names[s]);
            builder.CloseElement();
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Interaction ----

    private string PointId(int index) => $"{chartId}-p{index}";

    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        int count = Categories.Count * VisibleSeriesNames().Count;
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

    // Resolves a flat (category, series) index back to the ChartPoint it represents.
    private Task SelectAsync(int index)
    {
        IReadOnlyList<string> names = VisibleSeriesNames();
        selection.SetActive(index, Categories.Count * names.Count);
        if (!OnPointSelected.HasDelegate || names.Count == 0)
        {
            return Task.CompletedTask;
        }

        int c = index / names.Count;
        int s = index % names.Count;
        string category = Categories[c];
        string series = names[s];
        ChartPoint point = FindPoint(series, category) ?? new ChartPoint(Category: category, Y: 0, Series: series);
        return OnPointSelected.InvokeAsync(new ChartPointEventArgs(index, point));
    }

    private Task HoverAsync(int index)
    {
        selection.SetHovered(index);
        if (!OnPointHovered.HasDelegate)
        {
            return Task.CompletedTask;
        }

        IReadOnlyList<string> names = VisibleSeriesNames();
        if (index < 0 || names.Count == 0)
        {
            return OnPointHovered.InvokeAsync(new ChartPointEventArgs(-1, default));
        }

        int c = index / names.Count;
        int s = index % names.Count;
        ChartPoint point = FindPoint(names[s], Categories[c]) ?? default;
        return OnPointHovered.InvokeAsync(new ChartPointEventArgs(index, point));
    }

    private ChartPoint? FindPoint(string series, string category)
    {
        foreach (ChartPoint p in Points)
        {
            if (p.Series == series && p.Category == category)
            {
                return p;
            }
        }

        return null;
    }

    private Task ToggleLegendAsync(string series)
    {
        bool nowVisible = hiddenSeries.Contains(series);
        if (nowVisible)
        {
            hiddenSeries.Remove(series);
        }
        else
        {
            hiddenSeries.Add(series);
        }

        return OnLegendToggled.HasDelegate
            ? OnLegendToggled.InvokeAsync(new ChartLegendToggledEventArgs(series, nowVisible))
            : Task.CompletedTask;
    }
}
