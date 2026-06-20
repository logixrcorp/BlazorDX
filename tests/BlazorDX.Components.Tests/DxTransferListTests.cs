using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + move behavior for the composed transfer list.</summary>
public sealed class DxTransferListTests : TestContext
{
    private static readonly IReadOnlyList<string> Items = ["a", "b", "c"];

    public DxTransferListTests()
    {
        // The inner listboxes are inline (no overlay), but DI is needed by the grid
        // compute services the listbox primitive references; provide the no-ops.
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    [Fact]
    public void Partitions_items_into_available_and_selected()
    {
        IRenderedComponent<DxTransferList> transfer = RenderComponent<DxTransferList>(parameters => parameters
            .Add(t => t.Items, Items)
            .Add(t => t.Value, new[] { "b" }));

        var listboxes = transfer.FindAll(".dx-listbox");
        Assert.Equal(2, listboxes.Count);
        // Available pane has a, c; selected pane has b.
        Assert.Contains("a", listboxes[0].TextContent);
        Assert.Contains("c", listboxes[0].TextContent);
        Assert.DoesNotContain("b", listboxes[0].TextContent);
        Assert.Contains("b", listboxes[1].TextContent);
    }

    [Fact]
    public void Move_all_right_puts_every_item_in_selected()
    {
        IReadOnlyCollection<string> bound = [];
        IRenderedComponent<DxTransferList> transfer = RenderComponent<DxTransferList>(parameters => parameters
            .Add(t => t.Items, Items)
            .Add(t => t.Value, bound)
            .Add(t => t.ValueChanged, value => bound = value));

        transfer.Find("[aria-label='Move all to selected']").Click();

        Assert.Equal(["a", "b", "c"], bound);
    }

    [Fact]
    public void Selecting_a_source_item_then_moving_right_transfers_it()
    {
        IReadOnlyCollection<string> bound = [];
        IRenderedComponent<DxTransferList> transfer = RenderComponent<DxTransferList>(parameters => parameters
            .Add(t => t.Items, Items)
            .Add(t => t.Value, bound)
            .Add(t => t.ValueChanged, value => bound = value));

        // Click "a" in the available (first) listbox to check it.
        var available = transfer.FindAll(".dx-listbox")[0];
        available.QuerySelectorAll("[role=option]").First(o => o.TextContent == "a").Click();

        transfer.Find("[aria-label='Move selected to selected']").Click();

        Assert.Equal(["a"], bound);
    }

    [Fact]
    public void Remove_all_empties_the_selected_list()
    {
        IReadOnlyCollection<string> bound = ["a", "b"];
        IRenderedComponent<DxTransferList> transfer = RenderComponent<DxTransferList>(parameters => parameters
            .Add(t => t.Items, Items)
            .Add(t => t.Value, bound)
            .Add(t => t.ValueChanged, value => bound = value));

        transfer.Find("[aria-label='Remove all']").Click();

        Assert.Empty(bound);
    }
}
