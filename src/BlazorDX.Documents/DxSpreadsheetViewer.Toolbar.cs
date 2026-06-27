using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using BlazorDX.Documents.Formula;
using BlazorDX.Interop;

namespace BlazorDX.Documents;

/// <summary>
/// The editing chrome for <see cref="DxSpreadsheetViewer"/> in <see cref="Editable"/> mode:
/// a <c>role="toolbar"</c> (insert/delete row &amp; column, download) and an Excel-style
/// formula bar (the active cell's A1 address + an input bound to its <b>raw</b> content).
/// </summary>
/// <remarks>
/// Structural edits (insert/delete) mutate the in-memory buffer and recalculate; they do
/// <b>not</b> rewrite formula references (e.g. inserting a row does not shift <c>=B2</c> to
/// <c>=B3</c>) — a documented limitation. Download serializes the edited workbook with
/// <see cref="XlsxWorkbookWriter"/> (formulas preserved) and streams it through the shared
/// <see cref="IGridDomInterop"/> bridge ([JSImport], base64 over the boundary).
/// </remarks>
public sealed partial class DxSpreadsheetViewer
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>The client-side download bridge (browser real / SSR + tests null).</summary>
    [Inject] private IGridDomInterop Interop { get; set; } = default!;

    /// <summary>Shows the editing toolbar + formula bar above the grid. On by default.</summary>
    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>File name used by the toolbar's "Download .xlsx" action.</summary>
    [Parameter] public string DownloadFileName { get; set; } = "workbook.xlsx";

    // ---- Toolbar + formula bar rendering (called from BuildEditableGrid via regions) ----

    private void BuildEditToolbar(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-sheet-toolbar");
        builder.AddAttribute(2, "role", "toolbar");
        builder.AddAttribute(3, "aria-label", "Spreadsheet editing");

        ToolbarButton(builder, 10, "Insert row", "Insert row above the active cell", InsertRowAsync);
        ToolbarButton(builder, 20, "Delete row", "Delete the active row", DeleteRowAsync);
        ToolbarButton(builder, 30, "Insert column", "Insert column before the active cell", InsertColumnAsync);
        ToolbarButton(builder, 40, "Delete column", "Delete the active column", DeleteColumnAsync);

        builder.OpenElement(50, "span");
        builder.AddAttribute(51, "class", "dx-sheet-toolbar-spacer");
        builder.CloseElement();

        ToolbarButton(builder, 60, "Download .xlsx", "Download the workbook as an .xlsx file", DownloadAsync);

        builder.CloseElement();
    }

    private void ToolbarButton(RenderTreeBuilder builder, int seq, string text, string label, Func<Task> handler)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-sheet-toolbar-btn");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "title", label);
        builder.AddAttribute(seq + 5, "onclick", EventCallback.Factory.Create(this, handler));
        builder.AddContent(seq + 6, text);
        builder.CloseElement();
    }

    private void BuildFormulaBar(RenderTreeBuilder builder)
    {
        bool hasCells = EditRowCount > 0 && EditColumnCount > 0;
        bool editingActive = editingRow >= 0 && editingRow == activeRow && editingColumn == activeColumn;
        string address = hasCells ? CellLabel(activeRow, activeColumn) : string.Empty;
        string barValue = editingActive ? editBuffer : RawAt(activeRow, activeColumn);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-sheet-formulabar");

        // The A1 address of the active cell. Decorative — the input carries the full
        // accessible name — so it is hidden from assistive tech to avoid double reading.
        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "class", "dx-sheet-addressbox");
        builder.AddAttribute(4, "aria-hidden", "true");
        builder.AddContent(5, address);
        builder.CloseElement();

        builder.OpenElement(10, "input");
        builder.AddAttribute(11, "type", "text");
        builder.AddAttribute(12, "class", "dx-sheet-formulabar-input");
        builder.AddAttribute(13, "aria-label",
            hasCells ? $"Formula bar, cell {address}" : "Formula bar");
        builder.AddAttribute(14, "spellcheck", "false");
        builder.AddAttribute(15, "value", barValue);
        builder.AddAttribute(16, "disabled", !hasCells);
        builder.AddAttribute(17, "oninput",
            EventCallback.Factory.Create<ChangeEventArgs>(this, OnFormulaBarInput));
        builder.AddAttribute(18, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnFormulaBarKeyDownAsync));
        // The bar owns its own keys; don't let them reach the grid's navigation handler.
        builder.AddEventStopPropagationAttribute(19, "onkeydown", true);
        builder.CloseElement();

        builder.CloseElement();
    }

    // ---- Formula bar behaviour ---------------------------------------------------

    // Editing in the formula bar puts the active cell into edit mode (so the in-cell
    // input mirrors it) but keeps DOM focus in the bar — we do NOT set focusEditInput.
    private void OnFormulaBarInput(ChangeEventArgs args)
    {
        if (editRaw is null || activeRow < 0 || activeColumn < 0)
        {
            return;
        }

        editingRow = activeRow;
        editingColumn = activeColumn;
        editBuffer = args.Value?.ToString() ?? string.Empty;
    }

    private async Task OnFormulaBarKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "Enter":
                await CommitAsync();
                MoveActive(1, 0);
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

    // ---- Structural edits --------------------------------------------------------

    private async Task InsertRowAsync()
    {
        if (editRaw is null)
        {
            return;
        }

        int columns = EditColumnCount;
        var blank = new List<string>(columns);
        for (int c = 0; c < columns; c++)
        {
            blank.Add(string.Empty);
        }

        int at = Math.Clamp(activeRow, 0, editRaw.Count);
        editRaw.Insert(at, blank);
        activeRow = at;
        await AfterStructureChangeAsync();
    }

    private async Task DeleteRowAsync()
    {
        if (editRaw is null || editRaw.Count <= 1)
        {
            return; // keep at least one row
        }

        int at = Math.Clamp(activeRow, 0, editRaw.Count - 1);
        editRaw.RemoveAt(at);
        activeRow = Math.Clamp(activeRow, 0, editRaw.Count - 1);
        await AfterStructureChangeAsync();
    }

    private async Task InsertColumnAsync()
    {
        if (editRaw is null)
        {
            return;
        }

        int at = Math.Clamp(activeColumn, 0, EditColumnCount);
        foreach (List<string> row in editRaw)
        {
            row.Insert(Math.Min(at, row.Count), string.Empty);
        }

        activeColumn = at;
        await AfterStructureChangeAsync();
    }

    private async Task DeleteColumnAsync()
    {
        if (editRaw is null || EditColumnCount <= 1)
        {
            return; // keep at least one column
        }

        int at = Math.Clamp(activeColumn, 0, EditColumnCount - 1);
        foreach (List<string> row in editRaw)
        {
            if (at < row.Count)
            {
                row.RemoveAt(at);
            }
        }

        activeColumn = Math.Clamp(activeColumn, 0, EditColumnCount - 1);
        await AfterStructureChangeAsync();
    }

    // Clears the active cell's content (Delete / Backspace on a non-editing cell).
    private async Task ClearActiveCellAsync()
    {
        if (editRaw is null
            || activeRow < 0 || activeRow >= editRaw.Count
            || activeColumn < 0 || activeColumn >= editRaw[activeRow].Count)
        {
            return;
        }

        if (editRaw[activeRow][activeColumn].Length > 0)
        {
            editRaw[activeRow][activeColumn] = string.Empty;
            IsDirty = true;
            editWorkbook?.SetCell(activeRow, activeColumn, string.Empty); // incremental
            if (WorkbookChanged.HasDelegate)
            {
                await WorkbookChanged.InvokeAsync(BuildEditedWorkbook());
            }
        }

        focusActiveCell = true;
        StateHasChanged();
    }

    // Shared tail for insert/delete: drop any in-flight edit, recompute, surface the new
    // workbook, and re-focus the active cell.
    private async Task AfterStructureChangeAsync()
    {
        editingRow = -1;
        editingColumn = -1;
        editBuffer = string.Empty;
        IsDirty = true;
        RebuildWorkbook(); // dimensions changed — rebuild the dependency graph
        focusActiveCell = true;
        if (WorkbookChanged.HasDelegate)
        {
            await WorkbookChanged.InvokeAsync(BuildEditedWorkbook());
        }

        StateHasChanged();
    }

    // ---- Download ----------------------------------------------------------------

    private async Task DownloadAsync()
    {
        byte[] bytes = XlsxWorkbookWriter.Write(BuildEditedWorkbook());
        await Interop.DownloadBytesAsync(DownloadFileName, XlsxMime, bytes);
    }
}
