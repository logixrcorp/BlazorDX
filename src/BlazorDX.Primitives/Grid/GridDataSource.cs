using BlazorDX.Compute;

namespace BlazorDX.Primitives.Grid;

/// <summary>A requested sort key: the column's index and header, and direction.</summary>
public readonly record struct GridSortKey(int Column, string Field, bool Descending);

/// <summary>A requested column text filter: the column's index and header, and the text.</summary>
public readonly record struct GridColumnFilter(int Column, string Field, string Text);

/// <summary>
/// A windowed data request the grid sends to an <see cref="IGridDataSource{TRow}"/>:
/// which slice of the (sorted, filtered) result to return, plus the active sort and
/// filters to apply server-side.
/// </summary>
public sealed record GridDataRequest(
    int Skip,
    int Take,
    IReadOnlyList<GridSortKey> Sort,
    IReadOnlyList<GridColumnFilter> Filters);

/// <summary>A page returned by a data source: the requested rows plus the total count.</summary>
/// <typeparam name="TRow">The row type.</typeparam>
public sealed record GridDataPage<TRow>(IReadOnlyList<TRow> Rows, int TotalCount);

/// <summary>
/// A server-side data source for <see cref="DataGridPrimitive{TRow}"/>. When supplied,
/// the grid stops holding all rows in memory and instead fetches the visible window,
/// pushing sorting and filtering down to this source. The implementation maps the
/// column indices/headers in the request to its own query (SQL, an API, LINQ, …).
/// </summary>
/// <typeparam name="TRow">The row type, bound through the generated accessor.</typeparam>
public interface IGridDataSource<TRow>
{
    /// <summary>Returns the requested window of rows and the total row count.</summary>
    Task<GridDataPage<TRow>> GetRowsAsync(GridDataRequest request, CancellationToken cancellationToken);
}

/// <summary>An aggregate the grid asks a data source to compute server-side for a group.</summary>
public readonly record struct GridAggregateRequest(int Column, string Field, GridAggregateKind Kind);

/// <summary>
/// A request for the distinct groups of a column, computed server-side: the column to
/// group by, the active filters to apply first, and the per-group aggregates to compute.
/// The data source typically answers with a single <c>GROUP BY</c> query.
/// </summary>
public sealed record GridGroupRequest(
    int GroupColumn,
    string GroupField,
    IReadOnlyList<GridColumnFilter> Filters,
    IReadOnlyList<GridAggregateRequest> Aggregations);

/// <summary>A server-computed aggregate value for one column within one group.</summary>
public readonly record struct GridGroupAggregateResult(int Column, GridAggregateKind Kind, double Value);

/// <summary>
/// One group as summarized by the server: the group key (the grouped column's text), how
/// many rows it holds, and any requested per-group aggregates. Rows themselves are not
/// returned here — the grid lazily fetches them via <c>GetRowsAsync</c> when the group is
/// expanded, scoping the request to this group's key.
/// </summary>
public sealed record GridGroupSummary(
    string Key,
    int Count,
    IReadOnlyList<GridGroupAggregateResult> Aggregates);

/// <summary>The set of groups for a grouped column, as summarized by a data source.</summary>
public sealed record GridGroupPage(IReadOnlyList<GridGroupSummary> Groups);

/// <summary>
/// A server-side data source that can also group. When a grid is bound to one of these and
/// a group column is chosen, the grid asks the server for group summaries (key + count +
/// aggregates) up front, then lazily fetches each group's rows through the inherited
/// <see cref="IGridDataSource{TRow}.GetRowsAsync"/> — with the group's key added as a
/// filter on the grouped column — only when that group is expanded. So grouping and
/// subtotals scale to data far larger than memory.
/// </summary>
/// <typeparam name="TRow">The row type, bound through the generated accessor.</typeparam>
public interface IGridGroupDataSource<TRow> : IGridDataSource<TRow>
{
    /// <summary>Returns the distinct groups of the requested column with their counts and aggregates.</summary>
    Task<GridGroupPage> GetGroupsAsync(GridGroupRequest request, CancellationToken cancellationToken);
}
