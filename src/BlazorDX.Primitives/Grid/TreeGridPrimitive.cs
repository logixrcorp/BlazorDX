using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Primitives.Grid;

/// <summary>One visible row in a flattened tree: the row, its nesting depth, and state.</summary>
/// <typeparam name="TRow">The row type.</typeparam>
public readonly record struct TreeGridRow<TRow>(TRow Row, int Depth, bool HasChildren, bool Expanded);

/// <summary>
/// Tier 1 headless tree grid: a hierarchical table. Roots come from
/// <see cref="Items"/>; children come from a host-supplied
/// <see cref="ChildrenSelector"/>. The tree is flattened to the rows currently
/// visible (honoring each node's expand state) and that flat list is virtualized
/// exactly like the flat grid, so deep trees stay cheap. Renders nothing itself.
/// </summary>
/// <typeparam name="TRow">The node type (a reference type, for identity-based expand state).</typeparam>
public class TreeGridPrimitive<TRow> : ComponentBase, IAsyncDisposable
    where TRow : class
{
    private readonly HashSet<TRow> expanded = new(ReferenceEqualityComparer.Instance);
    private List<TreeGridRow<TRow>> flattened = [];
    private int firstVisibleIndex;
    private int visibleCount;
    private bool scrollSubscribed;

    /// <summary>The root nodes of the tree.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<TRow> Items { get; set; } = [];

    /// <summary>The generated, reflection-free column accessor for <typeparamref name="TRow"/>.</summary>
    [Parameter, EditorRequired] public IGridRowAccessor<TRow> Accessor { get; set; } = default!;

    /// <summary>Returns a node's children (empty for leaves).</summary>
    [Parameter, EditorRequired] public Func<TRow, IReadOnlyList<TRow>> ChildrenSelector { get; set; } = default!;

    /// <summary>Expand the whole tree on first load.</summary>
    [Parameter] public bool InitiallyExpanded { get; set; }

    [Parameter] public int RowHeight { get; set; } = 32;

    [Parameter] public int ViewportHeight { get; set; } = 480;

    [Parameter] public int Overscan { get; set; } = 8;

    [Inject] private IGridDomInterop Dom { get; set; } = default!;

    /// <summary>The columns to render, in display order.</summary>
    protected IReadOnlyList<GridColumnInfo> Columns => Accessor.Columns;

    /// <summary>Stable element id for the scroll container (drives DOM measurement).</summary>
    protected string ContainerId { get; } = $"dx-tree-{Guid.NewGuid():N}";

    /// <summary>Total visible (flattened) rows, for aria-rowcount and virtualization.</summary>
    protected int VisibleRowCount => flattened.Count;

    protected double TopPadding => (double)firstVisibleIndex * RowHeight;

    protected double BottomPadding => Math.Max(0, (double)(VisibleRowCount - LastVisibleIndex) * RowHeight);

    private int LastVisibleIndex => Math.Min(VisibleRowCount, firstVisibleIndex + visibleCount);

    private bool initialized;

    protected override void OnParametersSet()
    {
        if (!initialized && InitiallyExpanded)
        {
            initialized = true;
            ExpandAll(Items);
        }

        visibleCount = EstimateVisibleCount(ViewportHeight);
        Flatten();
    }

    protected string CellText(TRow row, int columnIndex) => Accessor.GetCellText(row, columnIndex);

    protected bool HasChildren(TRow row) => ChildrenSelector(row).Count > 0;

    protected bool IsExpanded(TRow row) => expanded.Contains(row);

    /// <summary>The flattened rows currently inside the virtualization window.</summary>
    protected IEnumerable<TreeGridRow<TRow>> VisibleRows()
    {
        for (int i = firstVisibleIndex; i < LastVisibleIndex; i++)
        {
            yield return flattened[i];
        }
    }

    /// <summary>Toggles a node's expand state and rebuilds the visible list.</summary>
    protected void Toggle(TRow row)
    {
        if (!HasChildren(row))
        {
            return;
        }

        if (!expanded.Remove(row))
        {
            expanded.Add(row);
        }

        Flatten();
        StateHasChanged();
    }

    /// <summary>Keyboard expand/collapse: Right opens, Left closes (or moves to a leaf's parent state).</summary>
    protected void OnRowKeyDown(TRow row, string key)
    {
        if (!HasChildren(row))
        {
            return;
        }

        bool isOpen = IsExpanded(row);
        if (key == "ArrowRight" && !isOpen)
        {
            Toggle(row);
        }
        else if (key == "ArrowLeft" && isOpen)
        {
            Toggle(row);
        }
    }

    private void ExpandAll(IReadOnlyList<TRow> nodes)
    {
        foreach (TRow node in nodes)
        {
            IReadOnlyList<TRow> children = ChildrenSelector(node);
            if (children.Count > 0)
            {
                expanded.Add(node);
                ExpandAll(children);
            }
        }
    }

    // Walks the tree depth-first, emitting only rows reachable through expanded
    // ancestors — the classic flatten that makes a tree virtualizable.
    private void Flatten()
    {
        List<TreeGridRow<TRow>> rows = new();
        AppendLevel(Items, 0, rows);
        flattened = rows;
    }

    private void AppendLevel(IReadOnlyList<TRow> nodes, int depth, List<TreeGridRow<TRow>> rows)
    {
        foreach (TRow node in nodes)
        {
            IReadOnlyList<TRow> children = ChildrenSelector(node);
            bool hasChildren = children.Count > 0;
            bool isOpen = hasChildren && expanded.Contains(node);
            rows.Add(new TreeGridRow<TRow>(node, depth, hasChildren, isOpen));
            if (isOpen)
            {
                AppendLevel(children, depth + 1, rows);
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
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
    }

    private int EstimateVisibleCount(int viewportHeight) =>
        (int)Math.Ceiling((double)viewportHeight / RowHeight) + (Overscan * 2);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Dom.DisposeAsync();
    }
}
