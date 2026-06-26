using AngleSharp.Dom;
using BlazorDX.Documents;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The read-only spreadsheet viewer: a tab per sheet, tab switching, and a grid whose
/// header/data cells carry the right ARIA roles with aria-rowcount/colcount reflecting
/// the full sheet under virtualization.
/// </summary>
public sealed class DxSpreadsheetViewerTests : TestContext
{
    public DxSpreadsheetViewerTests()
    {
        // DxVirtualize injects the DOM bridge; off-browser the null bridge stands in.
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static Workbook SampleWorkbook() =>
        new(
        [
            new Worksheet("People",
            [
                ["Name", "Age"],
                ["Alice", "30"],
                ["Bob", "25"],
            ], 2),
            new Worksheet("Cities",
            [
                ["City", "Pop"],
                ["Austin", "1000000"],
            ], 2),
        ]);

    private IRenderedComponent<DxSpreadsheetViewer> Render(Workbook workbook) =>
        RenderComponent<DxSpreadsheetViewer>(p => p
            .Add(v => v.Workbook, workbook)
            .Add(v => v.ViewportHeight, 600)
            .Add(v => v.RowHeight, 29));

    [Fact]
    public void Renders_a_tab_per_sheet()
    {
        IRenderedComponent<DxSpreadsheetViewer> viewer = Render(SampleWorkbook());

        IElement tablist = viewer.Find("[role=tablist]");
        Assert.Equal("Worksheets", tablist.GetAttribute("aria-label"));

        IRefreshableElementCollection<IElement> tabs = viewer.FindAll("[role=tab]");
        Assert.Equal(2, tabs.Count);
        Assert.Equal("People", tabs[0].TextContent);
        Assert.Equal("Cities", tabs[1].TextContent);

        // First tab selected by default.
        Assert.Equal("true", tabs[0].GetAttribute("aria-selected"));
        Assert.Equal("false", tabs[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public void Switching_tabs_shows_that_sheet()
    {
        IRenderedComponent<DxSpreadsheetViewer> viewer = Render(SampleWorkbook());

        // Sheet 1 (People) is showing: its header "Name" is present, "City" is not.
        Assert.Contains("Name", viewer.Markup);
        Assert.DoesNotContain("Austin", viewer.Markup);

        viewer.FindAll("[role=tab]")[1].Click();

        Assert.Equal("true", viewer.FindAll("[role=tab]")[1].GetAttribute("aria-selected"));
        Assert.Contains("City", viewer.Markup);
        Assert.Contains("Austin", viewer.Markup);
    }

    [Fact]
    public void Header_row_uses_columnheader_roles()
    {
        IRenderedComponent<DxSpreadsheetViewer> viewer = Render(SampleWorkbook());

        IElement grid = viewer.Find("[role=grid]");
        IElement headRow = grid.QuerySelector(".dx-sheet-headrow")!;
        Assert.Equal("row", headRow.GetAttribute("role"));

        IHtmlCollection<IElement> headers = headRow.QuerySelectorAll("[role=columnheader]");
        // One corner gutter header + 2 data column headers.
        Assert.Equal(3, headers.Length);
        Assert.Contains(headers, h => h.TextContent == "Name");
        Assert.Contains(headers, h => h.TextContent == "Age");
    }

    [Fact]
    public void Data_rows_render_with_row_and_gridcell_roles()
    {
        IRenderedComponent<DxSpreadsheetViewer> viewer = Render(SampleWorkbook());

        // Data rows live in the virtualized window; each is role="row" of gridcells.
        IRefreshableElementCollection<IElement> dataRows = viewer.FindAll(".dx-sheet-row[role=row]");
        Assert.Equal(2, dataRows.Count); // Alice + Bob (header is a separate headrow)

        IElement alice = dataRows[0];
        Assert.Contains(alice.QuerySelectorAll("[role=gridcell]"), c => c.TextContent == "Alice");
        Assert.Contains(alice.QuerySelectorAll("[role=gridcell]"), c => c.TextContent == "30");
        // Row gutter label is a rowheader, not a gridcell.
        Assert.Equal("rowheader", alice.QuerySelector(".dx-sheet-rowlabel")!.GetAttribute("role"));
    }

    [Fact]
    public void Grid_reports_full_size_via_aria_row_and_col_count()
    {
        IRenderedComponent<DxSpreadsheetViewer> viewer = Render(SampleWorkbook());

        IElement grid = viewer.Find("[role=grid]");
        // People: 3 rows total (header + 2 data); 2 data cols + 1 gutter col = 3.
        Assert.Equal("3", grid.GetAttribute("aria-rowcount"));
        Assert.Equal("3", grid.GetAttribute("aria-colcount"));
        Assert.Equal("true", grid.GetAttribute("aria-readonly"));
    }

    [Fact]
    public void Empty_sheet_shows_a_message_not_a_grid()
    {
        Workbook workbook = new([new Worksheet("Blank", [], 0)]);
        IRenderedComponent<DxSpreadsheetViewer> viewer = Render(workbook);

        Assert.Single(viewer.FindAll("[role=tab]"));
        Assert.Empty(viewer.FindAll("[role=grid]"));
        Assert.Contains("empty", viewer.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private IRenderedComponent<DxSpreadsheetViewer> RenderEditable(Workbook workbook) =>
        RenderComponent<DxSpreadsheetViewer>(p => p
            .Add(v => v.Workbook, workbook)
            .Add(v => v.Editable, true)
            .Add(v => v.ViewportHeight, 600)
            .Add(v => v.RowHeight, 29));

    [Fact]
    public void Editable_grid_moving_the_active_cell_re_renders_without_corrupting_the_tree()
    {
        // Regression: the active cell's ElementReferenceCapture used to be emitted only
        // when the cell was active. Moving the active cell then removed that frame in
        // place, and Blazor's differ threw "Unexpected frame type during RemoveOldFrame:
        // ElementReferenceCapture" (followed by cascade NullReference / missing-event-id
        // errors). Clicking from one cell to another must re-render cleanly, leaving
        // exactly one active cell.
        IRenderedComponent<DxSpreadsheetViewer> viewer = RenderEditable(SampleWorkbook());

        IReadOnlyList<IElement> cells = viewer.FindAll(".dx-sheet-editcell");
        Assert.NotEmpty(cells);

        cells[0].Click();                                  // (0,0) — already the default active cell
        viewer.FindAll(".dx-sheet-editcell")[1].Click();   // move active → the diff path that crashed

        Assert.Single(viewer.FindAll(".dx-sheet-cell-active"));
    }

    [Fact]
    public void Editable_grid_edits_a_cell_and_raises_workbook_changed()
    {
        // Entering/leaving edit mode swaps a cell's content for an <input> subtree (which
        // carries its own reference capture) and back — the other render path that touches
        // reference captures. Double-click to edit, type, commit, and confirm the change
        // surfaces without a render-tree fault.
        Workbook? changed = null;
        IRenderedComponent<DxSpreadsheetViewer> viewer = RenderComponent<DxSpreadsheetViewer>(p => p
            .Add(v => v.Workbook, SampleWorkbook())
            .Add(v => v.Editable, true)
            .Add(v => v.ViewportHeight, 600)
            .Add(v => v.RowHeight, 29)
            .Add(v => v.WorkbookChanged, wb => changed = wb));

        // Double-click a data cell to open the editor, type a new value, commit with Enter.
        viewer.FindAll(".dx-sheet-row .dx-sheet-editcell")[0].DoubleClick();
        IElement input = viewer.Find(".dx-sheet-cell-input");
        input.Input("Carol");
        input.KeyDown("Enter");

        Assert.NotNull(changed);
        Assert.True(viewer.Instance.IsDirty);
    }
}
