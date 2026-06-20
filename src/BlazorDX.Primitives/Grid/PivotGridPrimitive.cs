using BlazorDX.Compute;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Tier 1 headless pivot table (cross-tab). Buckets rows by a row field and a
/// column field, then aggregates a numeric value field within each bucket using
/// the compute backend (Rust in the browser, managed C# on the server) — the same
/// kernels the data grid uses. Row, column, and grand totals are aggregated from
/// the underlying values (not from the cells), so means stay correct. Renders
/// nothing itself; a Tier 2 component supplies the table markup.
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public class PivotGridPrimitive<TRow> : ComponentBase
{
    private readonly Dictionary<(string Row, string Col), double> cells = new();
    private readonly Dictionary<string, double> rowTotals = new();
    private readonly Dictionary<string, double> colTotals = new();
    private double grandTotal = double.NaN;
    private object? lastItems;
    private (int Row, int Col, int Value, GridAggregateKind Kind) lastConfig = (-1, -1, -1, default);

    [Parameter, EditorRequired] public IReadOnlyList<TRow> Items { get; set; } = [];

    [Parameter, EditorRequired] public IGridRowAccessor<TRow> Accessor { get; set; } = default!;

    /// <summary>Column index whose distinct values become pivot rows.</summary>
    [Parameter] public int RowField { get; set; }

    /// <summary>Column index whose distinct values become pivot columns.</summary>
    [Parameter] public int ColumnField { get; set; } = 1;

    /// <summary>Numeric column index aggregated into the cells.</summary>
    [Parameter] public int ValueField { get; set; } = 2;

    [Parameter] public GridAggregateKind Aggregate { get; set; } = GridAggregateKind.Sum;

    [Inject] private IGridCompute Compute { get; set; } = default!;

    /// <summary>Active compute backend name, for diagnostics UI.</summary>
    protected string Backend => Compute.Backend;

    /// <summary>Distinct pivot-row keys, sorted.</summary>
    protected IReadOnlyList<string> RowKeys { get; private set; } = [];

    /// <summary>Distinct pivot-column keys, sorted.</summary>
    protected IReadOnlyList<string> ColumnKeys { get; private set; } = [];

    protected string RowFieldHeader => Header(RowField);

    protected string ColumnFieldHeader => Header(ColumnField);

    protected string ValueFieldHeader => Header(ValueField);

    private string Header(int column) =>
        column >= 0 && column < Accessor.Columns.Count ? Accessor.Columns[column].Header : string.Empty;

    /// <summary>The aggregated value of one cell, or NaN when the bucket is empty.</summary>
    protected double Cell(string rowKey, string colKey) =>
        cells.TryGetValue((rowKey, colKey), out double value) ? value : double.NaN;

    protected double RowTotal(string rowKey) =>
        rowTotals.TryGetValue(rowKey, out double value) ? value : double.NaN;

    protected double ColumnTotal(string colKey) =>
        colTotals.TryGetValue(colKey, out double value) ? value : double.NaN;

    protected double GrandTotal => grandTotal;

    protected override async Task OnParametersSetAsync()
    {
        (int, int, int, GridAggregateKind) config = (RowField, ColumnField, ValueField, Aggregate);
        if (ReferenceEquals(lastItems, Items) && lastConfig.Equals(config))
        {
            return;
        }

        lastItems = Items;
        lastConfig = (RowField, ColumnField, ValueField, Aggregate);
        await RecomputeAsync();
    }

    private async Task RecomputeAsync()
    {
        // Bucket row indices by (rowKey, colKey); also keep per-row, per-col, and
        // all-index lists so totals aggregate the raw values, not the cell results.
        Dictionary<(string, string), List<int>> byCell = new();
        Dictionary<string, List<int>> byRow = new();
        Dictionary<string, List<int>> byCol = new();
        SortedSet<string> rowSet = new(StringComparer.OrdinalIgnoreCase);
        SortedSet<string> colSet = new(StringComparer.OrdinalIgnoreCase);
        List<int> all = new(Items.Count);

        for (int i = 0; i < Items.Count; i++)
        {
            string rowKey = Accessor.GetCellText(Items[i], RowField);
            string colKey = Accessor.GetCellText(Items[i], ColumnField);
            rowSet.Add(rowKey);
            colSet.Add(colKey);
            Bucket(byCell, (rowKey, colKey), i);
            Bucket(byRow, rowKey, i);
            Bucket(byCol, colKey, i);
            all.Add(i);
        }

        RowKeys = rowSet.ToArray();
        ColumnKeys = colSet.ToArray();

        cells.Clear();
        rowTotals.Clear();
        colTotals.Clear();

        foreach (KeyValuePair<(string, string), List<int>> entry in byCell)
        {
            cells[entry.Key] = await AggregateAsync(entry.Value);
        }

        foreach (KeyValuePair<string, List<int>> entry in byRow)
        {
            rowTotals[entry.Key] = await AggregateAsync(entry.Value);
        }

        foreach (KeyValuePair<string, List<int>> entry in byCol)
        {
            colTotals[entry.Key] = await AggregateAsync(entry.Value);
        }

        grandTotal = all.Count > 0 ? await AggregateAsync(all) : double.NaN;
        StateHasChanged();
    }

    private async Task<double> AggregateAsync(List<int> indices)
    {
        double[] values = new double[indices.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Accessor.GetCellValue(Items[indices[i]], ValueField);
        }

        GridAggregate result = await Compute.AggregateAsync(values);
        return result.Value(Aggregate);
    }

    private static void Bucket<TKey>(Dictionary<TKey, List<int>> map, TKey key, int index)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out List<int>? list))
        {
            list = new List<int>();
            map[key] = list;
        }

        list.Add(index);
    }
}
