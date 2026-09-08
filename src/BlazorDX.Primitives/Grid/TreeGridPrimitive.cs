using BlazorDX.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

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
    private int activeIndex = -1;
    private bool suppressKeysRegistered;

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

    /// <summary>
    /// The element id of the active (roving-focus) row, for <c>aria-activedescendant</c>.
    /// Addressed by index into the flattened row list rather than a captured element
    /// reference, so navigation works even when the active row is scrolled out of the
    /// rendered window — the same reason <c>DataGridPrimitive</c> addresses its active cell
    /// by slot rather than by element.
    /// </summary>
    protected string ActiveRowId => $"{ContainerId}-active";

    /// <summary>The flattened-row position of the active row, or -1 when the tree is empty.</summary>
    protected int ActiveIndex => activeIndex;

    /// <summary>Whether a row is currently active (false only when the tree has no rows).</summary>
    protected bool HasActiveRow => activeIndex >= 0 && activeIndex < flattened.Count;

    /// <summary>Whether the flattened row at <paramref name="index"/> is the active row.</summary>
    protected bool IsActiveRow(int index) => HasActiveRow && index == activeIndex;

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
        ClampActiveIndex();
    }

    protected string CellText(TRow row, int columnIndex) => Accessor.GetCellText(row, columnIndex);

    protected bool HasChildren(TRow row) => ChildrenSelector(row).Count > 0;

    protected bool IsExpanded(TRow row) => expanded.Contains(row);

    /// <summary>The flattened rows currently inside the virtualization window, paired with each row's absolute index into the full flattened list (needed to compare against <see cref="ActiveIndex"/>).</summary>
    protected IEnumerable<(int Index, TreeGridRow<TRow> Row)> VisibleRows()
    {
        for (int i = firstVisibleIndex; i < LastVisibleIndex; i++)
        {
            yield return (i, flattened[i]);
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
        ClampActiveIndex();
        StateHasChanged();
    }

    /// <summary>Makes the flattened row at <paramref name="index"/> the active row (e.g. on click).</summary>
    protected void SetActiveRow(int index)
    {
        if (index >= 0 && index < flattened.Count)
        {
            activeIndex = index;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Roving-row keyboard navigation, mirroring the WAI-ARIA tree pattern <c>DxTreeView</c>
    /// already implements: Up/Down move the active row across the full flattened list (not
    /// just the rendered window), Right expands a collapsed row or descends into an expanded
    /// one's first child, Left collapses an expanded row or moves to its parent, Home/End jump
    /// to the first/last row.
    /// </summary>
    protected async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (!HasActiveRow)
        {
            return;
        }

        TreeGridRow<TRow> row = flattened[activeIndex];
        switch (args.Key)
        {
            case "ArrowDown":
                if (activeIndex < flattened.Count - 1)
                {
                    activeIndex += 1;
                }

                break;
            case "ArrowUp":
                if (activeIndex > 0)
                {
                    activeIndex -= 1;
                }

                break;
            case "ArrowRight":
                if (row.HasChildren && !row.Expanded)
                {
                    Toggle(row.Row);
                }
                else if (row.HasChildren)
                {
                    activeIndex += 1;   // depth-first flatten: the first child is always next
                }

                break;
            case "ArrowLeft":
                if (row.HasChildren && row.Expanded)
                {
                    Toggle(row.Row);
                }
                else if (row.Depth > 0)
                {
                    activeIndex = ParentIndex(activeIndex);
                }

                break;
            case "Home":
                activeIndex = 0;
                break;
            case "End":
                activeIndex = flattened.Count - 1;
                break;
            default:
                return;   // not a navigation key
        }

        StateHasChanged();
        await EnsureActiveVisibleAsync();
    }

    // Re-anchors the active row onto a real row after the visible list reshapes
    // (expand/collapse, or the host swapping Items/ChildrenSelector).
    private void ClampActiveIndex()
    {
        if (flattened.Count == 0)
        {
            activeIndex = -1;
        }
        else if (activeIndex < 0)
        {
            activeIndex = 0;
        }
        else if (activeIndex >= flattened.Count)
        {
            activeIndex = flattened.Count - 1;
        }
    }

    // The flatten is depth-first pre-order, so a node's parent is the nearest preceding
    // entry at a shallower depth.
    private int ParentIndex(int childIndex)
    {
        int depth = flattened[childIndex].Depth;
        for (int i = childIndex - 1; i >= 0; i--)
        {
            if (flattened[i].Depth < depth)
            {
                return i;
            }
        }

        return childIndex;
    }

    // Scrolls the container so the active row sits inside the viewport, mirroring
    // DataGridPrimitive's EnsureActiveVisibleAsync. The current scroll position is
    // approximated from the windowing state, so no extra interop round-trip is needed.
    private async Task EnsureActiveVisibleAsync()
    {
        if (activeIndex < 0)
        {
            return;
        }

        int rowsInView = Math.Max(1, ViewportHeight / RowHeight);
        int topRow = firstVisibleIndex + Overscan;
        int bottomRow = topRow + rowsInView - 1;

        double? target = null;
        if (activeIndex < topRow)
        {
            target = (double)activeIndex * RowHeight;
        }
        else if (activeIndex > bottomRow)
        {
            target = (double)(activeIndex - rowsInView + 1) * RowHeight;
        }

        if (target is double top)
        {
            await Dom.ScrollToAsync(ContainerId, Math.Max(0, top));
        }
    }

    // Registers the JS keydown guard that suppresses native arrow/page scrolling (so row
    // navigation stays in control) without blocking text inputs. Browser-only.
    private async Task EnsureArrowSuppressionAsync()
    {
        if (suppressKeysRegistered || !OperatingSystem.IsBrowser())
        {
            return;
        }

        suppressKeysRegistered = true;
        await Dom.SuppressArrowKeysAsync(ContainerId);
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
        if (!firstRender)
        {
            return;
        }

        await EnsureArrowSuppressionAsync();

        if (scrollSubscribed || !OperatingSystem.IsBrowser())
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
