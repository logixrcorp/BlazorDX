using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Multi-column sort: Shift+Click adds secondary keys; plain click resets.</summary>
public sealed class DxDataGridMultiSortTests : TestContext
{
    public DxDataGridMultiSortTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "B", Quantity = 1 },
        new() { Name = "A", Quantity = 2 },
        new() { Name = "A", Quantity = 1 },
        new() { Name = "B", Quantity = 2 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render() =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor()));

    private static (string Name, string Qty)[] RowPairs(IRenderedComponent<DxDataGrid<WidgetRow>> grid) =>
        grid.FindAll(".dx-grid-row").Select(r =>
        {
            var cells = r.QuerySelectorAll(".dx-grid-cell");
            return (cells[0].TextContent, cells[1].TextContent);
        }).ToArray();

    [Fact]
    public void Plain_click_sorts_by_one_column_then_toggles()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        grid.FindAll(".dx-grid-th-label")[1].Click();   // Quantity asc
        Assert.Equal(["1", "1", "2", "2"], RowPairs(grid).Select(p => p.Qty));

        grid.FindAll(".dx-grid-th-label")[1].Click();   // Quantity desc
        Assert.Equal(["2", "2", "1", "1"], RowPairs(grid).Select(p => p.Qty));
    }

    [Fact]
    public void Shift_click_adds_a_secondary_sort_key()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        grid.FindAll(".dx-grid-th-label")[0].Click();   // Name asc (primary)
        grid.FindAll(".dx-grid-th-label")[1].Click(new MouseEventArgs { ShiftKey = true });   // + Quantity asc

        // Name asc, then Quantity asc within each name.
        Assert.Equal([("A", "1"), ("A", "2"), ("B", "1"), ("B", "2")], RowPairs(grid));
    }

    [Fact]
    public void Multi_sort_shows_priority_numbers_on_headers()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        grid.FindAll(".dx-grid-th-label")[0].Click();
        grid.FindAll(".dx-grid-th-label")[1].Click(new MouseEventArgs { ShiftKey = true });

        Assert.Contains("▲1", grid.FindAll(".dx-grid-th-label")[0].TextContent);
        Assert.Contains("▲2", grid.FindAll(".dx-grid-th-label")[1].TextContent);
    }

    [Fact]
    public void Plain_click_resets_a_multi_sort_to_a_single_column()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        grid.FindAll(".dx-grid-th-label")[0].Click();
        grid.FindAll(".dx-grid-th-label")[1].Click(new MouseEventArgs { ShiftKey = true });

        // A plain click on Quantity drops the Name key -> single Quantity sort, no badges.
        grid.FindAll(".dx-grid-th-label")[1].Click();

        Assert.Equal(["1", "1", "2", "2"], RowPairs(grid).Select(p => p.Qty));
        Assert.DoesNotContain("▲1", grid.FindAll(".dx-grid-th-label")[0].TextContent);
        Assert.DoesNotContain("2", grid.FindAll(".dx-grid-th-label")[1].TextContent.Replace("Quantity", ""));
    }
}
