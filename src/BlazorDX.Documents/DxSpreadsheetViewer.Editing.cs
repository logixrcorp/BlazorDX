using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using BlazorDX.Components;
using BlazorDX.Documents.Formula;

namespace BlazorDX.Documents;

/// <summary>
/// The editable layer of <see cref="DxSpreadsheetViewer"/>: when <see cref="Editable"/>
/// is set, the same virtualized <c>role="grid"</c> becomes a keyboard-first spreadsheet.
/// Each cell holds <b>raw content</b> (a literal, or a <c>=</c> formula); the grid shows
/// the <b>computed value</b> from <see cref="FormulaEngine"/>, and a cell in edit mode
/// shows its raw content in an <c>&lt;input&gt;</c>. Commit recalculates the sheet.
/// </summary>
/// <remarks>
/// Kept in its own partial so the read-only file stays focused and under the 1000-line
/// cap. No reflection, no JS interop: focus is moved with Blazor's
/// <see cref="ElementReference.FocusAsync()"/>, and arrow/Home/End navigation runs in C#.
/// The active cell carries a roving <c>tabindex</c> (the DataGrid/scheduler keyboard
/// model); error cells get an <c>aria-label</c> that announces the error so meaning does
/// not rely on color alone (WCAG 1.4.1).
/// </remarks>
public sealed partial class DxSpreadsheetViewer
{
    // Per-sheet mutable raw content (the edit buffer) and the incremental recalc engine
    // computed over it. The engine is rebuilt on load / structural change; single-cell
    // edits go through editWorkbook.SetCell so only affected cells recompute.
    private List<List<string>>? editRaw;
    private IncrementalWorkbook? editWorkbook;
    private int editSheetIndex = -1;

    // 2-D virtualization of the editable grid: a single scroll container windows both rows and
    // columns. Fixed cell metrics (must match dx-spreadsheet.css: cell 9.5rem, gutter 3.25rem).
    private const int ColWidthPx = 152;       // .dx-sheet-cell width (9.5rem @ 16px root)
    private const int RowLabelWidthPx = 52;   // .dx-sheet-rowlabel width (3.25rem)
    private const int ColOverscan = 3;
    private const int RowOverscan = 6;

    private readonly string gridScrollId = $"dx-sheet-scroll-{Guid.NewGuid():N}";
    private bool gridScrollSubscribed;
    private int rowWinFirst;   // first DATA-row index in the window (data rows are buffer rows 1..)
    private int rowWinCount;
    private int colWinFirst;   // first column index in the window
    private int colWinCount;

    /// <summary>
    /// Assumed viewport width (px) for the initial column window before the live container is
    /// measured (and off-browser, e.g. SSR/tests). The real width drives windowing once mounted.
    /// </summary>
    [Parameter] public int ViewportWidth { get; set; } = 960;

    // The active cell (full-sheet coordinates incl. the header at row 0) and, when a
    // cell is open for editing, the in-progress text and the input/cell element refs.
    private int activeRow;
    private int activeColumn;
    private int editingRow = -1;
    private int editingColumn = -1;
    private string editBuffer = string.Empty;
    private ElementReference editInput;
    private ElementReference activeCellElement;
    private bool focusEditInput;
    private bool focusActiveCell;

    /// <summary>
    /// When true the viewer is an editable spreadsheet: cells accept literals and
    /// <c>=</c> formulas, commit triggers a recalculation, and the edited
    /// <see cref="Workbook"/> is surfaced via <see cref="WorkbookChanged"/>.
    /// </summary>
    [Parameter] public bool Editable { get; set; }

    /// <summary>
    /// Raised after every committed edit with a freshly built <see cref="Workbook"/> of
    /// raw content (formulas as <c>=…</c>), so the host can persist or save it.
    /// </summary>
    [Parameter] public EventCallback<Workbook> WorkbookChanged { get; set; }

    /// <summary>Whether any cell has been edited since the workbook was loaded.</summary>
    public bool IsDirty { get; private set; }

    // Rebuilds the edit buffer + computed grid when the active sheet (or its source
    // workbook) changes. Called from OnParametersSet. A no-op when not editable.
    private void SyncEditState()
    {
        if (!Editable)
        {
            return;
        }

        if (editSheetIndex != ActiveIndex || editRaw is null)
        {
            LoadActiveSheetIntoBuffer();
        }
    }

