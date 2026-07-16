using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

// A [ChartRow] domain type exercising every ChartField, including a non-string Category (Year, an
// int) to prove text fields accept any type, and a numeric field left off a non-numeric property
// (see SalesRow.Notes below) to prove that mapping is silently dropped rather than failing to compile.
[ChartRow]
public sealed class SalesRow
{
    [ChartValue(ChartField.Category)] public string Quarter { get; set; } = string.Empty;

    [ChartValue(ChartField.Y)] public double Revenue { get; set; }

    [ChartValue(ChartField.Series)] public string Region { get; set; } = string.Empty;

    [ChartValue(ChartField.Color)] public string? Accent { get; set; }

    // Not a numeric type: [ChartValue(ChartField.Y2)] here would be silently dropped by the
    // generator (asserted in Non_numeric_property_is_silently_not_mapped below).
    public string Notes { get; set; } = string.Empty;
}

// A minimal type proving a non-string property still works for a text field (stringified).
[ChartRow]
public sealed class YearlyRow
{
    [ChartValue(ChartField.Category)] public int Year { get; set; }

    [ChartValue(ChartField.Y)] public int Count { get; set; }
}

// Proves a non-numeric property tagged with a numeric field is dropped, not a compile error.
[ChartRow]
public sealed class MismappedRow
{
    [ChartValue(ChartField.Category)] public string Label { get; set; } = string.Empty;

    [ChartValue(ChartField.Y2)] public string NotNumeric { get; set; } = string.Empty;
}

/// <summary>
/// End-to-end coverage of the <c>[ChartRow]</c>/<c>[ChartValue]</c> source generator: the
/// generated <c>ToChartPoints()</c> extension is exercised through real usage (there is no
/// dedicated generator-snapshot test project — same policy <c>[GridRow]</c> already follows).
/// </summary>
public sealed class ChartRowGeneratorTests : TestContext
{
    [Fact]
    public void ToChartPoints_maps_each_ChartValue_tagged_property()
    {
        List<SalesRow> rows =
        [
            new() { Quarter = "Q1", Revenue = 100, Region = "West", Accent = "#2563eb", Notes = "n/a" },
            new() { Quarter = "Q2", Revenue = 150, Region = "East", Accent = null, Notes = "n/a" },
        ];

        IReadOnlyList<ChartPoint> points = rows.ToChartPoints();

        Assert.Equal(2, points.Count);
        Assert.Equal("Q1", points[0].Category);
        Assert.Equal(100, points[0].Y);
        Assert.Equal("West", points[0].Series);
        Assert.Equal("#2563eb", points[0].Color);
        Assert.Null(points[1].Color);
        // Fields nothing was mapped to keep ChartPoint's own defaults.
        Assert.Equal(0, points[0].X);
        Assert.Null(points[0].Y2);
    }

    [Fact]
    public void ToChartPoints_stringifies_a_non_string_category_property()
    {
        List<YearlyRow> rows = [new() { Year = 2026, Count = 42 }];

        IReadOnlyList<ChartPoint> points = rows.ToChartPoints();

        Assert.Equal("2026", Assert.Single(points).Category);
        Assert.Equal(42, points[0].Y);
    }

    [Fact]
    public void Non_numeric_property_is_silently_not_mapped()
    {
        List<MismappedRow> rows = [new() { Label = "A", NotNumeric = "not a number" }];

        IReadOnlyList<ChartPoint> points = rows.ToChartPoints();

        ChartPoint point = Assert.Single(points);
        Assert.Equal("A", point.Category);
        Assert.Null(point.Y2);   // dropped, not a compile error and not a runtime exception
    }

    [Fact]
    public void Generated_points_feed_a_real_chart_component()
    {
        // Proves the generated extension's output is exactly what DxBarChart already consumes -
        // no separate adapter step, matching the "one generic shape, not named variations" goal.
        List<SalesRow> rows = [new() { Quarter = "Q1", Revenue = 100, Region = "West" }];
        IReadOnlyList<ChartPoint> points = rows.ToChartPoints();

        IRenderedComponent<DxBarChart> chart = RenderComponent<DxBarChart>(p => p.Add(c => c.Points, points));

        Assert.Single(chart.FindAll("rect.dx-bar-rect"));
    }
}
