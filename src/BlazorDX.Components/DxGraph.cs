using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Which underlying chart <see cref="DxGraph"/> renders. Grouped by the data shape each kind
/// reads — see <see cref="DxGraph"/>'s remarks.
/// </summary>
public enum GraphKind
{
    /// <summary>Reads <see cref="DxGraph.Points"/>.</summary>
    Bar,

    /// <summary>Reads <see cref="DxGraph.Points"/>.</summary>
    Area,

    /// <summary>Reads <see cref="DxGraph.Points"/>.</summary>
    Line,

    /// <summary>Reads <see cref="DxGraph.Points"/> (+ <see cref="DxGraph.Donut"/>).</summary>
    Pie,

    /// <summary>Reads <see cref="DxGraph.Points"/>.</summary>
    Scatter,

    /// <summary>Reads <see cref="DxGraph.Points"/> + <see cref="DxGraph.Categories"/>.</summary>
    StackedBar,

    /// <summary>Reads <see cref="DxGraph.Points"/> + <see cref="DxGraph.Axes"/>.</summary>
    Radar,

    /// <summary>Reads <see cref="DxGraph.Points"/>.</summary>
    Funnel,

    /// <summary>Reads <see cref="DxGraph.Points"/> (<c>Y</c>..<c>Y4</c> as OHLC).</summary>
    Candlestick,

    /// <summary>Reads <see cref="DxGraph.Points"/> (<c>Y2</c> marks an absolute total).</summary>
    Waterfall,

    /// <summary>Reads <see cref="DxGraph.Points"/> (<c>Y2</c> sizes the bubble).</summary>
    Bubble,

    /// <summary>Reads <see cref="DxGraph.Points"/> (<c>Series</c>/<c>Category</c> as row/column).</summary>
    Heatmap,

    /// <summary>Reads <see cref="DxGraph.Points"/>.</summary>
    Sparkline,

    /// <summary>Reads <see cref="DxGraph.Root"/>.</summary>
    Treemap,

    /// <summary>Reads <see cref="DxGraph.Root"/>.</summary>
    Sunburst,

    /// <summary>Reads <see cref="DxGraph.Value"/>.</summary>
    RadialGauge,

    /// <summary>Reads <see cref="DxGraph.Value"/>.</summary>
    LinearGauge,

    /// <summary>Reads <see cref="DxGraph.RawValues"/>.</summary>
    Histogram,
}

/// <summary>
/// A single dynamic entry point over the 18 chart kinds whose data reduces to one of three
/// already-shared, strongly-typed shapes: <see cref="ChartPoint"/> (13 kinds), a
/// <see cref="ChartTreeNode"/> root (Treemap/Sunburst), or a bare scalar/raw-sample list (the
/// gauges, Histogram). Switching <see cref="Kind"/> at runtime — e.g. a dropdown toggling the
/// same series between Bar/Line/Area — re-renders through the matching underlying component; no
/// new markup, no re-binding <see cref="Points"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a facade, not a rewrite: every case below opens the real <c>Dx*Chart</c> component via
/// <see cref="RenderTreeBuilder.OpenComponent{TComponent}"/> and forwards typed parameters — zero
/// reflection, zero boxing, and the compiler still catches a typo'd parameter name at the call
/// site (inside <em>this</em> file) exactly as it would in hand-written markup.
/// </para>
/// <para>
/// <b>Why only these 18, not all 25:</b> the other 7 chart types (<c>DxBulletChart</c>,
/// <c>DxBoxPlot</c>, <c>DxSankeyChart</c>, <c>DxNetworkGraph</c>, <c>DxParallelCoordinates</c>,
/// <c>DxWordCloud</c>, <c>DxChordDiagram</c>) each need their own dedicated data record
/// (<c>BulletPoint</c>, <c>BoxPlotGroup</c>, <c>SankeyNode</c>/<c>SankeyLink</c>, etc.) that no
/// other kind reuses. Adding one of those kinds to <see cref="DxGraph"/> would cost exactly one
/// new parameter (or pair) for exactly one kind — no consolidation benefit, just a wider surface
/// on the shared facade. The 18 kinds here were chosen because they're "free riders": 13 kinds
/// share <see cref="ChartPoint"/>, 2 share <see cref="ChartTreeNode"/>, and the gauges/Histogram
/// need only BCL primitives (<see cref="double"/>, <see cref="IReadOnlyList{T}"/> of
/// <see cref="double"/>) — no new record type at all. Those 7 stay as their own named components,
/// used directly — which is exactly what a fundamentally different data shape calls for.
/// </para>
/// </remarks>
public sealed class DxGraph : ComponentBase
{
    [Parameter, EditorRequired] public GraphKind Kind { get; set; }