    private void LoadActiveSheetIntoBuffer()
    {
        Worksheet? sheet = ActiveSheet;
        editSheetIndex = ActiveIndex;
        editingRow = -1;
        editingColumn = -1;

        if (sheet is null)
        {
            editRaw = null;
            editWorkbook = null;
            return;
        }

        int columns = sheet.ColumnCount;
        var buffer = new List<List<string>>(sheet.Rows.Count);
        foreach (IReadOnlyList<string> row in sheet.Rows)
        {
            var copy = new List<string>(columns);
            for (int c = 0; c < columns; c++)
            {
                copy.Add(c < row.Count ? row[c] ?? string.Empty : string.Empty);
            }

            buffer.Add(copy);
        }

        editRaw = buffer;
        activeRow = Math.Clamp(activeRow, 0, Math.Max(0, buffer.Count - 1));
        activeColumn = Math.Clamp(activeColumn, 0, Math.Max(0, columns - 1));

        // Seed the windows from the assumed viewport; the real container size refines them
        // once mounted (UpdateWindowFromScrollAsync).
        rowWinFirst = 0;
        colWinFirst = 0;
        rowWinCount = ((int)Math.Ceiling((double)ViewportHeight / RowHeight)) + (RowOverscan * 2);
        colWinCount = ((int)Math.Ceiling((double)ViewportWidth / ColWidthPx)) + (ColOverscan * 2);

        RebuildWorkbook();
    }

    // Builds a fresh incremental workbook from the whole sheet buffer (header row included;
    // it is just text literals). Used on load and after a structural change, where the
    // dimensions move; single-cell edits go through editWorkbook.SetCell instead.
    private void RebuildWorkbook() =>
        editWorkbook = editRaw is null ? null : new IncrementalWorkbook(editRaw);

    private int EditRowCount => editRaw?.Count ?? 0;

    // Width comes from the live buffer (so insert/delete column is reflected), falling
    // back to the source sheet before the buffer is built.
    private int EditColumnCount =>
        editRaw is { Count: > 0 } ? editRaw[0].Count : (ActiveSheet?.ColumnCount ?? 0);

    private string RawAt(int row, int column) =>
        editRaw is not null && row >= 0 && row < editRaw.Count
        && column >= 0 && column < editRaw[row].Count
            ? editRaw[row][column]
            : string.Empty;

    private CellValue ComputedAt(int row, int column) =>
        editWorkbook?.GetValue(row, column) ?? CellValue.Blank;

    // ---- Rendering ---------------------------------------------------------------

