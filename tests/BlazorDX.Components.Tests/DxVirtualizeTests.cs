using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The virtualizer should render only the window, not the whole list.</summary>
public sealed class DxVirtualizeTests : TestContext
{
    public DxVirtualizeTests()
    {
        Services.AddScoped<IGridDomInterop, NullGridDomInterop>();
    }

    [Fact]
    public void Renders_only_the_visible_window_not_the_whole_list()
    {
        IReadOnlyList<int> items = Enumerable.Range(0, 10_000).ToList();

        IRenderedComponent<DxVirtualize<int>> virt = RenderComponent<DxVirtualize<int>>(parameters => parameters
            .Add(v => v.Items, items)
            .Add(v => v.ItemHeight, 32)
            .Add(v => v.ViewportHeight, 320)
            .Add(v => v.Overscan, 8)
            .Add(v => v.ChildContent, (RenderFragment<int>)(item => b => b.AddContent(0, item))));

        int rendered = virt.FindAll(".dx-virtualize-item").Count;
        // ceil(320/32) + 2*8 = 10 + 16 = 26 — far fewer than 10,000.
        Assert.Equal(26, rendered);
    }

    [Fact]
    public void Renders_the_first_items_at_the_top()
    {
        IReadOnlyList<string> items = Enumerable.Range(0, 500).Select(i => $"item-{i}").ToList();

        IRenderedComponent<DxVirtualize<string>> virt = RenderComponent<DxVirtualize<string>>(parameters => parameters
            .Add(v => v.Items, items)
            .Add(v => v.ChildContent, (RenderFragment<string>)(item => b => b.AddContent(0, item))));

        Assert.Contains("item-0", virt.Markup);
        Assert.DoesNotContain("item-499", virt.Markup); // below the fold
    }
}
