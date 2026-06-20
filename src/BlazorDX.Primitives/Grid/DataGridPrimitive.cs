using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Interaction;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Tier 1 headless data grid: owns row ordering, grouping, the virtualization
/// window, sorting, aggregates, and ARIA wiring, but renders nothing on its own.
/// A Tier 2 component (or a custom subclass) supplies the markup by overriding
/// <see cref="Microsoft.AspNetCore.Components.ComponentBase.BuildRenderTree"/> and
/// reading the protected state below.
///
/// Rendering is driven by a flat list of <see cref="GridRowSlot"/> (group headers
/// interleaved with data rows), so one windowing implementation serves both the
/// grouped and ungrouped views.
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public partial class DataGridPrimitive<TRow> : ComponentBase, IAsyncDisposable
{
    private int[] rowOrder = [];
    private int[] columnOrder = [];
    private readonly List<(int Column, bool Descending)> sortKeys = new();
    private readonly HashSet<int> hiddenColumns = new();
    private double[] columnWidths = [];
    private int resizingColumn = -1;
    private double resizeStartX;
    private double resizeStartWidth;
    private int firstVisibleIndex;
    private int visibleCount;
    private bool scrollSubscribed;
    private Dictionary<int, GridAggregate> aggregates = new();
    private object? aggregatedItems;

    private List<GridRowSlot> slots = [];
    private List<Group> groups = [];
    private readonly Dictionary<int, string> columnFilters = new();
    private readonly HashSet<int> expandedRows = new();
    private readonly Dictionary<int, HashSet<string>> excludedValues = new();
    private readonly Dictionary<int, IReadOnlyList<string>> distinctCache = new();
    private object? distinctItems;
    private readonly HashSet<int> selectedRows = new();
    private readonly Dictionary<string, bool> groupExpanded = new();
    private Dictionary<string, Dictionary<int, GridAggregate>> groupAggregates = new();
    private object? groupAggregatedItems;
    private int groupAggregatedColumn = -2;

    /// <summary>
    /// The in-memory rows. Provide this for client-side data, or set
    /// <see cref="DataSource"/> for server-side (remote) binding instead.
    /// </summary>
    [Parameter] public IReadOnlyList<TRow> Items { get; set; } = [];

    /// <summary>The generated, reflection-free accessor for <typeparamref name="TRow"/>.</summary>
    [Parameter, EditorRequired] public IGridRowAccessor<TRow> Accessor { get; set; } = default!;

    /// <summary>Fixed row height in pixels; virtualization math depends on it.</summary>
    [Parameter] public int RowHeight { get; set; } = 32;

    /// <summary>Visible container height in pixels.</summary>
    [Parameter] public int ViewportHeight { get; set; } = 480;

    /// <summary>Extra rows rendered above and below the viewport to cover fast scrolls.</summary>
    [Parameter] public int Overscan { get; set; } = 8;

    /// <summary>Column index to group rows by, or -1 for no grouping.</summary>
    [Parameter] public int GroupByColumn { get; set; } = -1;

    /// <summary>Optional per-column aggregates to show in a footer (column index → statistic).</summary>
    [Parameter] public IReadOnlyDictionary<int, GridAggregateKind>? Aggregations { get; set; }

    /// <summary>When true, a selection checkbox column is shown and rows can be selected.</summary>
    [Parameter] public bool Selectable { get; set; }

    /// <summary>Raised with the selected rows (in row order) whenever the selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TRow>> SelectionChanged { get; set; }

    /// <summary>When true, a per-column filter row is shown beneath the header.</summary>
    [Parameter] public bool Filterable { get; set; }

    /// <summary>Number of leading display columns frozen to the left while scrolling horizontally.</summary>
    [Parameter] public int PinnedColumns { get; set; }

    /// <summary>When true, an "Export CSV" action is available (the styled layer renders the button).</summary>
    [Parameter] public bool ShowExport { get; set; }

    /// <summary>File name used for the CSV export.</summary>
    [Parameter] public string ExportFileName { get; set; } = "grid.csv";

    /// <summary>When true, an "Export Excel" action produces a real .xlsx workbook.</summary>
    [Parameter] public bool ShowExcelExport { get; set; }

    /// <summary>File name used for the Excel (.xlsx) export.</summary>
    [Parameter] public string ExcelFileName { get; set; } = "grid.xlsx";

    /// <summary>When true, an "Export PDF" action produces a paginated table PDF.</summary>
    [Parameter] public bool ShowPdfExport { get; set; }

    /// <summary>File name used for the PDF export.</summary>
    [Parameter] public string PdfFileName { get; set; } = "grid.pdf";

    /// <summary>When true, a column chooser (show/hide columns) is available.</summary>
    [Parameter] public bool ShowColumnChooser { get; set; }

    /// <summary>When true, a "Copy" action copies the selected rows (or all visible) as TSV.</summary>
    [Parameter] public bool ShowClipboard { get; set; }

    /// <summary>When true, each header offers an Excel-style distinct-value filter menu.</summary>
    [Parameter] public bool ShowFilterMenu { get; set; }

    /// <summary>
    /// Optional expandable detail panel rendered beneath a row. When set, each data
    /// row gets an expand twisty and its detail is shown when expanded.
    /// </summary>
    [Parameter] public RenderFragment<TRow>? DetailTemplate { get; set; }

    /// <summary>When true, editable cells can be edited in place (double-click).</summary>
    [Parameter] public bool Editable { get; set; }

    /// <summary>Raised with the row after an inline edit is committed.</summary>
    [Parameter] public EventCallback<TRow> RowEdited { get; set; }

    [Inject] private IGridCompute Compute { get; set; } = default!;
    [Inject] private IGridDomInterop Dom { get; set; } = default!;

    /// <summary>The columns to render, in display order.</summary>
    protected IReadOnlyList<GridColumnInfo> Columns => Accessor.Columns;

    /// <summary>Stable element id for the scroll container (drives DOM measurement).</summary>
    protected string ContainerId { get; } = $"dx-grid-{Guid.NewGuid():N}";

    /// <summary>Total data rows (for aria-rowcount).</summary>
    protected int TotalRows => RemoteMode ? remoteTotal : Items.Count;

    /// <summary>Total display slots (group headers + data rows) driving virtualization.</summary>
    protected int SlotCount => RemoteGrouped ? remoteGroupSlots.Count : RemoteMode ? remoteTotal : slots.Count;

    protected bool IsGrouped => !RemoteMode && GroupByColumn >= 0 && GroupByColumn < Columns.Count;

    /// <summary>Header text of the grouped column, when grouping (in-memory or server) is active.</summary>
    protected string GroupColumnHeader =>
        IsGrouped || RemoteGrouped ? Columns[GroupByColumn].Header : string.Empty;

    /// <summary>Pixel height of the spacer above the rendered window.</summary>
    protected double TopPadding => (double)firstVisibleIndex * RowHeight;

    /// <summary>Pixel height of the spacer below the rendered window.</summary>
    protected double BottomPadding => Math.Max(0, (double)(SlotCount - LastVisibleIndex) * RowHeight);

    /// <summary>The primary sort column (first key), or -1 when unsorted.</summary>
    protected int SortColumn => sortKeys.Count > 0 ? sortKeys[0].Column : -1;

    /// <summary>Whether the primary sort key is descending.</summary>
    protected bool SortDescending => sortKeys.Count > 0 && sortKeys[0].Descending;

    /// <summary>
    /// The sort state of a column: whether it is a sort key, its direction, and its
    /// 1-based position in the multi-column sort order (0 when not sorted).
    /// </summary>
    protected (bool Found, bool Descending, int Order) SortStateOf(int actualColumn)
    {
        int index = sortKeys.FindIndex(k => k.Column == actualColumn);
        return index < 0 ? (false, false, 0) : (true, sortKeys[index].Descending, index + 1);
    }

    /// <summary>Number of active sort keys (for showing order badges only when &gt; 1).</summary>
    protected int SortKeyCount => sortKeys.Count;

    private int LastVisibleIndex => Math.Min(SlotCount, firstVisibleIndex + visibleCount);

    protected override void OnParametersSet()
    {
        if (RemoteMode)
        {
            InitializeRemote();
            ClampActiveCell();
            return;
        }

        // Reset ordering to identity whenever the data set identity changes size.
        if (rowOrder.Length != Items.Count)
        {
            rowOrder = BuildIdentityOrder(Items.Count);
            sortKeys.Clear();
            selectedRows.Clear();   // row indices would be stale against a new data set
            expandedRows.Clear();
            columnFilters.Clear();
            excludedValues.Clear();
            distinctCache.Clear();
        }

        if (columnOrder.Length != Columns.Count)
        {
            columnOrder = BuildIdentityOrder(Columns.Count);
        }

        if (columnWidths.Length != Columns.Count)
        {
            columnWidths = new double[Columns.Count];   // 0 = auto (1fr)
        }

        visibleCount = EstimateVisibleCount(ViewportHeight);
        RebuildSlots();
        ClampActiveCell();
    }

    /// <summary>The actual column index shown at each display position (left to right).</summary>
    protected IReadOnlyList<int> ColumnOrder => columnOrder;

    /// <summary>Moves a column from one display position to another (drag or keyboard reorder).</summary>
    protected void MoveColumn(int fromDisplay, int toDisplay)
    {
        if (fromDisplay < 0 || toDisplay < 0
            || fromDisplay >= columnOrder.Length || toDisplay >= columnOrder.Length
            || fromDisplay == toDisplay)
        {
            return;
        }

        columnOrder = ListReorder.Move(columnOrder, fromDisplay, toDisplay).ToArray();
        StateHasChanged();
        _ = NotifyStateChangedAsync();
    }

    // ---- Column resize ----

    private const double MinColumnWidth = 60;
    private const double DefaultColumnWidth = 150;

    /// <summary>Explicit pixel width of a column, or 0 when it auto-sizes (1fr).</summary>
    protected double ColumnWidth(int actualColumn) =>
        actualColumn >= 0 && actualColumn < columnWidths.Length ? columnWidths[actualColumn] : 0;

    /// <summary>True while a column is being dragged to a new width.</summary>
    protected bool IsResizing => resizingColumn >= 0;

    /// <summary>Begins a resize gesture for a column at the pointer's X position.</summary>
    protected void StartColumnResize(int actualColumn, double clientX)
    {
        if (actualColumn < 0 || actualColumn >= columnWidths.Length)
        {
            return;
        }

        resizingColumn = actualColumn;
        resizeStartX = clientX;
        // Seed from the current width; auto columns start at a sensible default.
        resizeStartWidth = columnWidths[actualColumn] > 0 ? columnWidths[actualColumn] : DefaultColumnWidth;
        StateHasChanged();
    }

    /// <summary>Updates the resizing column's width as the pointer moves.</summary>
    protected void ResizeColumnTo(double clientX)
    {
        if (resizingColumn < 0)
        {
            return;
        }

        columnWidths[resizingColumn] = Math.Max(MinColumnWidth, resizeStartWidth + (clientX - resizeStartX));
        StateHasChanged();
    }

    /// <summary>Ends the resize gesture.</summary>
    protected void EndColumnResize()
    {
        resizingColumn = -1;
        StateHasChanged();
        _ = NotifyStateChangedAsync();
    }

    // ---- Pinned (frozen) columns ----

    /// <summary>Width pinned columns occupy when frozen (resized width, or a fixed default).</summary>
    private const double SelectionColumnWidth = 36;

    /// <summary>Whether any columns are pinned (drives horizontal-scroll behavior).</summary>
    protected bool HasPinnedColumns => PinnedColumns > 0;

    /// <summary>Whether the column at a display position is frozen.</summary>
    protected bool IsPinned(int displayPosition) => displayPosition < PinnedColumns;

    /// <summary>Whether a display position is the last frozen column (gets the divider shadow).</summary>
    protected bool IsLastPinned(int displayPosition) => displayPosition == PinnedColumns - 1;

    /// <summary>The sticky left offset (px) for a frozen column or the selection cell.</summary>
    protected double PinnedLeft(int displayPosition)
    {
        double left = Selectable ? SelectionColumnWidth : 0;
        for (int i = 0; i < displayPosition && i < PinnedColumns; i++)
        {
            if (!IsColumnHidden(ColumnOrder[i]))   // a hidden column occupies no track
            {
                left += PinnedColumnWidth(ColumnOrder[i]);
            }
        }

        return left;
    }

    /// <summary>A pinned column needs a definite width; falls back to a fixed default when auto.</summary>
    protected double PinnedColumnWidth(int actualColumn)
    {
        double width = ColumnWidth(actualColumn);
        return width > 0 ? width : DefaultColumnWidth;
    }

    // ---- Column chooser (show/hide) ----

    /// <summary>Whether a column is hidden by the chooser.</summary>
    protected bool IsColumnHidden(int actualColumn) => hiddenColumns.Contains(actualColumn);

    /// <summary>Count of currently-visible columns.</summary>
    protected int VisibleColumnCount => Columns.Count - hiddenColumns.Count;

    /// <summary>Shows or hides a column (by its actual index).</summary>
    protected void ToggleColumnVisibility(int actualColumn)
    {
        if (!hiddenColumns.Remove(actualColumn))
        {
            hiddenColumns.Add(actualColumn);
        }

        StateHasChanged();
        _ = NotifyStateChangedAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (RemoteMode)
        {
            // Server-side grouping: (re)fetch group summaries when the group column changes
            // or it's the first time. Flat remote mode has nothing to precompute here.
            if (RemoteGrouped && (!remoteGroupsLoaded || remoteGroupedColumn != GroupByColumn))
            {
                await RefreshRemoteGroupsAsync();
            }

            return;
        }

        // Recompute aggregates only when the data set changes, not on every render.
        if (Aggregations is { Count: > 0 } && !ReferenceEquals(aggregatedItems, Items))
        {
            aggregatedItems = Items;
            await ComputeAggregatesAsync();
        }

        // Per-group subtotals depend on the group column and data, not on sort or
        // expand state (aggregation is order-independent), so recompute narrowly.
        if (IsGrouped && Aggregations is { Count: > 0 }
            && (!ReferenceEquals(groupAggregatedItems, Items) || groupAggregatedColumn != GroupByColumn))
        {
            groupAggregatedItems = Items;
            groupAggregatedColumn = GroupByColumn;
            await ComputeGroupAggregatesAsync();
        }
    }

    /// <summary>The display slots currently inside the virtualization window.</summary>
    protected IEnumerable<GridRowSlot> VisibleSlots()
    {
        if (RemoteGrouped)
        {
            return VisibleRemoteGroupSlots();
        }

        return VisibleFlatSlots();
    }

    private IEnumerable<GridRowSlot> VisibleFlatSlots()
    {
        if (RemoteMode)
        {
            // No materialized slot list; the window's absolute indices are the rows.
            for (int index = firstVisibleIndex; index < LastVisibleIndex; index++)
            {
                yield return new GridRowSlot(false, -1, index);
            }

            yield break;
        }

        for (int position = firstVisibleIndex; position < LastVisibleIndex; position++)
        {
            yield return slots[position];
        }
    }

    protected TRow RowAt(int rowIndex) =>
        RemoteGrouped ? RemoteGroupRowAt(rowIndex) : RemoteMode ? remoteRows[rowIndex] : Items[rowIndex];

    protected string CellText(TRow row, int columnIndex) => Accessor.GetCellText(row, columnIndex);

    protected string GroupKey(int groupIndex) =>
        RemoteGrouped ? RemoteGroupKey(groupIndex) : groups[groupIndex].Key;

    protected int GroupRowCount(int groupIndex) =>
        RemoteGrouped ? RemoteGroupCount(groupIndex) : groups[groupIndex].RowIndices.Count;

    protected bool GroupExpanded(int groupIndex) =>
        RemoteGrouped ? RemoteGroupExpanded(groupIndex) : IsExpanded(groups[groupIndex].Key);

    /// <summary>Toggles a group's expanded state and rebuilds the display list.</summary>
    protected async Task ToggleGroup(int groupIndex)
    {
        if (RemoteGrouped)
        {
            await ToggleRemoteGroupAsync(groupIndex);
            return;
        }

        if (groupIndex < 0 || groupIndex >= groups.Count)
        {
            return;
        }

        string key = groups[groupIndex].Key;
        groupExpanded[key] = !IsExpanded(key);
        RebuildSlots();
        StateHasChanged();
    }

    // ---- Filtering ----

    /// <summary>The current filter text for a column (empty when unfiltered).</summary>
    protected string ColumnFilter(int columnIndex) =>
        columnFilters.TryGetValue(columnIndex, out string? text) ? text : string.Empty;

    /// <summary>Whether any column filter is active.</summary>
    protected bool HasActiveFilters => columnFilters.Count > 0;

    /// <summary>Number of data rows currently passing the filters.</summary>
    protected int VisibleRowCount => FilteredOrder().Length;

    /// <summary>Sets (or clears) the contains-filter for a column and rebuilds the view.</summary>
    protected void SetFilter(int columnIndex, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            columnFilters.Remove(columnIndex);
        }
        else
        {
            columnFilters[columnIndex] = text;
        }

        _ = NotifyStateChangedAsync();

        if (RemoteMode)
        {
            _ = RefreshRemoteAsync();   // the server re-filters the whole result
            return;
        }

        RebuildSlots();
        StateHasChanged();
    }

    // ---- Excel-style value filter ----

    /// <summary>The distinct values of a column (sorted, capped), for the filter menu.</summary>
    protected IReadOnlyList<string> DistinctValues(int actualColumn)
    {
        if (!ReferenceEquals(distinctItems, Items))
        {
            distinctItems = Items;
            distinctCache.Clear();
        }

        if (distinctCache.TryGetValue(actualColumn, out IReadOnlyList<string>? cached))
        {
            return cached;
        }

        const int cap = 500;   // keep the menu bounded on high-cardinality columns
        SortedSet<string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (TRow row in Items)
        {
            values.Add(Accessor.GetCellText(row, actualColumn));
            if (values.Count >= cap)
            {
                break;
            }
        }

        string[] list = values.ToArray();
        distinctCache[actualColumn] = list;
        return list;
    }

    /// <summary>Whether a value is currently included (checked) for a column.</summary>
    protected bool IsValueChecked(int actualColumn, string value) =>
        !(excludedValues.TryGetValue(actualColumn, out HashSet<string>? excluded) && excluded.Contains(value));

    /// <summary>Whether a column has an active value filter.</summary>
    protected bool HasValueFilter(int actualColumn) =>
        excludedValues.TryGetValue(actualColumn, out HashSet<string>? excluded) && excluded.Count > 0;

    /// <summary>Includes or excludes a value from a column's filter and rebuilds the view.</summary>
    protected void ToggleValueFilter(int actualColumn, string value)
    {
        if (!excludedValues.TryGetValue(actualColumn, out HashSet<string>? excluded))
        {
            excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            excludedValues[actualColumn] = excluded;
        }

        if (!excluded.Remove(value))
        {
            excluded.Add(value);
        }

        RebuildSlots();
        StateHasChanged();
    }

    /// <summary>Clears a column's value filter.</summary>
    protected void ClearValueFilter(int actualColumn)
    {
        if (excludedValues.Remove(actualColumn))
        {
            RebuildSlots();
            StateHasChanged();
        }
    }

    // The current row order narrowed to rows matching every active filter.
    private int[] FilteredOrder()
    {
        if (columnFilters.Count == 0 && excludedValues.Count == 0)
        {
            return rowOrder;
        }

        List<int> kept = new(rowOrder.Length);
        foreach (int rowIndex in rowOrder)
        {
            if (RowMatchesFilters(rowIndex))
            {
                kept.Add(rowIndex);
            }
        }

        return kept.ToArray();
    }

    private bool RowMatchesFilters(int rowIndex)
    {
        foreach (KeyValuePair<int, string> filter in columnFilters)
        {
            if (Accessor.GetCellText(Items[rowIndex], filter.Key)
                .IndexOf(filter.Value, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        foreach (KeyValuePair<int, HashSet<string>> filter in excludedValues)
        {
            if (filter.Value.Count > 0
                && filter.Value.Contains(Accessor.GetCellText(Items[rowIndex], filter.Key)))
            {
                return false;
            }
        }

        return true;
    }

    // ---- Selection ----

    // ---- Master / detail ----

    /// <summary>Whether a detail panel is configured.</summary>
    protected bool HasDetail => DetailTemplate is not null;

    protected bool IsRowExpanded(int rowIndex) => expandedRows.Contains(rowIndex);

    /// <summary>Toggles a row's expanded detail panel.</summary>
    protected void ToggleRowExpanded(int rowIndex)
    {
        if (!expandedRows.Remove(rowIndex))
        {
            expandedRows.Add(rowIndex);
        }

        StateHasChanged();
    }

    protected bool IsRowSelected(int rowIndex) => selectedRows.Contains(rowIndex);

    protected int SelectedCount => selectedRows.Count;

    /// <summary>True when every currently-visible (filtered) data row is selected.</summary>
    protected bool AllVisibleSelected
    {
        get
        {
            int[] visible = FilteredOrder();
            return visible.Length > 0 && Array.TrueForAll(visible, selectedRows.Contains);
        }
    }

    /// <summary>True when some but not all visible rows are selected (header indeterminate state).</summary>
    protected bool SomeVisibleSelected => selectedRows.Count > 0 && !AllVisibleSelected;

    /// <summary>Toggles selection of a single row.</summary>
    protected void ToggleRow(int rowIndex)
    {
        if (!selectedRows.Remove(rowIndex))
        {
            selectedRows.Add(rowIndex);
        }

        _ = NotifySelectionAsync();
        StateHasChanged();
    }

    /// <summary>Selects all visible rows, or clears them if all are already selected.</summary>
    protected void ToggleSelectAll()
    {
        int[] visible = FilteredOrder();
        if (AllVisibleSelected)
        {
            foreach (int rowIndex in visible)
            {
                selectedRows.Remove(rowIndex);
            }
        }
        else
        {
            foreach (int rowIndex in visible)
            {
                selectedRows.Add(rowIndex);
            }
        }

        _ = NotifySelectionAsync();
        StateHasChanged();
    }

    private Task NotifySelectionAsync()
    {
        if (!SelectionChanged.HasDelegate)
        {
            return Task.CompletedTask;
        }

        // Return selected rows in stable row order, not hash-set order.
        List<TRow> selected = new(selectedRows.Count);
        foreach (int rowIndex in rowOrder)
        {
            if (selectedRows.Contains(rowIndex))
            {
                selected.Add(Items[rowIndex]);
            }
        }

        return SelectionChanged.InvokeAsync(selected);
    }

    // ---- Inline edit ----

    private bool focusEditPending;

    /// <summary>The row index being edited, or -1 when not editing.</summary>
    protected int EditingRow { get; private set; } = -1;

    /// <summary>The column index being edited, or -1 when not editing.</summary>
    protected int EditingColumn { get; private set; } = -1;

    /// <summary>The editor input element, captured by the styled layer for focus.</summary>
    protected ElementReference EditInput { get; set; }

    /// <summary>Live text in the open editor (tracked so Enter can commit without a blur).</summary>
    protected string EditDraft { get; private set; } = string.Empty;

    protected bool IsEditing(int rowIndex, int columnIndex) =>
        EditingRow == rowIndex && EditingColumn == columnIndex;

    protected bool CanEdit(int columnIndex) => Editable && !RemoteMode && Accessor.CanEditColumn(columnIndex);

    /// <summary>Updates the draft text as the user types in the editor.</summary>
    protected void SetEditDraft(string? value) => EditDraft = value ?? string.Empty;

    /// <summary>Enters edit mode for a cell, if the column is editable.</summary>
    protected void BeginEdit(int rowIndex, int columnIndex)
    {
        if (!CanEdit(columnIndex))
        {
            return;
        }

        EditingRow = rowIndex;
        EditingColumn = columnIndex;
        EditDraft = Accessor.GetCellText(Items[rowIndex], columnIndex);
        focusEditPending = true;
        StateHasChanged();
    }

    /// <summary>Cancels the in-progress edit without writing back.</summary>
    protected void CancelEdit()
    {
        EditingRow = -1;
        EditingColumn = -1;
        StateHasChanged();
    }

    /// <summary>Writes the current draft back through the generated accessor and exits edit mode.</summary>
    protected async Task CommitEditAsync()
    {
        if (EditingRow < 0 || EditingColumn < 0)
        {
            return;
        }

        int rowIndex = EditingRow;
        int columnIndex = EditingColumn;
        Accessor.SetCellText(Items[rowIndex], columnIndex, EditDraft);
        EditingRow = -1;
        EditingColumn = -1;

        // The edited value may change aggregates; recompute the affected ones.
        if (Aggregations is { Count: > 0 })
        {
            await ComputeAggregatesAsync();
            if (IsGrouped)
            {
                await ComputeGroupAggregatesAsync();
            }
        }

        RebuildSlots();
        StateHasChanged();

        if (RowEdited.HasDelegate)
        {
            await RowEdited.InvokeAsync(Items[rowIndex]);
        }
    }

    /// <summary>
    /// Sorts by a column. A plain click sorts by that column alone (toggling its
    /// direction); an additive click (Shift) adds or toggles it as an extra key,
    /// keeping the existing keys for a multi-column sort. Single-column sorts still
    /// offload numeric columns to the compute backend; multi-column sorts use a
    /// managed stable comparer.
    /// </summary>
    protected async Task SortByAsync(int columnIndex, bool additive = false)
    {
        int existing = sortKeys.FindIndex(k => k.Column == columnIndex);

        if (additive)
        {
            if (existing >= 0)
            {
                sortKeys[existing] = (columnIndex, !sortKeys[existing].Descending);
            }
            else
            {
                sortKeys.Add((columnIndex, false));
            }
        }
        else if (sortKeys.Count == 1 && sortKeys[0].Column == columnIndex)
        {
            sortKeys[0] = (columnIndex, !sortKeys[0].Descending);
        }
        else
        {
            sortKeys.Clear();
            sortKeys.Add((columnIndex, false));
        }

        if (RemoteMode)
        {
            await RefreshRemoteAsync();   // the server re-sorts the whole result
            await NotifyStateChangedAsync();
            return;
        }

        await ApplySortAsync();
        RebuildSlots();
        StateHasChanged();
        await NotifyStateChangedAsync();
    }

    private async Task ApplySortAsync()
    {
        if (sortKeys.Count == 0)
        {
            rowOrder = BuildIdentityOrder(Items.Count);
            return;
        }

        if (sortKeys.Count == 1)
        {
            (int column, _) = sortKeys[0];
            rowOrder = Columns[column].IsNumeric ? await SortNumericAsync(column) : SortText(column);
            return;
        }

        rowOrder = MultiKeySort();
    }

    // Managed multi-key stable sort: compare keys in priority order, breaking final
    // ties by row index so the result is deterministic.
    private int[] MultiKeySort()
    {
        int[] order = BuildIdentityOrder(Items.Count);
        Array.Sort(order, (left, right) =>
        {
            foreach ((int column, bool descending) in sortKeys)
            {
                int comparison = Columns[column].IsNumeric
                    ? Accessor.GetCellValue(Items[left], column).CompareTo(Accessor.GetCellValue(Items[right], column))
                    : string.Compare(
                        Accessor.GetCellText(Items[left], column),
                        Accessor.GetCellText(Items[right], column),
                        StringComparison.OrdinalIgnoreCase);
                if (comparison != 0)
                {
                    return descending ? -comparison : comparison;
                }
            }

            return left.CompareTo(right);
        });

        return order;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusEditPending)
        {
            focusEditPending = false;
            try
            {
                await EditInput.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // editor not in the DOM (e.g. row scrolled out); ignore.
            }
        }

        if (firstRender)
        {
            await EnsureArrowSuppressionAsync();
            if (RemoteMode && !RemoteGrouped)
            {
                await FetchWindowAsync(force: true);   // initial window + total count (flat remote only)
            }
        }

        if (!firstRender || scrollSubscribed || !OperatingSystem.IsBrowser())
        {
            return;
        }

        scrollSubscribed = true;
        await Dom.SubscribeScrollAsync(ContainerId, OnScroll);
        await UpdateWindowAsync();
    }

    private void OnScroll() => _ = UpdateWindowAsync();

    private async Task UpdateWindowAsync()
    {
        (double scrollTop, double clientHeight, _) = await Dom.MeasureViewportAsync(ContainerId);
        int viewport = clientHeight > 0 ? (int)clientHeight : ViewportHeight;

        int desiredFirst = Math.Max(0, (int)(scrollTop / RowHeight) - Overscan);
        int desiredCount = EstimateVisibleCount(viewport);

        if (desiredFirst == firstVisibleIndex && desiredCount == visibleCount)
        {
            return;
        }

        firstVisibleIndex = desiredFirst;
        visibleCount = desiredCount;
        await InvokeAsync(StateHasChanged);

        if (RemoteMode && !RemoteGrouped)
        {
            await FetchWindowAsync();   // pull the newly-scrolled-into-view rows (flat remote only)
        }
    }

    private async Task<int[]> SortNumericAsync(int columnIndex)
    {
        double[] values = new double[Items.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Accessor.GetCellValue(Items[i], columnIndex);
        }

        return await Compute.SortAsync(values, SortDescending);
    }

    private int[] SortText(int columnIndex)
    {
        int[] order = BuildIdentityOrder(Items.Count);
        Array.Sort(order, (left, right) =>
        {
            int comparison = string.Compare(
                Accessor.GetCellText(Items[left], columnIndex),
                Accessor.GetCellText(Items[right], columnIndex),
                StringComparison.OrdinalIgnoreCase);
            return SortDescending ? -comparison : comparison;
        });

        return order;
    }

    private int EstimateVisibleCount(int viewportHeight) =>
        (int)Math.Ceiling((double)viewportHeight / RowHeight) + (Overscan * 2);

    private static int[] BuildIdentityOrder(int count)
    {
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }

        return order;
    }

    public ValueTask DisposeAsync() => Dom.DisposeAsync();
}