    private void BuildEditableGrid(RenderTreeBuilder builder, Worksheet sheet)
    {
        int dataRowCount = Math.Max(0, EditRowCount - 1);
        int dataColumns = EditColumnCount;

        // Resolve the column window (clamped to the sheet).
        int cFirst = Math.Clamp(colWinFirst, 0, Math.Max(0, dataColumns - 1));
        int cLast = Math.Min(dataColumns, cFirst + Math.Max(1, colWinCount));

        // Resolve the data-row window.
        int rFirst = Math.Clamp(rowWinFirst, 0, Math.Max(0, dataRowCount));
        int rLast = Math.Min(dataRowCount, rFirst + Math.Max(1, rowWinCount));

        double totalWidth = RowLabelWidthPx + ((double)dataColumns * ColWidthPx);
        double leftSpacer = (double)cFirst * ColWidthPx;
        double rightSpacer = (double)(dataColumns - cLast) * ColWidthPx;
        double topSpacer = (double)rFirst * RowHeight;
        double bottomSpacer = (double)Math.Max(0, dataRowCount - rLast) * RowHeight;

        // Toolbar + formula bar are siblings ABOVE the grid. Each goes in its own region
        // so its internal sequence numbers can't collide with the grid's (the diff matches
        // siblings by sequence; overlapping numbers corrupt it).
        if (ShowToolbar)
        {
            builder.OpenRegion(28);
            BuildEditToolbar(builder);
            builder.CloseRegion();

            builder.OpenRegion(29);
            BuildFormulaBar(builder);
            builder.CloseRegion();
        }

        // One scroll container windows BOTH axes (ADR-0009: runtime column schema). The header
        // row and the row-label gutter freeze via position:sticky; left/right and top/bottom
        // spacers reserve the off-window extent so the scrollbars reflect the full sheet.
        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "id", gridScrollId);
        builder.AddAttribute(32, "class", "dx-sheet-grid dx-sheet-grid-editable dx-sheet-scroll");
        builder.AddAttribute(33, "role", "grid");
        builder.AddAttribute(34, "aria-multiselectable", "false");
        builder.AddAttribute(35, "aria-label", $"{sheet.Name} worksheet, editable");
        builder.AddAttribute(36, "aria-rowcount", EditRowCount.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(37, "aria-colcount", (dataColumns + 1).ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(38, "tabindex", "0"); // scrollable region is keyboard-reachable (WCAG 2.1.1)
        builder.AddAttribute(39, "style", $"height:{ViewportHeight}px;");
        builder.AddAttribute(40, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnGridKeyDownAsync));

        // Header row (aria-rowindex 1): buffer row 0. Sticky-top, editable for column labels.
        BuildEditableRow(builder, 0, cFirst, cLast, totalWidth, leftSpacer, rightSpacer, isHeader: true);

        // Top spacer (reserves the rows scrolled above the window).
        Spacer(builder, 50, $"height:{topSpacer.ToString(CultureInfo.InvariantCulture)}px;");

        for (int di = rFirst; di < rLast; di++)
        {
            BuildEditableRow(builder, di + 1, cFirst, cLast, totalWidth, leftSpacer, rightSpacer, isHeader: false);
        }

        // Bottom spacer (reserves the rows scrolled below the window).
        Spacer(builder, 70, $"height:{bottomSpacer.ToString(CultureInfo.InvariantCulture)}px;");

        builder.CloseElement(); // grid
    }

    // A presentational block that reserves scroll extent without breaking the grid hierarchy.
    private static void Spacer(RenderTreeBuilder builder, int seq, string style)
    {
        builder.OpenElement(seq, "div");
        builder.AddAttribute(seq + 1, "role", "presentation");
        builder.AddAttribute(seq + 2, "style", style);
        builder.CloseElement();
    }

