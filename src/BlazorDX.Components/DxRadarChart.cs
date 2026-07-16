using System.Globalization;
using System.Text;
using BlazorDX.Primitives.Charts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A radar (spider) chart: one polygon per series over a shared set of <see cref="Axes"/>.
/// Reuses the shared <see cref="ChartPoint"/> model: each point's <see cref="ChartPoint.Category"/>
/// matches one of <see cref="Axes"/> and its <see cref="ChartPoint.Series"/> groups it into a
/// named series. Pure SVG, no JS or DI; styling via dx-chart.css.
/// </summary>
/// <remarks>
/// Selection is a progressive enhancement (see <see cref="DxBarChart"/>'s remarks). A small vertex
/// dot is drawn at each (series, axis) intersection as the interactive mark once
/// <see cref="OnPointSelected"/> and/or <see cref="OnPointHovered"/> are wired. The legend is
/// always click/keyboard-operable — clicking a series name hides its polygon and vertices and
/// raises <see cref="OnLegendToggled"/>.
/// </remarks>
public sealed class DxRadarChart : ComponentBase
{
    private static readonly string[] Palette =
        ["#2563eb", "#16a34a", "#d97706", "#dc2626", "#7c3aed", "#0891b2", "#db2777", "#65a30d"];

    private readonly ChartSelectionPrimitive selection = new();
    private readonly string chartId = $"dx-radar-{Guid.NewGuid():N}";
    private readonly HashSet<string> hiddenSeries = new(StringComparer.Ordinal);

    /// <summary>Axis labels (one per spoke); points align to these by <see cref="ChartPoint.Category"/>.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<string> Axes { get; set; } = [];

    /// <summary>The points to plot; grouped by <see cref="ChartPoint.Series"/>, placed by <see cref="ChartPoint.Category"/>.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<ChartPoint> Points { get; set; } = [];

    /// <summary>Axis maximum; 0 (default) auto-scales to the largest value.</summary>
    [Parameter] public double Max { get; set; }

    /// <summary>Number of concentric grid rings.</summary>
    [Parameter] public int Rings { get; set; } = 4;

    [Parameter] public int Width { get; set; } = 360;

    [Parameter] public int Height { get; set; } = 360;

    [Parameter] public string? Class { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    [Parameter] public EventCallback<ChartLegendToggledEventArgs> OnLegendToggled { get; set; }

    private bool Interactive => OnPointSelected.HasDelegate || OnPointHovered.HasDelegate;

    protected override void OnParametersSet() => selection.ClampTo(VisibleSeriesNames().Count * Axes.Count);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool interactive = Interactive;
        int n = Axes.Count;
        IReadOnlyList<string> all = AllSeriesNames();
        IReadOnlyList<string> names = VisibleSeriesNames();
        double cx = Width / 2.0;
        double cy = Height / 2.0;
        double radius = (Math.Min(Width, Height) / 2.0) - 44;   // leave room for labels
        double max = Max > 0 ? Max : Math.Max(1e-9, MaxValue());

        builder.OpenElement(0, "svg");
        builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
        builder.AddAttribute(2, "class", $"dx-chart-svg dx-radar {Class}".TrimEnd());
        builder.AddAttribute(3, "viewBox", Inv($"0 0 {Width} {Height}"));
        builder.AddAttribute(4, "role", interactive ? "application" : "img");
        builder.AddAttribute(5, "aria-label", $"Radar chart of {names.Count} series over {n} axes");

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

        if (n >= 3)
        {
            // Concentric grid rings.
            for (int ring = 1; ring <= Rings; ring++)
            {
                double r = radius * ring / Rings;
                Polygon(builder, 20, RingPoints(cx, cy, r, n), "none", "var(--dx-border, #cbd5e1)", 1, 1);
            }

            // Spokes + axis labels.
            for (int i = 0; i < n; i++)
            {
                (double sx, double sy) = Point(cx, cy, radius, i, n);
                Line(builder, 40, cx, cy, sx, sy, "var(--dx-border, #cbd5e1)");
                (double lx, double ly) = Point(cx, cy, radius + 16, i, n);
                Text(builder, 60, lx, ly, Axes[i]);
            }

            // Series polygons + interactive vertex dots.
            for (int s = 0; s < names.Count; s++)
            {
                string color = ColorOf(s, names[s]);
                builder.OpenElement(80, "g");
                builder.SetKey(names[s]);
                Polygon(builder, 81, SeriesPoints(cx, cy, radius, max, names[s], n), color, color, 2, 0.18);
                if (interactive)
                {
                    BuildVertices(builder, cx, cy, radius, max, names[s], s, n);
                }

                builder.CloseElement();
            }
        }

        builder.CloseElement();

        BuildLegend(builder, all);
    }