    [Parameter] public int Width { get; set; } = 640;

    [Parameter] public int Height { get; set; } = 280;

    /// <summary>Used by <see cref="GraphKind.Pie"/>, <see cref="GraphKind.RadialGauge"/>, <see cref="GraphKind.Sunburst"/> (single-dimension charts).</summary>
    [Parameter] public int Size { get; set; } = 240;

    [Parameter] public string? Class { get; set; }

    // ---- ChartPoint family (13 kinds) ----

    [Parameter] public IReadOnlyList<ChartPoint>? Points { get; set; }

    /// <summary>Used by <see cref="GraphKind.StackedBar"/>.</summary>
    [Parameter] public IReadOnlyList<string>? Categories { get; set; }

    /// <summary>Used by <see cref="GraphKind.Radar"/>.</summary>
    [Parameter] public IReadOnlyList<string>? Axes { get; set; }

    /// <summary>Used by <see cref="GraphKind.Bar"/>.</summary>
    [Parameter] public bool Horizontal { get; set; }

    /// <summary>Used by <see cref="GraphKind.Pie"/>.</summary>
    [Parameter] public bool Donut { get; set; }

    /// <summary>Used by <see cref="GraphKind.StackedBar"/>: stacked (default) or grouped.</summary>
    [Parameter] public bool Stacked { get; set; } = true;

    /// <summary>Used by <see cref="GraphKind.Heatmap"/>.</summary>
    [Parameter] public bool ShowValues { get; set; }

    /// <summary>Used by <see cref="GraphKind.Sparkline"/>: "line" (default) or "bar".</summary>
    [Parameter] public string Variant { get; set; } = "line";

    /// <summary>Used by <see cref="GraphKind.Bar"/> and <see cref="GraphKind.Waterfall"/>.</summary>
    [Parameter] public bool Gradient { get; set; }