    // Renders one row of the editable grid over the column window [cFirst, cLast), with left
    // and right spacers reserving the off-window columns. rowIndex is the buffer (full-sheet)
    // index; the header row uses columnheader cells, data rows use editable gridcells.
    private void BuildEditableRow(
        RenderTreeBuilder builder, int rowIndex, int cFirst, int cLast,
        double totalWidth, double leftSpacer, double rightSpacer, bool isHeader)
    {
        builder.OpenElement(0, "div");
        builder.SetKey(isHeader ? "dx-head" : rowIndex);
        builder.AddAttribute(1, "class", isHeader ? "dx-sheet-headrow" : "dx-sheet-row");
        builder.AddAttribute(2, "role", "row");
        builder.AddAttribute(3, "aria-rowindex", (rowIndex + 1).ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(4, "style",
            $"width:{totalWidth.ToString(CultureInfo.InvariantCulture)}px;" +
            (isHeader ? string.Empty : $"height:{RowHeight}px;"));

        // Row-label gutter (sticky-left): an empty corner for the header, the row number otherwise.
        builder.OpenElement(5, "span");
        builder.AddAttribute(6, "class", isHeader
            ? "dx-sheet-colhead dx-sheet-rowlabel"
            : "dx-sheet-cell dx-sheet-rowlabel");
        builder.AddAttribute(7, "role", isHeader ? "columnheader" : "rowheader");
        builder.AddAttribute(8, "aria-colindex", "1");
        builder.AddContent(9, isHeader ? string.Empty : rowIndex.ToString(CultureInfo.InvariantCulture));
        builder.CloseElement();

        // Spacers are always present (width 0 when the window touches an edge) so the row's
        // child structure stays stable across renders. The cell loop is wrapped in a region so
        // BuildEditableCell's internal sequence numbers can't collide with the spacers'.
        Spacer(builder, 10, $"flex:0 0 {leftSpacer.ToString(CultureInfo.InvariantCulture)}px;");

        builder.OpenRegion(13);
        for (int c = cFirst; c < cLast; c++)
        {
            BuildEditableCell(builder, rowIndex, c, isHeader);
        }

        builder.CloseRegion();

        Spacer(builder, 14, $"flex:0 0 {rightSpacer.ToString(CultureInfo.InvariantCulture)}px;");

        builder.CloseElement(); // row
    }

    private void BuildEditableCell(RenderTreeBuilder builder, int rowIndex, int column, bool isHeader)
    {
        bool isActive = rowIndex == activeRow && column == activeColumn;
        bool isEditing = rowIndex == editingRow && column == editingColumn;
        CellValue computed = ComputedAt(rowIndex, column);
        bool isErrorCell = computed.IsError && !isEditing;
        string display = computed.ToDisplayString();
        string raw = RawAt(rowIndex, column);

        string cssClass = isHeader ? "dx-sheet-colhead dx-sheet-editcell" : "dx-sheet-cell dx-sheet-editcell";
        if (isActive)
        {
            cssClass += " dx-sheet-cell-active";
        }

        if (isErrorCell)
        {
            cssClass += " dx-sheet-cell-error";
        }

        builder.OpenElement(10, "span");
        builder.SetKey(column);
        builder.AddAttribute(11, "class", cssClass);
        builder.AddAttribute(12, "role", isHeader ? "columnheader" : "gridcell");
        builder.AddAttribute(13, "aria-colindex", (column + 2).ToString(CultureInfo.InvariantCulture));
        if (!isHeader)
        {
            builder.AddAttribute(14, "aria-readonly", "false");
        }

        // Roving tabindex: only the active cell is in the tab order; the rest are -1
        // and reached with the arrow keys (ARIA grid keyboard model).
        builder.AddAttribute(15, "tabindex", isActive && !isEditing ? "0" : "-1");

        if (isErrorCell)
        {
            // Announce the error with an accessible name + title so it is conveyed by
            // text, not color alone (WCAG 1.4.1).
            string label = $"Error: {display}";
            builder.AddAttribute(16, "aria-label", label);
            builder.AddAttribute(17, "title", label);
        }

        if (!isEditing)
        {
            int capturedRow = rowIndex;
            int capturedCol = column;
            builder.AddAttribute(18, "onclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => OnCellClick(capturedRow, capturedCol)));
            builder.AddAttribute(19, "ondblclick",
                EventCallback.Factory.Create<MouseEventArgs>(this, () => BeginEdit(capturedRow, capturedCol)));
        }

        // The reference capture MUST be unconditional. Emitting it only for the active
        // cell means that when the active cell moves, the previously-active cell (which
        // persists at the same key/sequence) loses its ElementReferenceCapture frame —
        // and Blazor's differ cannot remove a reference-capture frame in place, throwing
        // "Unexpected frame type during RemoveOldFrame: ElementReferenceCapture". So every
        // cell always captures; only the active one stores into the focus target.
        builder.AddElementReferenceCapture(20, el =>
        {
            if (isActive)
            {
                activeCellElement = el;
            }
        });

        if (isEditing)
        {
            BuildCellInput(builder, rowIndex, column, raw);
        }
        else
        {
            builder.AddContent(21, display);
        }

        builder.CloseElement();
    }

    private void BuildCellInput(RenderTreeBuilder builder, int rowIndex, int column, string raw)
    {
        builder.OpenElement(30, "input");
        builder.AddAttribute(31, "type", "text");
        builder.AddAttribute(32, "class", "dx-sheet-cell-input");
        builder.AddAttribute(33, "aria-label",
            $"Cell {CellLabel(rowIndex, column)} content");
        builder.AddAttribute(34, "value", editBuffer);
        builder.AddAttribute(35, "oninput",
            EventCallback.Factory.Create<ChangeEventArgs>(this, e => editBuffer = e.Value?.ToString() ?? string.Empty));
        builder.AddAttribute(36, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnInputKeyDownAsync));
        // The input owns its own keys; stop the grid's navigation handler from firing.
        builder.AddEventStopPropagationAttribute(37, "onkeydown", true);
        builder.AddElementReferenceCapture(38, el => editInput = el);
        builder.CloseElement();
    }

    private static string CellLabel(int rowIndex, int column) =>
        CellAddress.ColumnToLetters(column) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);

