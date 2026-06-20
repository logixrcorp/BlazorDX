using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Excel-style per-column distinct-value filter menu.</summary>
public sealed class DxDataGridFilterMenuTests : TestContext
{
    public DxDataGridFilterMenuTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "Alpha", Quantity = 1 },
        new() { Name = "Beta", Quantity = 2 },
        new() { Name = "Alpha", Quantity = 3 },
        new() { Name = "Gamma", Quantity = 4 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render() =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowFilterMenu, true));

    [Fact]
    public void Each_header_has_a_funnel_button()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        Assert.Equal(2, grid.FindAll(".dx-grid-funnel").Count);   // Name, Quantity
    }

    [Fact]
    public void Opening_the_menu_lists_distinct_values()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        Assert.Empty(grid.FindAll(".dx-grid-valuemenu"));

        grid.FindAll(".dx-grid-funnel")[0].Click();   // Name column

        // Distinct names: Alpha, Beta, Gamma (Alpha de-duplicated).
        var items = grid.FindAll(".dx-grid-valuemenu-item").Select(l => l.TextContent.Trim()).ToArray();
        Assert.Equal(["Alpha", "Beta", "Gamma"], items);
    }

    [Fact]
    public void Unchecking_a_value_filters_out_its_rows()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        Assert.Equal(4, grid.FindAll(".dx-grid-row").Count);

        grid.FindAll(".dx-grid-funnel")[0].Click();
        // Uncheck "Alpha" (first distinct value).
        grid.FindAll(".dx-grid-valuemenu-item input")[0].Change(false);

        // Two Alpha rows gone -> Beta, Gamma remain.
        Assert.Equal(2, grid.FindAll(".dx-grid-row").Count);
        Assert.DoesNotContain("Alpha", grid.Find(".dx-grid-body").TextContent);
        Assert.Contains("dx-grid-funnel-active", grid.FindAll(".dx-grid-funnel")[0].GetAttribute("class")!);
    }

    [Fact]
    public void Clear_filter_restores_all_rows()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        grid.FindAll(".dx-grid-funnel")[0].Click();
        grid.FindAll(".dx-grid-valuemenu-item input")[0].Change(false);
        Assert.Equal(2, grid.FindAll(".dx-grid-row").Count);

        grid.Find(".dx-grid-valuemenu-clear").Click();

        Assert.Equal(4, grid.FindAll(".dx-grid-row").Count);
        Assert.DoesNotContain("dx-grid-funnel-active", grid.FindAll(".dx-grid-funnel")[0].GetAttribute("class")!);
    }
}
