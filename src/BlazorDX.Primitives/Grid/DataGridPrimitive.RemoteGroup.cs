using BlazorDX.Compute;
using BlazorDX.Primitives.Diagnostics;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Server-side grouping for <see cref="DataGridPrimitive{TRow}"/>. When the bound
/// <see cref="DataSource"/> implements <see cref="IGridGroupDataSource{TRow}"/> and a
/// group column is chosen, the grid asks the server for group summaries (key + count +
/// aggregates) up front and renders collapsed headers, then lazily fetches each group's
/// rows — scoped by the group key — only when it is expanded. So grouping and subtotals
/// scale past memory. Kept in its own partial to hold the core grid file under the cap.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    // Server-provided group summaries (key, count, aggregates) for the grouped column.
    private List<GridGroupSummary> remoteGroups = [];

    // Materialized slot list (headers + rows of expanded groups), windowed for virtualization.
    private List<GridRowSlot> remoteGroupSlots = [];

    // Flat buffer of fetched rows; an expanded group's row slots index into this.
    private readonly List<TRow> remoteGroupRows = [];

    // Rows fetched per group key, so collapsing then re-expanding doesn't refetch.
    private readonly Dictionary<string, IReadOnlyList<TRow>> remoteGroupRowCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Group keys currently expanded (absent = collapsed; server groups default collapsed).
    private readonly HashSet<string> remoteGroupOpen = new(StringComparer.OrdinalIgnoreCase);

    // The group column the current summaries were fetched for (re-fetch when it changes).
    private int remoteGroupedColumn = -1;
    private bool remoteGroupsLoaded;

    // A group with more rows than this loads only its first page; the rest is logged, not dropped.
    private const int RemoteGroupRowCap = 1000;

    /// <summary>Whether the grid is grouping server-side (a grouping source + a chosen column).</summary>
    protected bool RemoteGrouped =>
        DataSource is IGridGroupDataSource<TRow> && GroupByColumn >= 0 && GroupByColumn < Columns.Count;

    /// <summary>The materialized server-group slots inside the current virtualization window.</summary>
    protected IEnumerable<GridRowSlot> VisibleRemoteGroupSlots()
    {
        int last = Math.Min(remoteGroupSlots.Count, firstVisibleIndex + visibleCount);
        for (int position = firstVisibleIndex; position < last; position++)
        {
            yield return remoteGroupSlots[position];
        }
    }

    /// <summary>Resolves a server-group data-row slot to its fetched row.</summary>
    protected TRow RemoteGroupRowAt(int bufferIndex) => remoteGroupRows[bufferIndex];

    /// <summary>The group key (grouped column text) of a server group.</summary>
    protected string RemoteGroupKey(int groupIndex) => remoteGroups[groupIndex].Key;

    /// <summary>The server-reported row count of a group.</summary>
    protected int RemoteGroupCount(int groupIndex) => remoteGroups[groupIndex].Count;

    /// <summary>Whether a server group is expanded.</summary>
    protected bool RemoteGroupExpanded(int groupIndex) =>
        remoteGroupOpen.Contains(remoteGroups[groupIndex].Key);

    /// <summary>A server-computed subtotal for a column within a group (empty when none).</summary>
    protected GridAggregate RemoteGroupAggregate(int groupIndex, int columnIndex)
    {
        foreach (GridGroupAggregateResult aggregate in remoteGroups[groupIndex].Aggregates)
        {
            if (aggregate.Column == columnIndex)
            {
                // The server returns the single statistic the grid asked for; expose it in the
                // matching slot of a GridAggregate so the styled formatter can render it.
                return AggregateFromKind(aggregate.Kind, aggregate.Value, remoteGroups[groupIndex].Count);
            }
        }

        return GridAggregate.Empty;
    }

    private static GridAggregate AggregateFromKind(GridAggregateKind kind, double value, int count) => kind switch
    {
        GridAggregateKind.Sum => new GridAggregate(count, value, 0, 0, 0),
        GridAggregateKind.Mean => new GridAggregate(count, 0, 0, 0, value),
        GridAggregateKind.Min => new GridAggregate(count, 0, value, 0, 0),
        GridAggregateKind.Max => new GridAggregate(count, 0, 0, value, 0),
        _ => new GridAggregate(count, 0, 0, 0, 0),
    };

    // Fetches the group summaries for the current column, filters, and aggregates.
    private async Task RefreshRemoteGroupsAsync()
    {
        if (DataSource is not IGridGroupDataSource<TRow> source)
        {
            return;
        }

        remoteVersion++;
        int version = remoteVersion;
        GridGroupRequest request = new(
            GroupByColumn, Columns[GroupByColumn].Header, BuildRemoteFilters(), BuildRemoteAggregations());

        GridGroupPage page;
        try
        {
            page = await source.GetGroupsAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Diagnostics.TryReportError("DxDataGrid.RemoteGroups", ex.Message, ex);
            if (OnDataError.HasDelegate)
            {
                await OnDataError.InvokeAsync(ex);
            }

            return;
        }

        if (version != remoteVersion)
        {
            return;   // a newer refresh superseded this one
        }

        remoteGroups = [.. page.Groups];
        remoteGroupedColumn = GroupByColumn;
        remoteGroupsLoaded = true;

        // Sort/filter changed → previously-fetched rows may be stale; drop them.
        remoteGroupRowCache.Clear();
        remoteGroupRows.Clear();

        int total = 0;
        foreach (GridGroupSummary group in remoteGroups)
        {
            total += group.Count;
        }

        remoteTotal = total;
        RebuildRemoteGroupSlots();
        await InvokeAsync(StateHasChanged);
    }

    private GridAggregateRequest[] BuildRemoteAggregations()
    {
        if (Aggregations is not { Count: > 0 })
        {
            return [];
        }

        List<GridAggregateRequest> requests = new(Aggregations.Count);
        foreach (KeyValuePair<int, GridAggregateKind> entry in Aggregations)
        {
            int column = entry.Key;
            if (column >= 0 && column < Columns.Count && Columns[column].IsNumeric)
            {
                requests.Add(new GridAggregateRequest(column, Columns[column].Header, entry.Value));
            }
        }

        return requests.ToArray();
    }

    // Rebuilds the slot list: a header per group, plus the rows of each expanded group.
    private void RebuildRemoteGroupSlots()
    {
        List<GridRowSlot> rebuilt = new(remoteGroups.Count);
        remoteGroupRows.Clear();

        for (int groupIndex = 0; groupIndex < remoteGroups.Count; groupIndex++)
        {
            GridGroupSummary group = remoteGroups[groupIndex];
            rebuilt.Add(new GridRowSlot(true, groupIndex, -1));

            if (remoteGroupOpen.Contains(group.Key)
                && remoteGroupRowCache.TryGetValue(group.Key, out IReadOnlyList<TRow>? rows))
            {
                foreach (TRow row in rows)
                {
                    rebuilt.Add(new GridRowSlot(false, groupIndex, remoteGroupRows.Count));
                    remoteGroupRows.Add(row);
                }
            }
        }

        remoteGroupSlots = rebuilt;
    }

    /// <summary>Expands or collapses a server group, lazily fetching its rows on first expand.</summary>
    protected async Task ToggleRemoteGroupAsync(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= remoteGroups.Count)
        {
            return;
        }

        GridGroupSummary group = remoteGroups[groupIndex];
        if (!remoteGroupOpen.Add(group.Key))
        {
            remoteGroupOpen.Remove(group.Key);   // was open → collapse
            RebuildRemoteGroupSlots();
            StateHasChanged();
            return;
        }

        if (!remoteGroupRowCache.ContainsKey(group.Key))
        {
            await FetchRemoteGroupRowsAsync(group);
        }

        RebuildRemoteGroupSlots();
        StateHasChanged();
    }

    // Fetches an expanded group's rows via GetRowsAsync, scoping by the group key.
    private async Task FetchRemoteGroupRowsAsync(GridGroupSummary group)
    {
        if (DataSource is null)
        {
            return;
        }

        int take = Math.Min(group.Count, RemoteGroupRowCap);
        if (group.Count > RemoteGroupRowCap)
        {
            // Don't silently drop the tail: report what we capped so it's visible, not hidden.
            Diagnostics.TryReportWarning(
                "DxDataGrid.RemoteGroups",
                $"Group '{group.Key}' has {group.Count} rows; loaded the first {RemoteGroupRowCap}.");
        }

        // Pin the request to this group by adding its key as a filter on the grouped column.
        List<GridColumnFilter> filters = new(BuildRemoteFilters())
        {
            new GridColumnFilter(GroupByColumn, Columns[GroupByColumn].Header, group.Key),
        };
        GridDataRequest request = new(0, Math.Max(1, take), BuildRemoteSort(), filters);

        try
        {
            GridDataPage<TRow> page = await DataSource.GetRowsAsync(request, CancellationToken.None);
            remoteGroupRowCache[group.Key] = page.Rows;
        }
        catch (Exception ex)
        {
            Diagnostics.TryReportError("DxDataGrid.RemoteGroups", ex.Message, ex);
            if (OnDataError.HasDelegate)
            {
                await OnDataError.InvokeAsync(ex);
            }
        }
    }
}
