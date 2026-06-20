using BlazorDX.Primitives.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Server-side data binding for <see cref="DataGridPrimitive{TRow}"/>. Kept in its own
/// partial: when <see cref="DataSource"/> is set the grid enters <see cref="RemoteMode"/>,
/// where it no longer holds all rows. It fetches only the virtualization window, caches
/// loaded rows by absolute index, and pushes sort/filter down to the source. Grouping,
/// aggregates, in-memory selection, and inline edit are in-memory features and are
/// disabled in remote mode.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    private readonly Dictionary<int, TRow> remoteRows = new();
    private int remoteTotal;
    private int remoteVersion;   // bumped per refresh so stale fetches can be discarded
    private const int RemoteCacheCap = 5000;

    /// <summary>A server-side data source. When set, the grid binds remotely (see remarks on the type).</summary>
    [Parameter] public IGridDataSource<TRow>? DataSource { get; set; }

    /// <summary>Raised when a remote data fetch fails (after it is reported to diagnostics).</summary>
    [Parameter] public EventCallback<Exception> OnDataError { get; set; }

    // Optional observability sink — null (no-op) unless the app registers one.
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private IDxDiagnostics? Diagnostics => Services.GetService(typeof(IDxDiagnostics)) as IDxDiagnostics;

    /// <summary>Whether the grid is bound to a server-side <see cref="DataSource"/>.</summary>
    protected bool RemoteMode => DataSource is not null;

    /// <summary>The total row count reported by the data source (drives the scrollbar).</summary>
    protected int RemoteTotalCount => remoteTotal;

    /// <summary>Whether the row at an absolute index has been fetched into the cache.</summary>
    public bool IsRowLoaded(int index) =>
        RemoteGrouped || remoteRows.ContainsKey(index);   // server-group row slots are always materialized

    /// <summary>The cached row at an absolute index, or null if not yet loaded.</summary>
    protected TRow? RemoteRowAt(int index) => remoteRows.TryGetValue(index, out TRow? row) ? row : default;

    // Sets up remote state on parameter changes (called from OnParametersSet's remote branch).
    private void InitializeRemote()
    {
        if (columnOrder.Length != Columns.Count)
        {
            columnOrder = BuildIdentityOrder(Columns.Count);
        }

        if (columnWidths.Length != Columns.Count)
        {
            columnWidths = new double[Columns.Count];
        }

        visibleCount = EstimateVisibleCount(ViewportHeight);
    }

    /// <summary>
    /// Discards the cache and re-fetches from the start of the current window. Called when
    /// the sort or filters change so the server re-orders/re-filters the whole result.
    /// </summary>
    protected async Task RefreshRemoteAsync()
    {
        if (RemoteGrouped)
        {
            // Sort/filter changed: re-summarize groups server-side (rows refetch lazily on expand).
            await RefreshRemoteGroupsAsync();
            return;
        }

        remoteVersion++;
        remoteRows.Clear();
        await FetchWindowAsync(force: true);
    }

    /// <summary>Fetches the current virtualization window if any of its rows are missing.</summary>
    private async Task FetchWindowAsync(bool force = false)
    {
        if (DataSource is null)
        {
            return;
        }

        int first = Math.Max(0, firstVisibleIndex);
        int take = Math.Max(1, visibleCount);
        if (remoteTotal > 0)
        {
            take = Math.Min(take, Math.Max(0, remoteTotal - first));
        }

        if (take <= 0 || (!force && AllLoaded(first, take)))
        {
            return;
        }

        int version = remoteVersion;
        GridDataRequest request = new(first, take, BuildRemoteSort(), BuildRemoteFilters());

        GridDataPage<TRow> page;
        try
        {
            page = await DataSource.GetRowsAsync(request, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A remote fetch failed (network/server). Surface it instead of crashing the
            // render or swallowing it silently: report to diagnostics + raise OnDataError.
            Diagnostics.TryReportError("DxDataGrid.RemoteFetch", ex.Message, ex);
            if (OnDataError.HasDelegate)
            {
                await OnDataError.InvokeAsync(ex);
            }

            return;
        }

        // A newer refresh started while we awaited; this result is stale.
        if (version != remoteVersion)
        {
            return;
        }

        remoteTotal = page.TotalCount;
        if (remoteRows.Count > RemoteCacheCap)
        {
            remoteRows.Clear();   // crude bound; keeps memory flat on huge scrolls
        }

        for (int i = 0; i < page.Rows.Count; i++)
        {
            remoteRows[first + i] = page.Rows[i];
        }

        await InvokeAsync(StateHasChanged);
    }

    private bool AllLoaded(int first, int take)
    {
        for (int i = first; i < first + take; i++)
        {
            if (!remoteRows.ContainsKey(i))
            {
                return false;
            }
        }

        return true;
    }

    private GridSortKey[] BuildRemoteSort()
    {
        GridSortKey[] keys = new GridSortKey[sortKeys.Count];
        for (int i = 0; i < sortKeys.Count; i++)
        {
            (int column, bool descending) = sortKeys[i];
            keys[i] = new GridSortKey(column, Columns[column].Header, descending);
        }

        return keys;
    }

    private GridColumnFilter[] BuildRemoteFilters()
    {
        List<GridColumnFilter> filters = new(columnFilters.Count);
        foreach (KeyValuePair<int, string> entry in columnFilters)
        {
            filters.Add(new GridColumnFilter(entry.Key, Columns[entry.Key].Header, entry.Value));
        }

        return filters.ToArray();
    }
}
