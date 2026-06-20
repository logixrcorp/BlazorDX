using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Keyboard cell navigation for <see cref="DataGridPrimitive{TRow}"/> (the ARIA
/// "grid" pattern). Kept in its own partial so the core grid file stays under the
/// 1000-line cap — this is the navigation concern, not the data concern.
///
/// The active cell is addressed by a slot position (into the full, virtualized
/// slot list) plus a display-column position. Focus stays on the grid body and
/// the active cell is pointed to with <c>aria-activedescendant</c>, so navigation
/// works regardless of which rows are currently realized in the DOM. When the
/// active cell crosses the rendered window the grid scrolls it back into view.
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    private int activeSlot = -1;
    private int activeColumn = -1;
    private bool suppressKeysRegistered;

    /// <summary>
    /// When true, the grid supports arrow-key cell navigation: arrows move the
    /// active cell, Home/End jump to the row edges, Ctrl+Home/End to the grid
    /// corners, and PageUp/PageDown move by a viewport.
    /// </summary>
    [Parameter] public bool KeyboardNavigation { get; set; }

    /// <summary>The element id of the active cell, for <c>aria-activedescendant</c>.</summary>
    protected string ActiveCellId => $"{ContainerId}-active";

    /// <summary>The original data-row index of the active cell, or -1 when none.</summary>
    protected int ActiveRow =>
        activeSlot >= 0 && activeSlot < slots.Count && !slots[activeSlot].IsGroupHeader
            ? slots[activeSlot].RowIndex
            : -1;

    /// <summary>The display-column position of the active cell, or -1 when none.</summary>
    protected int ActiveColumn => activeColumn;

    /// <summary>Whether keyboard navigation is on and an active cell currently exists.</summary>
    protected bool HasActiveCell => KeyboardNavigation && ActiveRow >= 0 && activeColumn >= 0;

    /// <summary>Whether the cell at (row, display column) is the active cell.</summary>
    protected bool IsActiveCell(int rowIndex, int displayColumn) =>
        KeyboardNavigation && rowIndex == ActiveRow && displayColumn == activeColumn;

    /// <summary>
    /// Re-anchors the active cell onto a real, visible data cell after the data or
    /// columns reshape (sort/filter/group/hide). Called at the end of OnParametersSet.
    /// </summary>
    private void ClampActiveCell()
    {
        if (!KeyboardNavigation)
        {
            activeSlot = -1;
            activeColumn = -1;
            return;
        }

        if (activeColumn < 0 || activeColumn >= Columns.Count || IsColumnHidden(ColumnOrder[activeColumn]))
        {
            activeColumn = FirstVisibleColumnPosition();
        }

        if (activeSlot < 0 || activeSlot >= slots.Count || slots[activeSlot].IsGroupHeader)
        {
            activeSlot = FirstDataSlot();
        }
    }

    /// <summary>Moves the active cell in response to a navigation key.</summary>
    protected async Task OnCellNavigationAsync(KeyboardEventArgs args)
    {
        // Editing owns its own keys; navigation only runs when enabled and idle.
        if (!KeyboardNavigation || EditingRow >= 0)
        {
            return;
        }

        int page = Math.Max(1, (ViewportHeight / RowHeight) - 1);
        switch (args.Key)
        {
            case "ArrowDown": MoveActiveRow(1); break;
            case "ArrowUp": MoveActiveRow(-1); break;
            case "ArrowRight": activeColumn = NextVisibleColumnPosition(activeColumn, 1); break;
            case "ArrowLeft": activeColumn = NextVisibleColumnPosition(activeColumn, -1); break;
            case "PageDown": MoveActiveRow(page); break;
            case "PageUp": MoveActiveRow(-page); break;
            case "Home":
                activeColumn = FirstVisibleColumnPosition();
                if (args.CtrlKey)
                {
                    activeSlot = FirstDataSlot();
                }

                break;
            case "End":
                activeColumn = LastVisibleColumnPosition();
                if (args.CtrlKey)
                {
                    activeSlot = LastDataSlot();
                }

                break;
            default:
                return;   // not a navigation key
        }

        StateHasChanged();
        await EnsureActiveVisibleAsync();
    }

    /// <summary>Makes a cell the active cell (e.g. on click), if keyboard nav is on.</summary>
    protected void SetActiveCell(int rowIndex, int displayColumn)
    {
        if (!KeyboardNavigation)
        {
            return;
        }

        int slot = SlotOfRow(rowIndex);
        if (slot >= 0)
        {
            activeSlot = slot;
            activeColumn = displayColumn;
            StateHasChanged();
        }
    }

    private void MoveActiveRow(int delta)
    {
        int dir = Math.Sign(delta);
        if (dir == 0)
        {
            return;
        }

        int position = activeSlot;
        for (int step = 0; step < Math.Abs(delta); step++)
        {
            int next = NextDataSlot(position, dir);
            if (next < 0)
            {
                break;   // hit the top/bottom; stay on the last reachable row
            }

            position = next;
        }

        activeSlot = position;
    }

    private int NextDataSlot(int from, int dir)
    {
        for (int i = from + dir; i >= 0 && i < slots.Count; i += dir)
        {
            if (!slots[i].IsGroupHeader)
            {
                return i;
            }
        }

        return -1;
    }

    private int FirstDataSlot()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsGroupHeader)
            {
                return i;
            }
        }

        return -1;
    }

    private int LastDataSlot()
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (!slots[i].IsGroupHeader)
            {
                return i;
            }
        }

        return -1;
    }

    private int SlotOfRow(int rowIndex)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsGroupHeader && slots[i].RowIndex == rowIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private int FirstVisibleColumnPosition()
    {
        for (int display = 0; display < Columns.Count; display++)
        {
            if (!IsColumnHidden(ColumnOrder[display]))
            {
                return display;
            }
        }

        return -1;
    }

    private int LastVisibleColumnPosition()
    {
        for (int display = Columns.Count - 1; display >= 0; display--)
        {
            if (!IsColumnHidden(ColumnOrder[display]))
            {
                return display;
            }
        }

        return -1;
    }

    private int NextVisibleColumnPosition(int from, int dir)
    {
        for (int display = from + dir; display >= 0 && display < Columns.Count; display += dir)
        {
            if (!IsColumnHidden(ColumnOrder[display]))
            {
                return display;
            }
        }

        return from;   // clamp at the row edge rather than wrapping
    }

    // Scrolls the container so the active slot sits inside the viewport. The
    // current scroll position is approximated from the windowing state, so no
    // extra interop round-trip is needed. A no-op off-browser, where there is no
    // scroll position to set.
    private async Task EnsureActiveVisibleAsync()
    {
        if (activeSlot < 0)
        {
            return;
        }

        int rowsInView = Math.Max(1, ViewportHeight / RowHeight);
        int topRow = firstVisibleIndex + Overscan;   // first roughly-fully-visible row
        int bottomRow = topRow + rowsInView - 1;

        double? target = null;
        if (activeSlot < topRow)
        {
            target = (double)activeSlot * RowHeight;
        }
        else if (activeSlot > bottomRow)
        {
            target = (double)(activeSlot - rowsInView + 1) * RowHeight;
        }

        if (target is double top)
        {
            await Dom.ScrollToAsync(ContainerId, Math.Max(0, top));
        }
    }

    // Registers the JS keydown guard that suppresses native arrow/page scrolling
    // (so cell navigation stays in control) without blocking text inputs. Browser-only.
    private async Task EnsureArrowSuppressionAsync()
    {
        if (suppressKeysRegistered || !KeyboardNavigation || !OperatingSystem.IsBrowser())
        {
            return;
        }

        suppressKeysRegistered = true;
        await Dom.SuppressArrowKeysAsync(ContainerId);
    }
}