    /// <summary>
    /// Used by <see cref="GraphKind.Radar"/> (axis max; <c>null</c> auto-scales) and the gauges
    /// (full-scale max; <c>null</c> defaults to 100) — same parameter, different per-kind default.
    /// </summary>
    [Parameter] public double? Max { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointSelected { get; set; }

    [Parameter] public EventCallback<ChartPointEventArgs> OnPointHovered { get; set; }

    // ---- ChartTreeNode family (2 kinds) ----

    [Parameter] public ChartTreeNode? Root { get; set; }

    // ---- Gauges (2 kinds) ----

    [Parameter] public double Value { get; set; }

    [Parameter] public double Min { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Color { get; set; }

    [Parameter] public string Format { get; set; } = "0";

    // ---- Histogram ----

    [Parameter] public IReadOnlyList<double>? RawValues { get; set; }

    [Parameter] public int Bins { get; set; } = 12;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        switch (Kind)
        {
            case GraphKind.Bar: RenderBar(builder); break;
            case GraphKind.Area: RenderArea(builder); break;
            case GraphKind.Line: RenderLine(builder); break;
            case GraphKind.Pie: RenderPie(builder); break;
            case GraphKind.Scatter: RenderScatter(builder); break;
            case GraphKind.StackedBar: RenderStackedBar(builder); break;
            case GraphKind.Radar: RenderRadar(builder); break;
            case GraphKind.Funnel: RenderFunnel(builder); break;
            case GraphKind.Candlestick: RenderCandlestick(builder); break;
            case GraphKind.Waterfall: RenderWaterfall(builder); break;
            case GraphKind.Bubble: RenderBubble(builder); break;
            case GraphKind.Heatmap: RenderHeatmap(builder); break;
            case GraphKind.Sparkline: RenderSparkline(builder); break;
            case GraphKind.Treemap: RenderTreemap(builder); break;
            case GraphKind.Sunburst: RenderSunburst(builder); break;
            case GraphKind.RadialGauge: RenderRadialGauge(builder); break;
            case GraphKind.LinearGauge: RenderLinearGauge(builder); break;
            case GraphKind.Histogram: RenderHistogram(builder); break;
            default: throw new NotSupportedException($"Unknown {nameof(GraphKind)}: {Kind}");
        }
    }

    private void RenderBar(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxBarChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Horizontal", Horizontal);
        builder.AddComponentParameter(5, "Gradient", Gradient);
        builder.AddComponentParameter(6, "Class", Class);
        builder.AddComponentParameter(7, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(8, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderArea(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxAreaChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderLine(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxLineChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderPie(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxPieChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Donut", Donut);
        builder.AddComponentParameter(3, "Size", Size);
        builder.AddComponentParameter(4, "Class", Class);
        builder.AddComponentParameter(5, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(6, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderScatter(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxScatterChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.AddComponentParameter(5, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(6, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderStackedBar(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxStackedBarChart>(0);
        builder.AddComponentParameter(1, "Categories", Categories ?? []);
        builder.AddComponentParameter(2, "Points", Points ?? []);
        builder.AddComponentParameter(3, "Stacked", Stacked);
        builder.AddComponentParameter(4, "Width", Width);
        builder.AddComponentParameter(5, "Height", Height);
        builder.AddComponentParameter(6, "Class", Class);
        builder.AddComponentParameter(7, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(8, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderRadar(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxRadarChart>(0);
        builder.AddComponentParameter(1, "Axes", Axes ?? []);
        builder.AddComponentParameter(2, "Points", Points ?? []);
        builder.AddComponentParameter(3, "Max", Max ?? 0);
        builder.AddComponentParameter(4, "Width", Width);
        builder.AddComponentParameter(5, "Height", Height);
        builder.AddComponentParameter(6, "Class", Class);
        builder.AddComponentParameter(7, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(8, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderFunnel(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxFunnelChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.AddComponentParameter(5, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(6, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderCandlestick(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxCandlestickChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.AddComponentParameter(5, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(6, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderWaterfall(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxWaterfallChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Gradient", Gradient);
        builder.AddComponentParameter(5, "Class", Class);
        builder.AddComponentParameter(6, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(7, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderBubble(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxBubbleChart>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.AddComponentParameter(5, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(6, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderHeatmap(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxHeatmap>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "ShowValues", ShowValues);
        builder.AddComponentParameter(5, "Class", Class);
        builder.AddComponentParameter(6, "OnPointSelected", OnPointSelected);
        builder.AddComponentParameter(7, "OnPointHovered", OnPointHovered);
        builder.CloseComponent();
    }

    private void RenderSparkline(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxSparkline>(0);
        builder.AddComponentParameter(1, "Points", Points ?? []);
        builder.AddComponentParameter(2, "Variant", Variant);
        builder.AddComponentParameter(3, "Width", Width);
        builder.AddComponentParameter(4, "Height", Height);
        builder.AddComponentParameter(5, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderTreemap(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxTreemap>(0);
        builder.AddComponentParameter(1, "Root", Root ?? new ChartTreeNode("Root"));
        builder.AddComponentParameter(2, "Width", Width);
        builder.AddComponentParameter(3, "Height", Height);
        builder.AddComponentParameter(4, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderSunburst(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxSunburst>(0);
        builder.AddComponentParameter(1, "Root", Root ?? new ChartTreeNode("Root"));
        builder.AddComponentParameter(2, "Size", Size);
        builder.AddComponentParameter(3, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderRadialGauge(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxRadialGauge>(0);
        builder.AddComponentParameter(1, "Value", Value);
        builder.AddComponentParameter(2, "Min", Min);
        builder.AddComponentParameter(3, "Max", Max ?? 100);
        builder.AddComponentParameter(4, "Size", Size);
        builder.AddComponentParameter(5, "Label", Label);
        builder.AddComponentParameter(6, "Color", Color);
        builder.AddComponentParameter(7, "Format", Format);
        builder.AddComponentParameter(8, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderLinearGauge(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxLinearGauge>(0);
        builder.AddComponentParameter(1, "Value", Value);
        builder.AddComponentParameter(2, "Min", Min);
        builder.AddComponentParameter(3, "Max", Max ?? 100);
        builder.AddComponentParameter(4, "Width", Width);
        builder.AddComponentParameter(5, "Height", Height);
        builder.AddComponentParameter(6, "Color", Color);
        builder.AddComponentParameter(7, "AriaLabel", Label);
        builder.AddComponentParameter(8, "Class", Class);
        builder.CloseComponent();
    }

    private void RenderHistogram(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxHistogram>(0);
        builder.AddComponentParameter(1, "Values", RawValues ?? []);
        builder.AddComponentParameter(2, "Bins", Bins);
        builder.AddComponentParameter(3, "Width", Width);
        builder.AddComponentParameter(4, "Height", Height);
        builder.AddComponentParameter(5, "Class", Class);
        builder.CloseComponent();
    }
}
