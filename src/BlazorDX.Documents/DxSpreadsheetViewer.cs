using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using BlazorDX.Components;

namespace BlazorDX.Documents;

/// <summary>
/// Tier 2 styled, read-only, multi-sheet spreadsheet viewer over a parsed
/// <see cref="Workbook"/>. Sheet tabs form a WAI-ARIA <c>role="tablist"</c>; the
/// active sheet renders as a virtualized <c>role="grid"</c> whose row window comes
/// from the reused <c>DxVirtualize&lt;T&gt;</c> primitive. Styling is CSS-variable
/// driven (see dx-spreadsheet.css). Inherits its sheet/keyboard state from
/// <see cref="SpreadsheetViewerPrimitive"/>.
/// </summary>
/// <remarks>
/// We deliberately do <b>not</b> use <c>DxDataGrid&lt;TRow&gt;</c> here. That grid's
/// binding is source-generated for compile-time-known column types, but a spreadsheet's
/// columns are a runtime-defined schema (ADR-0009). So we window the rows with the
/// general-purpose <c>DxVirtualize&lt;T&gt;</c> primitive and render cells straight from
/// the workbook's <c>string</c> grid. The grid declares <c>aria-rowcount</c>/
/// <c>aria-colcount</c> for the <em>full</em> sheet so assistive technology reports the
/// true size even though only the scroll window is in the DOM (WCAG 1.3.1).
/// </remarks>
public sealed partial class DxSpreadsheetViewer : SpreadsheetViewerPrimitive
{
    private ElementReference[] tabElements = [];
    private int pendingFocus = -1;

    /// <summary>Optional extra CSS class on the viewer root.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Pixel height of each data row; drives the virtualization math.</summary>
    [Parameter] public int RowHeight { get; set; } = 29;

    /// <summary>Pixel height of the scrollable grid viewport.</summary>
    [Parameter] public int ViewportHeight { get; set; } = 420;

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (tabElements.Length != Sheets.Count)
        {
            tabElements = new ElementReference[Sheets.Count];
        }

        SyncEditState();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-sheet-viewer {Class}".TrimEnd());

        BuildTabStrip(builder);
        BuildActiveSheet(builder);

