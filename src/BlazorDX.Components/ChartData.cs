namespace BlazorDX.Components;

/// <summary>
/// The single, generic data point every BlazorDX chart accepts — replacing the previous per-chart
/// bespoke shapes (<c>ChartBar</c>, parallel <c>X</c>/<c>Y</c> lists, a bare <c>Values</c> list, and
/// per-series <c>ChartSeries</c> arrays). Not every field applies to every chart: a
/// bar/pie/funnel/sparkline chart reads <see cref="Category"/> + <see cref="Y"/>; a
/// line/area/scatter chart reads <see cref="X"/> + <see cref="Y"/>; a stacked-bar/radar chart also
/// reads <see cref="Series"/> to group points onto a shared category/axis list; a candlestick
/// chart reads <see cref="Y"/>..<see cref="Y4"/> as Open/High/Low/Close. A field a given chart
/// type doesn't use is simply ignored. Plain record struct — no reflection, no per-consumer mapping
/// step (a <c>[ChartRow]</c> source generator for projecting an existing domain type onto this
/// shape is planned as a follow-up).
/// </summary>
/// <param name="X">X-axis value (line/area/scatter charts).</param>
/// <param name="Y">
/// The primary value: bar/pie/funnel/sparkline magnitude, an XY chart's Y, or a candlestick's Open.
/// </param>
/// <param name="Category">
/// Category / axis-label key: the bar/pie/funnel/sparkline label, the stacked-bar/radar
/// category-or-axis key, or the candlestick's label.
/// </param>
/// <param name="Y2">Candlestick High.</param>
/// <param name="Y3">Candlestick Low.</param>
/// <param name="Y4">Candlestick Close.</param>
/// <param name="Series">Series name, for charts with more than one series (stacked bar, radar).</param>
/// <param name="Color">Optional CSS color override for this point/series; otherwise a palette color is used.</param>
public readonly record struct ChartPoint(
    double X = 0,
    double Y = 0,
    string? Category = null,
    double? Y2 = null,
    double? Y3 = null,
    double? Y4 = null,
    string? Series = null,
    string? Color = null);

/// <summary>
/// The point a click, keyboard selection, or hover interaction occurred on, for the discrete-mark
/// charts (bar, pie, funnel, scatter, stacked bar, radar, candlestick). Continuous/downsampled
/// charts (line, area) and the decorative sparkline don't raise this — see
/// <see cref="BlazorDX.Primitives.Charts.ChartSelectionPrimitive"/>.
/// </summary>
/// <param name="Index">The point's index into the chart's <c>Points</c> list.</param>
/// <param name="Point">The point itself.</param>
public readonly record struct ChartPointEventArgs(int Index, ChartPoint Point);

/// <summary>
/// A legend entry's visibility was toggled on a multi-series chart (pie, stacked bar, radar). The
/// chart already hides/shows the corresponding slice or series itself when this fires — the event
/// is for a host that wants to react (e.g. update an external summary), not required plumbing.
/// </summary>
/// <param name="Key">The toggled entry's key: a pie slice's <see cref="ChartPoint.Category"/>, or a stacked-bar/radar <see cref="ChartPoint.Series"/> name.</param>
/// <param name="Visible">The entry's new visibility state.</param>
public readonly record struct ChartLegendToggledEventArgs(string Key, bool Visible);

/// <summary>
/// Which <see cref="ChartPoint"/> field a <see cref="ChartValueAttribute"/>-tagged property maps
/// onto. <see cref="Category"/>/<see cref="Series"/>/<see cref="Color"/> accept a property of any
/// type (stringified via <c>Convert.ToString</c>, so an <c>int</c> or <c>enum</c> category works
/// as-is); <see cref="X"/>/<see cref="Y"/>/<see cref="Y2"/>/<see cref="Y3"/>/<see cref="Y4"/>
/// require a numeric-convertible property — a non-numeric property tagged with one of these is
/// silently not mapped (the field keeps its <see cref="ChartPoint"/> default), the same
/// non-numeric-columns-degrade-gracefully policy <c>[GridColumn]</c> already uses.
/// </summary>
public enum ChartField
{
    /// <summary>→ <see cref="ChartPoint.Category"/>.</summary>
    Category,

