using BlazorDX.Components;

namespace BlazorDX.Demo.Client;

/// <summary>
/// A demo row type. The <c>[ChartRow]</c>/<c>[ChartValue]</c> attributes drive
/// BlazorDX.SourceGen, which emits <c>SalesRecordChartExtensions.ToChartPoints()</c> at build
/// time — a chart binds this existing domain data with zero reflection and no manual mapping.
/// </summary>
[ChartRow]
public sealed class SalesRecord
{
    [ChartValue(ChartField.Category)]
    public string Quarter { get; set; } = string.Empty;

    [ChartValue(ChartField.Y)]
    public double Revenue { get; set; }

    [ChartValue(ChartField.Series)]
    public string Region { get; set; } = string.Empty;
}
