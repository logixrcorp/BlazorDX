using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + keyboard behavior for the styled menu (no-op browser bridges).</summary>
public sealed class DxMenuTests : TestContext
{
    private static readonly IReadOnlyList<MenuItem> Items =
    [
        new MenuItem("Rename"),
        new MenuItem("Archive", Disabled: true),
        new MenuItem("Delete"),
    ];

    public DxMenuTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Renders_menu_items_with_roles_when_open()
    {
        IRenderedComponent<DxMenu> menu = RenderComponent<DxMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.Trigger, (RenderFragment)(b => b.AddContent(0, "Open"))));

        string markup = menu.Markup;
        Assert.Contains("role=\"menu\"", markup);
        Assert.Equal(3, menu.FindAll("[role=menuitem]").Count);
        Assert.Contains("Delete", markup);
    }

    [Fact]
    public void Disabled_item_is_marked_disabled()
    {
        IRenderedComponent<DxMenu> menu = RenderComponent<DxMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.Trigger, (RenderFragment)(b => b.AddContent(0, "Open"))));

        var items = menu.FindAll("[role=menuitem]");
        Assert.Contains("disabled", items[1].OuterHtml);   // Archive
        Assert.DoesNotContain("dx-menu-item-disabled", items[0].OuterHtml);
    }

    [Fact]
    public void ArrowDown_moves_active_item_tabindex()
    {
        IRenderedComponent<DxMenu> menu = RenderComponent<DxMenu>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Items, Items)
            .Add(m => m.Trigger, (RenderFragment)(b => b.AddContent(0, "Open"))));

        // First enabled item (index 0) starts active.
        Assert.Equal("0", menu.FindAll("[role=menuitem]")[0].GetAttribute("tabindex"));

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // Index 1 is disabled, so focus rolls to index 2.
        var items = menu.FindAll("[role=menuitem]");
        Assert.Equal("-1", items[0].GetAttribute("tabindex"));
        Assert.Equal("0", items[2].GetAttribute("tabindex"));
    }
}