    // ---- Keyboard ----------------------------------------------------------------

    private async Task OnGridKeyDownAsync(KeyboardEventArgs args)
    {
        if (editingRow >= 0)
        {
            return; // the input handles keys while editing
        }

        switch (args.Key)
        {
            case "ArrowDown": MoveActive(1, 0); break;
            case "ArrowUp": MoveActive(-1, 0); break;
            case "ArrowRight": MoveActive(0, 1); break;
            case "ArrowLeft": MoveActive(0, -1); break;
            case "Home":
                activeColumn = 0;
                if (args.CtrlKey)
                {
                    activeRow = 0;
                }

                break;
            case "End":
                activeColumn = Math.Max(0, EditColumnCount - 1);
                if (args.CtrlKey)
                {
                    activeRow = Math.Max(0, EditRowCount - 1);
                }

                break;
            case "Enter":
            case "F2":
                BeginEdit(activeRow, activeColumn);
                return;
            case "Delete":
            case "Backspace":
                await ClearActiveCellAsync();
                return;
            default:
                // Excel-style type-to-edit: a single printable character (no modifier)
                // opens the cell seeded with that character, replacing prior content.
                if (args.Key.Length == 1 && !args.CtrlKey && !args.AltKey && !args.MetaKey)
                {
                    BeginEdit(activeRow, activeColumn, args.Key);
                }

                return;
        }

        EnsureActiveInWindow(); // Home/End move the active cell directly
        focusActiveCell = true;
        StateHasChanged();
    }

    private void MoveActive(int rowDelta, int colDelta)
    {
        activeRow = Math.Clamp(activeRow + rowDelta, 0, Math.Max(0, EditRowCount - 1));
        activeColumn = Math.Clamp(activeColumn + colDelta, 0, Math.Max(0, EditColumnCount - 1));
        EnsureActiveInWindow();
    }

    // ---- 2-D virtualization window -----------------------------------------------

    // Subscribes to the scroll container once it is mounted, then computes the initial window.
    private async Task EnsureGridScrollAsync()
    {
        if (!Editable || gridScrollSubscribed || !OperatingSystem.IsBrowser())
        {
            return;
        }

        gridScrollSubscribed = true;
        await Interop.SubscribeScrollAsync(gridScrollId, OnGridScroll);
        await UpdateWindowFromScrollAsync();
    }

    private void OnGridScroll() => _ = UpdateWindowFromScrollAsync();

    // Recomputes the row/column window from the live scroll metrics (mouse / trackpad / scrollbar).
    private async Task UpdateWindowFromScrollAsync()
    {
        (double top, double left, double clientH, double clientW) =
            await Interop.MeasureViewport2dAsync(gridScrollId);
        int viewH = clientH > 0 ? (int)clientH : ViewportHeight;
        int viewW = clientW > 0 ? (int)clientW : ViewportWidth;

        int newRowFirst = Math.Max(0, (int)(top / RowHeight) - RowOverscan);
        int newRowCount = (int)Math.Ceiling((double)viewH / RowHeight) + (RowOverscan * 2);
        int newColFirst = Math.Max(0, (int)(left / ColWidthPx) - ColOverscan);
        int newColCount = (int)Math.Ceiling((double)viewW / ColWidthPx) + (ColOverscan * 2);

        if (newRowFirst == rowWinFirst && newRowCount == rowWinCount
            && newColFirst == colWinFirst && newColCount == colWinCount)
        {
            return; // window unchanged — skip the re-render
        }

        rowWinFirst = newRowFirst;
        rowWinCount = newRowCount;
        colWinFirst = newColFirst;
        colWinCount = newColCount;
        await InvokeAsync(StateHasChanged);
    }

