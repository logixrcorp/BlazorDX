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
