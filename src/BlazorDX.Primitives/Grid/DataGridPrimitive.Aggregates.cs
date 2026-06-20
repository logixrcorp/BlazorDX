using BlazorDX.Compute;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Footer and per-group aggregates for <see cref="DataGridPrimitive{TRow}"/>. Kept in
/// its own partial so the core grid file stays under the 1000-line cap (this is the
/// aggregation concern). Aggregates are computed over the in-memory <c>Items</c> via
/// the compute backend; they are not available in remote mode.
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    private async Task ComputeAggregatesAsync()
    {
        Dictionary<int, GridAggregate> computed = new();
        foreach (KeyValuePair<int, GridAggregateKind> entry in Aggregations!)
        {
            int column = entry.Key;
            if (column < 0 || column >= Columns.Count || !Columns[column].IsNumeric)
            {
                continue;
            }

            double[] values = new double[Items.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Accessor.GetCellValue(Items[i], column);
            }

            computed[column] = await Compute.AggregateAsync(values);
        }

        aggregates = computed;
        StateHasChanged();
    }

    /// <summary>Whether an aggregate footer should render (grand totals; in-memory only).</summary>
    protected bool HasAggregations => !RemoteMode && Aggregations is { Count: > 0 };

    /// <summary>
    /// Whether per-group subtotals should render in group headers. True for in-memory
    /// grouping and for server-side grouping (where the server computes them).
    /// </summary>
    protected bool ShowGroupAggregates => (HasAggregations || RemoteGrouped) && Aggregations is { Count: > 0 };

    /// <summary>The aggregate statistic configured for a column, or null.</summary>
    protected GridAggregateKind? AggregationKind(int columnIndex) =>
        Aggregations is not null && Aggregations.TryGetValue(columnIndex, out GridAggregateKind kind)
            ? kind
            : null;

    /// <summary>The computed aggregate for a column (empty until computed).</summary>
    protected GridAggregate AggregateFor(int columnIndex) =>
        aggregates.TryGetValue(columnIndex, out GridAggregate value) ? value : GridAggregate.Empty;

    /// <summary>The computed subtotal for one column within one group (in-memory or server).</summary>
    protected GridAggregate GroupAggregate(int groupIndex, int columnIndex) =>
        RemoteGrouped
            ? RemoteGroupAggregate(groupIndex, columnIndex)
            : groupAggregates.TryGetValue(groups[groupIndex].Key, out Dictionary<int, GridAggregate>? perColumn)
              && perColumn.TryGetValue(columnIndex, out GridAggregate value)
                ? value
                : GridAggregate.Empty;

    private async Task ComputeGroupAggregatesAsync()
    {
        Dictionary<string, Dictionary<int, GridAggregate>> computed = new(StringComparer.OrdinalIgnoreCase);
        foreach (Group group in groups)
        {
            Dictionary<int, GridAggregate> perColumn = new();
            foreach (KeyValuePair<int, GridAggregateKind> entry in Aggregations!)
            {
                int column = entry.Key;
                if (column < 0 || column >= Columns.Count || !Columns[column].IsNumeric)
                {
                    continue;
                }

                double[] values = new double[group.RowIndices.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = Accessor.GetCellValue(Items[group.RowIndices[i]], column);
                }

                perColumn[column] = await Compute.AggregateAsync(values);
            }

            computed[group.Key] = perColumn;
        }

        groupAggregates = computed;
        StateHasChanged();
    }
}