    // Shifts the window so the active cell is always rendered (hence focusable; the browser
    // then scrolls it into view via FocusAsync). Called whenever the active cell moves.
    private void EnsureActiveInWindow()
    {
        int dataColumns = EditColumnCount;
        int dataRowCount = Math.Max(0, EditRowCount - 1);

        int colVisible = Math.Max(1, colWinCount - (ColOverscan * 2));
        if (activeColumn < colWinFirst)
        {
            colWinFirst = activeColumn;
        }
        else if (activeColumn >= colWinFirst + colVisible)
        {
            colWinFirst = activeColumn - colVisible + 1;
        }

        colWinFirst = Math.Clamp(colWinFirst, 0, Math.Max(0, dataColumns - 1));

        if (activeRow >= 1) // row 0 is the always-rendered sticky header
        {
            int activeData = activeRow - 1;
            int rowVisible = Math.Max(1, rowWinCount - (RowOverscan * 2));
            if (activeData < rowWinFirst)
            {
                rowWinFirst = activeData;
            }
            else if (activeData >= rowWinFirst + rowVisible)
            {
                rowWinFirst = activeData - rowVisible + 1;
            }

            rowWinFirst = Math.Clamp(rowWinFirst, 0, Math.Max(0, dataRowCount - 1));
        }
    }

    private async Task OnInputKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                await CommitAsync();
                MoveActive(1, 0);
                focusActiveCell = true;
                break;
            case "Tab":
                await CommitAsync();
                MoveActive(0, args.ShiftKey ? -1 : 1);
                focusActiveCell = true;
                break;
            case "Escape":
                CancelEdit();
                focusActiveCell = true;
                break;
            default:
                return;
        }

        StateHasChanged();
    }

    // ---- Edit lifecycle ----------------------------------------------------------

    private void OnCellClick(int rowIndex, int column)
    {
        activeRow = rowIndex;
        activeColumn = column;
        EnsureActiveInWindow();
        focusActiveCell = true;
        StateHasChanged();
    }

    private void BeginEdit(int rowIndex, int column, string? initialText = null)
    {
        if (editRaw is null || rowIndex < 0 || rowIndex >= editRaw.Count)
        {
            return;
        }

        activeRow = rowIndex;
        activeColumn = column;
        editingRow = rowIndex;
        editingColumn = column;
        editBuffer = initialText ?? RawAt(rowIndex, column);
        EnsureActiveInWindow();
        focusEditInput = true;
        StateHasChanged();
    }

    private void CancelEdit()
    {
        editingRow = -1;
        editingColumn = -1;
        editBuffer = string.Empty;
    }

    private async Task CommitAsync()
    {
        if (editingRow < 0 || editRaw is null)
        {
            return;
        }

        int row = editingRow;
        int column = editingColumn;
        string newValue = editBuffer;

        editingRow = -1;
        editingColumn = -1;
        editBuffer = string.Empty;

        if (row < editRaw.Count && column < editRaw[row].Count
            && !string.Equals(editRaw[row][column], newValue, StringComparison.Ordinal))
        {
            editRaw[row][column] = newValue;
            IsDirty = true;
            editWorkbook?.SetCell(row, column, newValue); // incremental: only dependents recompute
            if (WorkbookChanged.HasDelegate)
            {
                await WorkbookChanged.InvokeAsync(BuildEditedWorkbook());
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="Workbook"/> of the current raw content (formulas preserved as
    /// <c>=…</c>). The active sheet reflects in-flight edits; other sheets pass through.
    /// </summary>
    public Workbook BuildEditedWorkbook()
    {
        IReadOnlyList<Worksheet> source = Sheets;
        var sheets = new List<Worksheet>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            if (i == editSheetIndex && editRaw is not null)
            {
                var rows = new List<IReadOnlyList<string>>(editRaw.Count);
                foreach (List<string> r in editRaw)
                {
                    rows.Add(r.ToArray());
                }

                sheets.Add(new Worksheet(source[i].Name, rows, EditColumnCount));
            }
            else
            {
                sheets.Add(source[i]);
            }
        }

        return new Workbook(sheets);
    }

    // Moves DOM focus to the input that just opened, or to the active cell after a
    // navigation/commit. Runs from OnAfterRenderAsync; swallows the race where the
    // element is not yet realized.
    private async Task ApplyPendingEditFocusAsync()
    {
        if (focusEditInput)
        {
            focusEditInput = false;
            try
            {
                await editInput.FocusAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // input not yet realized, or its element ref went stale before focus ran; ignore.
            }

            return;
        }

        if (focusActiveCell)
        {
            focusActiveCell = false;
            try
            {
                await activeCellElement.FocusAsync();
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSException)
            {
                // active cell not realized (virtualized out) or its ref went stale; ignore.
            }
        }
    }
}
