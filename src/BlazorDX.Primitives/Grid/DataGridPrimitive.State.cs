using System.Linq;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Layout persistence for <see cref="DataGridPrimitive{TRow}"/>: capture the user's
/// column order / widths / hidden columns / sort / filters as a serializable
/// <see cref="GridState"/>, and restore it later. <see cref="OnStateChanged"/> fires on
/// every layout mutation so a host can auto-save (e.g. to localStorage or a profile).
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    /// <summary>Raised after any layout change (reorder, resize, hide, sort, filter) with the new state.</summary>
    [Parameter] public EventCallback<GridState> OnStateChanged { get; set; }

    /// <summary>Captures the current layout for persistence.</summary>
    public GridState CaptureState() => new()
    {
        ColumnOrder = columnOrder.ToArray(),
        ColumnWidths = columnWidths.ToArray(),
        HiddenColumns = hiddenColumns.OrderBy(static c => c).ToArray(),
        Sort = sortKeys.Select(static k => new GridSortState(k.Column, k.Descending)).ToArray(),
        Filters = columnFilters.OrderBy(static f => f.Key)
            .Select(static f => new GridFilterState(f.Key, f.Value)).ToArray(),
    };

    /// <summary>
    /// Restores a captured layout. Entries that don't fit the current columns (wrong
    /// count, out-of-range indices, a non-permutation order) are ignored, so stale saved
    /// state can never corrupt the grid.
    /// </summary>
    public async Task ApplyStateAsync(GridState state)
    {
        int columns = Columns.Count;

        if (state.ColumnOrder.Count == columns && IsPermutation(state.ColumnOrder, columns))
        {
            columnOrder = state.ColumnOrder.ToArray();
        }

        if (state.ColumnWidths.Count == columns)
        {
            columnWidths = state.ColumnWidths.ToArray();
        }

        hiddenColumns.Clear();
        foreach (int column in state.HiddenColumns)
        {
            if (column >= 0 && column < columns)
            {
                hiddenColumns.Add(column);
            }
        }

        sortKeys.Clear();
        foreach (GridSortState sort in state.Sort)
        {
            if (sort.Column >= 0 && sort.Column < columns)
            {
                sortKeys.Add((sort.Column, sort.Descending));
            }
        }

        columnFilters.Clear();
        foreach (GridFilterState filter in state.Filters)
        {
            if (filter.Column >= 0 && filter.Column < columns && !string.IsNullOrEmpty(filter.Text))
            {
                columnFilters[filter.Column] = filter.Text;
            }
        }

        if (RemoteMode)
        {
            await RefreshRemoteAsync();
        }
        else
        {
            await ApplySortAsync();
            RebuildSlots();
        }

        StateHasChanged();
    }

    // Fired from each layout mutator so hosts can persist without polling.
    private async Task NotifyStateChangedAsync()
    {
        if (OnStateChanged.HasDelegate)
        {
            await OnStateChanged.InvokeAsync(CaptureState());
        }
    }

    private static bool IsPermutation(IReadOnlyList<int> order, int count)
    {
        bool[] seen = new bool[count];
        foreach (int index in order)
        {
            if (index < 0 || index >= count || seen[index])
            {
                return false;
            }

            seen[index] = true;
        }

        return true;
    }
}
