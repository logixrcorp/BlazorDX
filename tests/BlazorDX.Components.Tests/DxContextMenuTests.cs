using BlazorDX.Components;
using BlazorDX.Interop;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Cursor-positioned open + keyboard nav for the styled context menu.</summary>
public sealed class DxContextMenuTests : TestContext
{
    private static readonly IReadOnlyList<MenuItem> Items =
    [
        new MenuItem("Cut"),
        new MenuItem("Paste", Disabled: true),
        new MenuItem("Delete"),
    ];

    public DxContextMenuTests()
    {
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    /// <param name="exitDurationMs">
    /// Exit-animation hold. A closing test passes 0: <c>PresenceBoundary</c> otherwise keeps the
    /// node mounted for a real 120ms timer, so the assertion becomes a race between that timer and
    /// bUnit's retry window — which is what made the Escape test flake under parallel load. Zero
    /// removes the timer rather than waiting longer for it, and nothing about *closing* is being
    /// tested by the animation.
    /// </param>
    private IRenderedComponent<DxContextMenu> RenderMenu(int exitDurationMs = 120) =>
        RenderComponent<DxContextMenu>(parameters => parameters
            .Add(m => m.Items, Items)
            .Add(m => m.ExitDurationMs, exitDurationMs)
            .Add(m => m.ChildContent, (RenderFragment)(b => b.AddContent(0, "Region"))));

    [Fact]
    public void Menu_is_closed_until_right_click()
    {
        IRenderedComponent<DxContextMenu> menu = RenderMenu();
        Assert.Empty(menu.FindAll("[role=menu]"));
    }

    [Fact]
    public void Right_click_opens_menu_at_cursor_with_items()
    {
        IRenderedComponent<DxContextMenu> menu = RenderMenu();

        menu.Find(".dx-ctx-region").TriggerEvent(
            "oncontextmenu", new MouseEventArgs { ClientX = 120, ClientY = 64 });

        string markup = menu.Markup;
        Assert.Contains("role=\"menu\"", markup);
        Assert.Equal(3, menu.FindAll("[role=menuitem]").Count);
        Assert.Contains("left:120px", markup);
        Assert.Contains("top:64px", markup);
    }

    [Fact]
    public void Disabled_item_is_marked_disabled()
    {
        IRenderedComponent<DxContextMenu> menu = RenderMenu();
        menu.Find(".dx-ctx-region").TriggerEvent(
            "oncontextmenu", new MouseEventArgs { ClientX = 1, ClientY = 1 });

        var items = menu.FindAll("[role=menuitem]");
        Assert.Contains("disabled", items[1].OuterHtml);   // Paste
    }

    [Fact]
    public void Escape_closes_the_menu()
    {
        IRenderedComponent<DxContextMenu> menu = RenderMenu(exitDurationMs: 0);
        menu.Find(".dx-ctx-region").TriggerEvent(
            "oncontextmenu", new MouseEventArgs { ClientX = 1, ClientY = 1 });

        menu.Find("[role=menu]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        // With no exit hold, PresenceBoundary releases the node on the render that follows the
        // close — so this waits on a render rather than on a 120ms timer it might lose a race to.
        menu.WaitForAssertion(() => Assert.Empty(menu.FindAll("[role=menu]")));
    }
}