        builder.CloseElement();
    }

    private void BuildTabStrip(RenderTreeBuilder builder)
    {
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-sheet-tabs");
        builder.AddAttribute(4, "role", "tablist");
        builder.AddAttribute(5, "aria-label", "Worksheets");
        builder.AddAttribute(6, "onkeydown",
            EventCallback.Factory.Create<KeyboardEventArgs>(this, OnTabStripKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(7, "onkeydown", true);

        for (int i = 0; i < Sheets.Count; i++)
        {
            Worksheet sheet = Sheets[i];
            int captured = i;
            bool selected = IsActive(i);

            builder.OpenElement(8, "button");
            builder.SetKey(sheet);
            builder.AddAttribute(9, "type", "button");
            builder.AddAttribute(10, "id", TabId(i));
            builder.AddAttribute(11, "class", selected ? "dx-sheet-tab dx-sheet-tab-selected" : "dx-sheet-tab");
            builder.AddAttribute(12, "role", "tab");
            builder.AddAttribute(13, "aria-selected", selected ? "true" : "false");
            builder.AddAttribute(14, "aria-controls", PanelId(i));
            builder.AddAttribute(15, "tabindex", selected ? "0" : "-1");
            builder.AddAttribute(16, "onclick", EventCallback.Factory.Create(this, () => OnTabClickAsync(captured)));
            builder.AddElementReferenceCapture(17, element => CaptureTab(captured, element));
            builder.AddContent(18, sheet.Name);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildActiveSheet(RenderTreeBuilder builder)
    {
        builder.OpenElement(20, "div");
        // NOTE: must NOT be "dx-sheet-panel" — that class belongs to the DxSheet
        // offcanvas overlay (dx-overlay.css), which styles it `position: fixed`. Since
        // both stylesheets load globally, reusing the name pulled the worksheet panel
        // out of flow and dropped the grid on top of the page footer.
        builder.AddAttribute(21, "class", "dx-sheet-tabpanel");
        builder.AddAttribute(22, "role", "tabpanel");
        if (ActiveSheet is not null)
        {
            builder.AddAttribute(23, "id", PanelId(ActiveIndex));
            builder.AddAttribute(24, "aria-labelledby", TabId(ActiveIndex));
        }

        Worksheet? sheet = ActiveSheet;
        if (sheet is null || sheet.Rows.Count == 0)
        {
            builder.OpenElement(25, "div");
            builder.AddAttribute(26, "class", "dx-sheet-empty");
            builder.AddContent(27, sheet is null ? "No worksheet to display." : "This sheet is empty.");
            builder.CloseElement();
            builder.CloseElement(); // tabpanel
            return;
        }

        if (Editable)
        {
            BuildEditableGrid(builder, sheet);
        }
        else
        {
            BuildGrid(builder, sheet);
        }

        builder.CloseElement(); // tabpanel
    }

    private void BuildGrid(RenderTreeBuilder builder, Worksheet sheet)
    {
        // The first row is the header; the remainder are data rows. The grid's
        // aria-rowcount/colcount reflect the FULL sheet (header + every data row,
        // and the +1 row-label gutter column) — not just the virtualized window.
        IReadOnlyList<string> header = sheet.Rows[0];
        int dataRowCount = sheet.Rows.Count - 1;
        int dataColumns = sheet.ColumnCount;

        builder.OpenElement(30, "div");
        builder.AddAttribute(31, "class", "dx-sheet-grid");
        builder.AddAttribute(32, "role", "grid");
        builder.AddAttribute(33, "aria-readonly", "true");
        builder.AddAttribute(34, "aria-label", $"{sheet.Name} worksheet");
        builder.AddAttribute(35, "aria-rowcount", (sheet.Rows.Count).ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(36, "aria-colcount", (dataColumns + 1).ToString(CultureInfo.InvariantCulture));

        // Header row (aria-rowindex 1): a corner cell for the row-label gutter, then
        // one column header per sheet column.
        builder.OpenElement(40, "div");
        builder.AddAttribute(41, "class", "dx-sheet-headrow");
        builder.AddAttribute(42, "role", "row");
        builder.AddAttribute(43, "aria-rowindex", "1");

        builder.OpenElement(44, "span");
        builder.AddAttribute(45, "class", "dx-sheet-colhead dx-sheet-rowlabel");
        builder.AddAttribute(46, "role", "columnheader");
        builder.AddAttribute(47, "aria-colindex", "1");
        builder.AddContent(48, "");
        builder.CloseElement();

        for (int c = 0; c < dataColumns; c++)
        {
            builder.OpenElement(50, "span");
            builder.SetKey(c);
            builder.AddAttribute(51, "class", "dx-sheet-colhead");
            builder.AddAttribute(52, "role", "columnheader");
            builder.AddAttribute(53, "aria-colindex", (c + 2).ToString(CultureInfo.InvariantCulture));
            builder.AddContent(54, c < header.Count ? header[c] : string.Empty);
            builder.CloseElement();
        }

        builder.CloseElement(); // header row

        // Data rows, windowed. DxVirtualize hosts the rowgroup; its spacer/item
        // wrappers are role="presentation" so they don't break the grid > row > cell
        // chain. Each ChildContent render is one role="row" of role="gridcell".
        builder.OpenComponent<DxVirtualize<int>>(60);
        builder.AddComponentParameter(61, nameof(DxVirtualize<int>.Items), DataRowIndices(dataRowCount));
        builder.AddComponentParameter(62, nameof(DxVirtualize<int>.ItemHeight), RowHeight);
        builder.AddComponentParameter(63, nameof(DxVirtualize<int>.ViewportHeight), ViewportHeight);
        builder.AddComponentParameter(64, nameof(DxVirtualize<int>.Role), "rowgroup");
        builder.AddComponentParameter(65, nameof(DxVirtualize<int>.ItemRole), "presentation");
        // The scroll container is the scrollable region; make it keyboard-reachable
        // (WCAG 2.1.1 / axe scrollable-region-focusable) since cells are not in the tab order.
        builder.AddComponentParameter(67, nameof(DxVirtualize<int>.TabIndex), 0);
        builder.AddComponentParameter(66, nameof(DxVirtualize<int>.ChildContent),
            (RenderFragment<int>)(dataIndex => rowBuilder => BuildDataRow(rowBuilder, sheet, dataIndex, dataColumns)));
        builder.CloseComponent();

        builder.CloseElement(); // grid
    }

    private static void BuildDataRow(RenderTreeBuilder builder, Worksheet sheet, int dataIndex, int dataColumns)
    {
        // dataIndex 0 is the first data row, which lives at sheet.Rows[1]; its
        // aria-rowindex is dataIndex + 2 (header is row 1, 1-based indexing).
        IReadOnlyList<string> row = sheet.Rows[dataIndex + 1];
        int rowNumber = dataIndex + 1; // the spreadsheet's data-row number shown in the gutter

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-sheet-row");
        builder.AddAttribute(2, "role", "row");
        builder.AddAttribute(3, "aria-rowindex", (dataIndex + 2).ToString(CultureInfo.InvariantCulture));

        // Row-label gutter cell (a header cell scoped to its row).
        builder.OpenElement(4, "span");
        builder.AddAttribute(5, "class", "dx-sheet-cell dx-sheet-rowlabel");
        builder.AddAttribute(6, "role", "rowheader");
        builder.AddAttribute(7, "aria-colindex", "1");
        builder.AddContent(8, rowNumber.ToString(CultureInfo.InvariantCulture));
        builder.CloseElement();

        for (int c = 0; c < dataColumns; c++)
        {
            string value = c < row.Count ? row[c] : string.Empty;
            builder.OpenElement(10, "span");
            builder.SetKey(c);
            builder.AddAttribute(11, "class", "dx-sheet-cell");
            builder.AddAttribute(12, "role", "gridcell");
            builder.AddAttribute(13, "aria-colindex", (c + 2).ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(14, "tabindex", "-1");
            builder.AddContent(15, value);
            builder.CloseElement();
        }

        builder.CloseElement(); // row
    }

    // The virtualizer windows over row indices; cells are pulled from the model by index.
    private static IReadOnlyList<int> DataRowIndices(int count)
    {
        int[] indices = new int[count];
        for (int i = 0; i < count; i++)
        {
            indices[i] = i;
        }

        return indices;
    }

    private string TabId(int index) => $"dx-sheet-tab-{ViewerId}-{index}";

    private string PanelId(int index) => $"dx-sheet-panel-{ViewerId}-{index}";

    private string ViewerId { get; } = Guid.NewGuid().ToString("N");

    private void CaptureTab(int index, ElementReference element)
    {
        if (index < tabElements.Length)
        {
            tabElements[index] = element;
        }
    }

    private async Task OnTabClickAsync(int index)
    {
        pendingFocus = index;
        await SetActiveAsync(index);
        SyncEditState(); // an internal tab switch doesn't fire OnParametersSet — reload the buffer
    }

    // Re-route keyboard activation through the primitive, then focus the new tab so
    // the roving tabindex and DOM focus stay in sync.
    private new async Task OnTabStripKeyDownAsync(KeyboardEventArgs args)
    {
        int before = ActiveIndex;
        await base.OnTabStripKeyDownAsync(args);
        if (ActiveIndex != before)
        {
            pendingFocus = ActiveIndex;
            SyncEditState(); // reload the edit buffer for the newly active sheet
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (pendingFocus >= 0 && pendingFocus < tabElements.Length)
        {
            int target = pendingFocus;
            pendingFocus = -1;
            try
            {
                await tabElements[target].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Element not yet rendered; ignore.
            }
        }

        await EnsureGridScrollAsync();
        await ApplyPendingEditFocusAsync();
    }
}