    /// <summary>→ <see cref="ChartPoint.X"/>.</summary>
    X,

    /// <summary>→ <see cref="ChartPoint.Y"/>.</summary>
    Y,

    /// <summary>→ <see cref="ChartPoint.Y2"/> (candlestick High).</summary>
    Y2,

    /// <summary>→ <see cref="ChartPoint.Y3"/> (candlestick Low).</summary>
    Y3,

    /// <summary>→ <see cref="ChartPoint.Y4"/> (candlestick Close).</summary>
    Y4,

    /// <summary>→ <see cref="ChartPoint.Series"/>.</summary>
    Series,

    /// <summary>→ <see cref="ChartPoint.Color"/>.</summary>
    Color,
}

/// <summary>
/// Marks a domain type as projectable onto <see cref="ChartPoint"/>. The <c>BlazorDX.SourceGen</c>
/// generator emits a <c>ToChartPoints()</c> extension for every type carrying this attribute (one
/// <see cref="ChartPoint"/> per row, built from its <see cref="ChartValueAttribute"/>-tagged
/// properties), so charts bind existing domain data with zero runtime reflection — the same
/// zero-reflection story <c>[GridRow]</c> already tells for the DataGrid.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ChartRowAttribute : Attribute
{
}

/// <summary>Declares a property as the source of one <see cref="ChartPoint"/> field. See <see cref="ChartField"/>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ChartValueAttribute : Attribute
{
    public ChartValueAttribute(ChartField field)
    {
        Field = field;
    }

    public ChartField Field { get; }
}

/// <summary>
/// One KPI row for <see cref="DxBulletChart"/> (Stephen Few's bullet-graph design): a single
/// measure bar against a target marker and, optionally, qualitative range bands (e.g.
/// poor/satisfactory/good). Doesn't fit the flat <see cref="ChartPoint"/> shape — a bullet row has
/// its own scale (<see cref="Max"/>) and multiple thresholds, not one X/Y pair.
/// </summary>
/// <param name="Label">The KPI's name.</param>
/// <param name="Value">The measure — how far the bar fills.</param>
/// <param name="Target">Where the target tick is drawn.</param>
/// <param name="Max">The scale's upper bound; <see cref="Value"/> and <see cref="Target"/> read against <c>[0, Max]</c>.</param>
/// <param name="Ranges">
/// Ascending thresholds within <c>[0, Max]</c> splitting the track into qualitative bands (e.g.
/// <c>[40, 75]</c> for poor/satisfactory/good). <c>null</c> draws a single plain track.
/// </param>
/// <param name="Color">Optional CSS color override for the measure bar; otherwise a palette color is used.</param>
public readonly record struct BulletPoint(
    string Label,
    double Value,
    double Target,
    double Max = 100,
    IReadOnlyList<double>? Ranges = null,
    string? Color = null);

/// <summary>The row a click, keyboard selection, or hover interaction occurred on, for <see cref="DxBulletChart"/>.</summary>
/// <param name="Index">The row's index into the chart's <c>Points</c> list.</param>
/// <param name="Point">The row itself.</param>
public readonly record struct BulletPointEventArgs(int Index, BulletPoint Point);

/// <summary>
/// One box (and, with <see cref="DxBoxPlot.Violin"/>, one density silhouette) for
/// <see cref="DxBoxPlot"/>: a labeled group of raw samples. The five-number summary and outliers
/// are computed by the chart itself (<see cref="Primitives.Charts.BoxPlotStatistics"/>), off the
/// compute backend's sort — a caller supplies raw values, not pre-computed statistics.
/// </summary>
/// <param name="Label">The group's name.</param>
/// <param name="Values">The raw samples; NaN values are ignored.</param>
/// <param name="Color">Optional CSS color override; otherwise a palette color is used.</param>
public readonly record struct BoxPlotGroup(string Label, IReadOnlyList<double> Values, string? Color = null);
