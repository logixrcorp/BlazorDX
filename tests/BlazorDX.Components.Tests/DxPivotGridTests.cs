using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Grid;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>A demo row for the pivot tests: City × Tier with a numeric value.</summary>
[GridRow]
public sealed class SaleRow
{
    [GridColumn("City", Order = 0)]
    public string City { get; set; } = string.Empty;

    [GridColumn("Tier", Order = 1)]
    public string Tier { get; set; } = string.Empty;

    [GridColumn("Amount", Order = 2)]
    public int Amount { get; set; }
}

/// <summary>Cross-tab bucketing + aggregation + totals (managed compute backend).</summary>
public sealed class DxPivotGridTests : TestContext
{
    public DxPivotGridTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
    }

    private static List<SaleRow> Sales() =>
    [
        new() { City = "Austin", Tier = "Free", Amount = 10 },
        new() { City = "Austin", Tier = "Pro", Amount = 20 },
        new() { City = "Austin", Tier = "Free", Amount = 5 },
        new() { City = "Berlin", Tier = "Pro", Amount = 100 },
    ];

    private IRenderedComponent<DxPivotGrid<SaleRow>> Render(GridAggregateKind agg = GridAggregateKind.Sum) =>
        RenderComponent<DxPivotGrid<SaleRow>>(parameters => parameters
            .Add(p => p.Items, Sales())
            .Add(p => p.Accessor, new SaleRowGridAccessor())
            .Add(p => p.RowField, 0)        // City
            .Add(p => p.ColumnField, 1)     // Tier
            .Add(p => p.ValueField, 2)      // Amount
            .Add(p => p.Aggregate, agg));

    [Fact]
    public void Renders_distinct_row_and_column_keys()
    {
        IRenderedComponent<DxPivotGrid<SaleRow>> pivot = Render();

        // 2 cities (rows) + a totals row.
        Assert.Equal(2, pivot.FindAll("tbody tr").Count);
        Assert.Contains("Austin", pivot.Markup);
        Assert.Contains("Berlin", pivot.Markup);
        // Column headers: Free, Pro, + Total.
        Assert.Contains("Free", pivot.Markup);
        Assert.Contains("Pro", pivot.Markup);
    }

    [Fact]
    public void Sum_aggregates_cells_correctly()
    {
        IRenderedComponent<DxPivotGrid<SaleRow>> pivot = Render();

        // Austin/Free = 10 + 5 = 15.
        var austinRow = pivot.FindAll("tbody tr")[0];
        var cells = austinRow.QuerySelectorAll(".dx-pivot-cell");
        Assert.Equal("15", cells[0].TextContent);   // Free
        Assert.Equal("20", cells[1].TextContent);   // Pro
        Assert.Equal("35", cells[2].TextContent);   // row total (15 + 20)
    }

    [Fact]
    public void Grand_total_sums_every_value()
    {
        IRenderedComponent<DxPivotGrid<SaleRow>> pivot = Render();

        // 10 + 20 + 5 + 100 = 135.
        Assert.Equal("135", pivot.Find(".dx-pivot-grand").TextContent);
    }

    [Fact]
    public void Mean_totals_aggregate_raw_values_not_cell_means()
    {
        IRenderedComponent<DxPivotGrid<SaleRow>> pivot = Render(GridAggregateKind.Mean);

        // Austin mean over raw 10, 20, 5 = 11.67 (not the mean of cell means).
        var austinRow = pivot.FindAll("tbody tr")[0];
        string rowTotal = austinRow.QuerySelectorAll(".dx-pivot-cell")[2].TextContent;
        Assert.Equal("11.67", rowTotal);
    }

    [Fact]
    public void Empty_cell_renders_blank()
    {
        IRenderedComponent<DxPivotGrid<SaleRow>> pivot = Render();

        // Berlin has no "Free" sales -> that cell is blank.
        var berlinRow = pivot.FindAll("tbody tr")[1];
        Assert.Equal(string.Empty, berlinRow.QuerySelectorAll(".dx-pivot-cell")[0].TextContent);
    }

    [Fact]
    public void Caption_names_the_aggregate_value_field_and_backend()
    {
        IRenderedComponent<DxPivotGrid<SaleRow>> pivot = Render();

        string caption = pivot.Find(".dx-pivot-caption").TextContent;
        Assert.Contains("Sum of Amount", caption);
        Assert.Contains("Managed C#", caption);
    }
}
