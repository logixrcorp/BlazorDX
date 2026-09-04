using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Keyboard cell navigation (the ARIA grid pattern): the active cell moves with
/// the arrow keys, Home/End, Ctrl+Home/End, and clamps at the grid edges.
/// </summary>
public sealed class DxDataGridKeyboardTests : TestContext
{
    public DxDataGridKeyboardTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "Alpha", Quantity = 10 },
        new() { Name = "Beta", Quantity = 20 },
        new() { Name = "Gamma", Quantity = 30 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render(bool keyboard = true) =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.KeyboardNavigation, keyboard));

    private static string Active(IRenderedComponent<DxDataGrid<WidgetRow>> grid) =>
        grid.Find(".dx-grid-cell-active").TextContent.Trim();

    private static void Press(IRenderedComponent<DxDataGrid<WidgetRow>> grid, string key, bool ctrl = false) =>
        grid.Find("[role=grid]").KeyDown(new KeyboardEventArgs { Key = key, CtrlKey = ctrl });

    [Fact]
    public void The_first_cell_is_active_by_default()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        Assert.Single(grid.FindAll(".dx-grid-cell-active"));
        Assert.Equal("Alpha", Active(grid));   // row 0, first column

        // The grid container is the tab stop and points assistive tech at the active
        // cell via aria-activedescendant (valid on role=grid, unlike role=rowgroup).
        var container = grid.Find("[role=grid]");
        var cellId = grid.Find(".dx-grid-cell-active").GetAttribute("id");
        Assert.Equal("0", container.GetAttribute("tabindex"));
        Assert.Equal(cellId, container.GetAttribute("aria-activedescendant"));
    }

    [Fact]
    public void Arrow_keys_move_the_active_cell()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        Press(grid, "ArrowRight");
        Assert.Equal("10", Active(grid));        // (0, Quantity)

        Press(grid, "ArrowDown");
        Assert.Equal("20", Active(grid));        // (1, Quantity)

        Press(grid, "ArrowLeft");
        Assert.Equal("Beta", Active(grid));      // (1, Name)

        Press(grid, "ArrowUp");
        Assert.Equal("Alpha", Active(grid));     // back to (0, Name)
    }

    [Fact]
    public void Home_and_end_jump_to_the_row_edges()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        Press(grid, "End");
        Assert.Equal("10", Active(grid));        // last column of row 0

        Press(grid, "Home");
        Assert.Equal("Alpha", Active(grid));     // first column of row 0
    }

    [Fact]
    public void Ctrl_end_jumps_to_the_last_cell_of_the_grid()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        Press(grid, "End", ctrl: true);
        Assert.Equal("30", Active(grid));        // last row, last column (Gamma's Quantity)

        Press(grid, "Home", ctrl: true);
        Assert.Equal("Alpha", Active(grid));     // first row, first column
    }

    [Fact]
    public void Navigation_clamps_at_the_grid_edges()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        // Already at the top-left corner: Up and Left are no-ops.
        Press(grid, "ArrowUp");
        Press(grid, "ArrowLeft");
        Assert.Equal("Alpha", Active(grid));
    }

    [Fact]
    public void No_active_cell_is_rendered_when_navigation_is_off()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render(keyboard: false);

        Assert.Empty(grid.FindAll(".dx-grid-cell-active"));
        Assert.False(grid.Find("[role=grid]").HasAttribute("tabindex"));
    }
}