    private void BuildVertices(RenderTreeBuilder builder, double cx, double cy, double radius, double max, string series, int seriesIndex, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double value = ValueAt(series, Axes[i]);
            (double x, double y) = Point(cx, cy, radius * value / max, i, n);
            int index = (seriesIndex * n) + i;

            string css = "dx-radar-vertex";
            if (selection.IsActive(index))
            {
                css += " dx-chart-mark-active";
            }

            if (selection.IsHovered(index))
            {
                css += " dx-chart-mark-hovered";
            }

            string label = $"{series}, {Axes[i]}: {value:0.##}";
            int captured = index;

            builder.OpenElement(90, "circle");
            builder.SetKey(index);
            builder.AddAttribute(91, "class", css);
            builder.AddAttribute(92, "cx", Inv($"{x:0.#}"));
            builder.AddAttribute(93, "cy", Inv($"{y:0.#}"));
            builder.AddAttribute(94, "r", "4");
            builder.AddAttribute(95, "id", PointId(index));
            builder.AddAttribute(96, "aria-label", label);
            builder.AddAttribute(97, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            builder.AddAttribute(98, "onmouseover", EventCallback.Factory.Create(this, () => HoverAsync(captured)));
            builder.AddAttribute(99, "onmouseout", EventCallback.Factory.Create(this, () => HoverAsync(-1)));
            builder.OpenElement(100, "title");
            builder.AddContent(101, label);
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    // Distinct series names in first-seen order — drives colour assignment and the legend.
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

    private double ValueAt(string series, string axis)
    {
        foreach (ChartPoint p in Points)
        {
            if (p.Series == series && p.Category == axis)
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

    private double MaxValue()
    {
        double max = 0;
        foreach (ChartPoint p in Points)
        {
            max = Math.Max(max, p.Y);
        }

        return max;
    }

    private static (double X, double Y) Point(double cx, double cy, double r, int axisIndex, int n)
    {
        double angle = (-Math.PI / 2) + (axisIndex * 2 * Math.PI / n);
        return (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
    }

    private static string RingPoints(double cx, double cy, double r, int n)
    {
        StringBuilder sb = new();
        for (int i = 0; i < n; i++)
        {
            (double x, double y) = Point(cx, cy, r, i, n);
            Append(sb, x, y);
        }

        return sb.ToString().TrimEnd();
    }

    private string SeriesPoints(double cx, double cy, double radius, double max, string series, int n)
    {
        StringBuilder sb = new();
        for (int i = 0; i < n; i++)
        {
            double value = ValueAt(series, Axes[i]);
            (double x, double y) = Point(cx, cy, radius * value / max, i, n);
            Append(sb, x, y);
        }

        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, double x, double y) =>
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#} "));

    private static void Polygon(RenderTreeBuilder builder, int seq, string points, string fill, string stroke, double strokeWidth, double fillOpacity)
    {
        builder.OpenElement(seq, "polygon");
        builder.AddAttribute(seq + 1, "points", points);
        builder.AddAttribute(seq + 2, "fill", fill);
        builder.AddAttribute(seq + 3, "fill-opacity", Inv($"{fillOpacity}"));
        builder.AddAttribute(seq + 4, "stroke", stroke);
        builder.AddAttribute(seq + 5, "stroke-width", Inv($"{strokeWidth}"));
        builder.CloseElement();
    }

    private static void Line(RenderTreeBuilder builder, int seq, double x1, double y1, double x2, double y2, string stroke)
    {
        builder.OpenElement(seq, "line");
        builder.AddAttribute(seq + 1, "x1", Inv($"{x1:0.#}"));
        builder.AddAttribute(seq + 2, "y1", Inv($"{y1:0.#}"));
        builder.AddAttribute(seq + 3, "x2", Inv($"{x2:0.#}"));
        builder.AddAttribute(seq + 4, "y2", Inv($"{y2:0.#}"));
        builder.AddAttribute(seq + 5, "stroke", stroke);
        builder.CloseElement();
    }

    private static void Text(RenderTreeBuilder builder, int seq, double x, double y, string content)
    {
        builder.OpenElement(seq, "text");
        builder.AddAttribute(seq + 1, "x", Inv($"{x:0.#}"));
        builder.AddAttribute(seq + 2, "y", Inv($"{y:0.#}"));
        builder.AddAttribute(seq + 3, "text-anchor", "middle");
        builder.AddAttribute(seq + 4, "dominant-baseline", "middle");
        builder.AddAttribute(seq + 5, "font-size", "11");
        builder.AddAttribute(seq + 6, "fill", "var(--dx-muted, #64748b)");
        builder.AddContent(seq + 7, content);
        builder.CloseElement();
    }

    private void BuildLegend(RenderTreeBuilder builder, IReadOnlyList<string> names)
    {
        builder.OpenElement(110, "div");
        builder.AddAttribute(111, "class", "dx-pie-legend");
        for (int s = 0; s < names.Count; s++)
        {
            bool isHidden = hiddenSeries.Contains(names[s]);
            string color = ColorOf(s, names[s]);
            builder.OpenElement(112, "span");
            builder.SetKey(s);
            builder.AddAttribute(113, "class", isHidden ? "dx-pie-legend-item dx-pie-legend-hidden" : "dx-pie-legend-item");

            builder.OpenElement(114, "button");
            builder.AddAttribute(115, "type", "button");
            builder.AddAttribute(116, "class", "dx-pie-legend-btn");
            builder.AddAttribute(117, "aria-pressed", isHidden ? "false" : "true");
            string name = names[s];
            builder.AddAttribute(118, "onclick", EventCallback.Factory.Create(this, () => ToggleLegendAsync(name)));

            builder.OpenElement(119, "span");
            builder.AddAttribute(120, "class", "dx-pie-legend-swatch");
            builder.AddAttribute(121, "style", $"background:{color}");
            builder.CloseElement();
            builder.AddContent(122, names[s]);
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
        int count = VisibleSeriesNames().Count * Axes.Count;
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
        IReadOnlyList<string> names = VisibleSeriesNames();
        selection.SetActive(index, names.Count * Axes.Count);
        if (!OnPointSelected.HasDelegate || Axes.Count == 0)
        {
            return Task.CompletedTask;
        }

        int s = index / Axes.Count;
        int i = index % Axes.Count;
        if (s < 0 || s >= names.Count)
        {
            return Task.CompletedTask;
        }

        string axis = Axes[i];
        string series = names[s];
        ChartPoint point = new(Category: axis, Y: ValueAt(series, axis), Series: series);
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
        if (index < 0 || Axes.Count == 0)
        {
            return OnPointHovered.InvokeAsync(new ChartPointEventArgs(-1, default));
        }

        int s = index / Axes.Count;
        int i = index % Axes.Count;
        if (s < 0 || s >= names.Count)
        {
            return OnPointHovered.InvokeAsync(new ChartPointEventArgs(-1, default));
        }

        string axis = Axes[i];
        string series = names[s];
        ChartPoint point = new(Category: axis, Y: ValueAt(series, axis), Series: series);
        return OnPointHovered.InvokeAsync(new ChartPointEventArgs(index, point));
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
