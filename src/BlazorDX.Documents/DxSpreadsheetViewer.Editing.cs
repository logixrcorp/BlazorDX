using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
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
    // Per-sheet mutable raw content (the edit buffer) and its last computed values.
    private List<List<string>>? editRaw;
    private CellValue[][]? editComputed;
    private int editSheetIndex = -1;

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
            editComputed = null;
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
        Recompute();
    }

    // Runs the formula engine over the whole sheet buffer (header row included; it is
    // just text literals) and caches the typed result grid for display.
    private void Recompute() =>
        editComputed = editRaw is null ? null : FormulaEngine.Recalculate(editRaw);

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
        editComputed is not null && row >= 0 && row < editComputed.Length
        && column >= 0 && column < editComputed[row].Length
            ? editComputed[row][column]
            : CellValue.Blank;

    // ---- Rendering ---------------------------------------------------------------

    private void BuildEditableGrid(RenderTreeBuilder builder, Worksheet sheet)
    {
        IReadOnlyList<string> header = sheet.Rows.Count > 0 ? sheet.Rows[0] : [];
        int dataRowCount = Math.Max(0, EditRowCount - 1);
        int dataColumns = EditColumnCount;

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

        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "class", "dx-sheet-grid dx-sheet-grid-editable");
        builder.AddAttribute(32, "role", "grid");
        builder.AddAttribute(33, "aria-multiselectable", "false");
        builder.AddAttribute(34, "aria-label", $"{sheet.Name} worksheet, editable");
        builder.AddAttribute(35, "aria-rowcount", EditRowCount.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(36, "aria-colcount", (dataColumns + 1).ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(37, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnGridKeyDownAsync));

        // Header row (aria-rowindex 1): row 0 of the buffer. It is editable like any
        // other row, so column labels can be renamed.
        BuildEditableRow(builder, 0, dataColumns, isHeader: true);

        builder.OpenComponent<DxVirtualize<int>>(60);
        builder.AddComponentParameter(61, nameof(DxVirtualize<int>.Items), DataRowIndices(dataRowCount));
        builder.AddComponentParameter(62, nameof(DxVirtualize<int>.ItemHeight), RowHeight);
        builder.AddComponentParameter(63, nameof(DxVirtualize<int>.ViewportHeight), ViewportHeight);
        builder.AddComponentParameter(64, nameof(DxVirtualize<int>.Role), "rowgroup");
        builder.AddComponentParameter(65, nameof(DxVirtualize<int>.ItemRole), "presentation");
        builder.AddComponentParameter(67, nameof(DxVirtualize<int>.TabIndex), 0);
        builder.AddComponentParameter(66, nameof(DxVirtualize<int>.ChildContent),
            (RenderFragment<int>)(dataIndex => rowBuilder =>
                BuildEditableRow(rowBuilder, dataIndex + 1, dataColumns, isHeader: false)));
        builder.CloseComponent();

        builder.CloseElement(); // grid
    }

    // Renders one row of the editable grid. rowIndex is the buffer (full-sheet) index;
    // the header row uses columnheader cells, data rows use editable gridcells.
    private void BuildEditableRow(RenderTreeBuilder builder, int rowIndex, int dataColumns, bool isHeader)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", isHeader ? "dx-sheet-headrow" : "dx-sheet-row");
        builder.AddAttribute(2, "role", "row");
        builder.AddAttribute(3, "aria-rowindex", (rowIndex + 1).ToString(CultureInfo.InvariantCulture));

        // Row-label gutter: an empty corner for the header, the data-row number otherwise.
        builder.OpenElement(4, "span");
        builder.AddAttribute(5, "class", isHeader
            ? "dx-sheet-colhead dx-sheet-rowlabel"
            : "dx-sheet-cell dx-sheet-rowlabel");
        builder.AddAttribute(6, "role", isHeader ? "columnheader" : "rowheader");
        builder.AddAttribute(7, "aria-colindex", "1");
        builder.AddContent(8, isHeader ? string.Empty : rowIndex.ToString(CultureInfo.InvariantCulture));
        builder.CloseElement();

        for (int c = 0; c < dataColumns; c++)
        {
            BuildEditableCell(builder, rowIndex, c, isHeader);
        }

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

        focusActiveCell = true;
        StateHasChanged();
    }

    private void MoveActive(int rowDelta, int colDelta)
    {
        activeRow = Math.Clamp(activeRow + rowDelta, 0, Math.Max(0, EditRowCount - 1));
        activeColumn = Math.Clamp(activeColumn + colDelta, 0, Math.Max(0, EditColumnCount - 1));
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
            Recompute();
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
            catch (InvalidOperationException)
            {
                // input not yet in the DOM; ignore.
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
            catch (InvalidOperationException)
            {
                // active cell not realized (virtualized out); ignore.
            }
        }
    }
}
