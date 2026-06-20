using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Column chooser: toggling visibility hides columns everywhere.</summary>
public sealed class DxDataGridChooserTests : TestContext
{
    public DxDataGridChooserTests()
    {
        Services.AddScoped<IGridCompute, ManagedGridCompute>();
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    private static List<WidgetRow> Rows() =>
    [
        new() { Name = "Alpha", Quantity = 10 },
        new() { Name = "Beta", Quantity = 20 },
    ];

    private IRenderedComponent<DxDataGrid<WidgetRow>> Render() =>
        RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowColumnChooser, true));

    [Fact]
    public void Chooser_lists_a_checkbox_per_column_when_opened()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();

        Assert.Empty(grid.FindAll(".dx-grid-chooser-panel"));
        grid.Find(".dx-grid-chooser-toggle").Click();

        Assert.Equal(2, grid.FindAll(".dx-grid-chooser-item input").Count);   // Name, Quantity
        Assert.Contains("(2/2)", grid.Find(".dx-grid-chooser-toggle").TextContent);
    }

    [Fact]
    public void Hiding_a_column_removes_its_header_and_cells()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        Assert.Equal(2, grid.FindAll(".dx-grid-th").Count);

        grid.Find(".dx-grid-chooser-toggle").Click();
        // Uncheck the second column ("Quantity").
        grid.FindAll(".dx-grid-chooser-item input")[1].Change(false);

        Assert.Single(grid.FindAll(".dx-grid-th"));
        Assert.Equal("Name", grid.FindAll(".dx-grid-th")[0].TextContent.Trim());
        // Data rows now have one cell each.
        Assert.Single(grid.FindAll(".dx-grid-row")[0].QuerySelectorAll(".dx-grid-cell"));
        Assert.Contains("(1/2)", grid.Find(".dx-grid-chooser-toggle").TextContent);
    }

    [Fact]
    public void The_last_visible_column_cannot_be_hidden()
    {
        IRenderedComponent<DxDataGrid<WidgetRow>> grid = Render();
        grid.Find(".dx-grid-chooser-toggle").Click();

        grid.FindAll(".dx-grid-chooser-item input")[1].Change(false);   // hide Quantity -> 1 left

        // The remaining (only) column's checkbox is disabled so the grid never empties.
        var inputs = grid.FindAll(".dx-grid-chooser-item input");
        Assert.True(inputs[0].HasAttribute("disabled"));
    }

    [Fact]
    public void Hidden_columns_are_excluded_from_csv_export()
    {
        string? csv = null;
        var dom = new CaptureDom(content => csv = content);
        Services.AddScoped<IGridDomInterop>(_ => dom);

        IRenderedComponent<DxDataGrid<WidgetRow>> grid = RenderComponent<DxDataGrid<WidgetRow>>(parameters => parameters
            .Add(g => g.Items, Rows())
            .Add(g => g.Accessor, new WidgetRowGridAccessor())
            .Add(g => g.ShowColumnChooser, true)
            .Add(g => g.ShowExport, true));

        grid.Find(".dx-grid-chooser-toggle").Click();
        grid.FindAll(".dx-grid-chooser-item input")[1].Change(false);   // hide Quantity
        grid.Find(".dx-grid-export").Click();

        Assert.Equal("Name\r\nAlpha\r\nBeta\r\n", csv);
    }

    private sealed class CaptureDom(Action<string> onDownload) : IGridDomInterop
    {
        public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
        public ValueTask<(double, double, double)> MeasureViewportAsync(string id) =>
            ValueTask.FromResult<(double, double, double)>((0, 0, 0));
        public ValueTask SubscribeScrollAsync(string id, Action onScroll) => ValueTask.CompletedTask;
        public ValueTask FocusFirstAsync(string id) => ValueTask.CompletedTask;
        public ValueTask DownloadTextAsync(string filename, string mime, string content)
        {
            onDownload(content);
            return ValueTask.CompletedTask;
        }

        public ValueTask DownloadBytesAsync(string filename, string mime, byte[] content) => ValueTask.CompletedTask;

        public ValueTask<bool> WriteClipboardAsync(string text) => ValueTask.FromResult(true);

        public ValueTask ScrollToAsync(string elementId, double top) => ValueTask.CompletedTask;

        public ValueTask SuppressArrowKeysAsync(string elementId) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
