using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Master/detail: expandable per-row detail panels.</summary>
public sealed class DxDataGridDetailTests : TestContext
{
    public DxDataGridDetailTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "Alpha", Quantity = 10 },
        new() { Name = "Beta", Quantity = 20 },
    ];

    private static RenderFragment<WidgetRow> Detail() =>
        row => builder => builder.AddContent(0, $"Detail of {row.Name}");

    [Fact]
    public void No_twisty_without_a_detail_template()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor()));

        Assert.Empty(grid.FindAll(".dx-grid-detail-toggle"));
    }

    [Fact]
    public void Each_row_gets_a_twisty_when_a_detail_template_is_set()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.DetailTemplate, Detail()));

        Assert.Equal(2, grid.FindAll(".dx-grid-detail-toggle").Count);
        Assert.Empty(grid.FindAll(".dx-grid-detail"));   // collapsed initially
    }

    [Fact]
    public void Expanding_a_row_renders_its_detail_panel()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.DetailTemplate, Detail()));

        grid.FindAll(".dx-grid-detail-toggle")[0].Click();   // expand Alpha

        var detail = grid.Find(".dx-grid-detail");
        Assert.Equal("Detail of Alpha", detail.TextContent);
        Assert.Equal("true", grid.FindAll(".dx-grid-detail-toggle")[0].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Collapsing_removes_the_detail_panel()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.DetailTemplate, Detail()));

        grid.FindAll(".dx-grid-detail-toggle")[0].Click();
        Assert.Single(grid.FindAll(".dx-grid-detail"));

        grid.FindAll(".dx-grid-detail-toggle")[0].Click();
        Assert.Empty(grid.FindAll(".dx-grid-detail"));
    }

    [Fact]
    public void Rows_expand_independently()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.DetailTemplate, Detail()));

        grid.FindAll(".dx-grid-detail-toggle")[1].Click();   // expand Beta only

        var details = grid.FindAll(".dx-grid-detail");
        Assert.Single(details);
        Assert.Equal("Detail of Beta", details[0].TextContent);
    }
}
