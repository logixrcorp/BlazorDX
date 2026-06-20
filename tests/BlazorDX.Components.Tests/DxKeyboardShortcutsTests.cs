using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The keyboard-shortcuts cheat sheet: open/close, listing, label fallback.</summary>
public sealed class DxKeyboardShortcutsTests : TestContext
{
    private static IReadOnlyList<Hotkey> Bindings() =>
    [
        new Hotkey("Ctrl+K", default, "Open command palette"),
        new Hotkey("Ctrl+S", default, "Save"),
        new Hotkey("?", default, null),                 // no description -> labelled by its combo
        new Hotkey("", default, "Unbound, no combo"),   // skipped: no combo
    ];

    [Fact]
    public void Renders_nothing_when_closed()
    {
        IRenderedComponent<DxKeyboardShortcuts> sheet = RenderComponent<DxKeyboardShortcuts>(p => p
            .Add(c => c.Bindings, Bindings())
            .Add(c => c.Open, false));

        Assert.Empty(sheet.FindAll("[role=dialog]"));
    }

    [Fact]
    public void Lists_one_row_per_bound_shortcut()
    {
        IRenderedComponent<DxKeyboardShortcuts> sheet = RenderComponent<DxKeyboardShortcuts>(p => p
            .Add(c => c.Bindings, Bindings())
            .Add(c => c.Open, true));

        // Three of the four bindings have a combo; the comboless one is skipped.
        Assert.Equal(3, sheet.FindAll(".dx-keys-row").Count);
        Assert.DoesNotContain("Unbound, no combo", sheet.Markup);
    }

    [Fact]
    public void Uses_the_combo_as_the_label_when_no_description()
    {
        IRenderedComponent<DxKeyboardShortcuts> sheet = RenderComponent<DxKeyboardShortcuts>(p => p
            .Add(c => c.Bindings, Bindings())
            .Add(c => c.Open, true));

        var labels = sheet.FindAll(".dx-keys-label").Select(n => n.TextContent).ToList();
        Assert.Contains("Open command palette", labels);
        Assert.Contains("?", labels);   // the description-less binding falls back to its combo
    }

    [Fact]
    public void Close_button_requests_close()
    {
        bool? lastOpen = null;
        IRenderedComponent<DxKeyboardShortcuts> sheet = RenderComponent<DxKeyboardShortcuts>(p => p
            .Add(c => c.Bindings, Bindings())
            .Add(c => c.Open, true)
            .Add(c => c.OpenChanged, EventCallback.Factory.Create<bool>(this, v => lastOpen = v)));

        sheet.Find(".dx-keys-close").Click();

        Assert.False(lastOpen);
    }

    [Fact]
    public void Backdrop_click_requests_close()
    {
        bool? lastOpen = null;
        IRenderedComponent<DxKeyboardShortcuts> sheet = RenderComponent<DxKeyboardShortcuts>(p => p
            .Add(c => c.Bindings, Bindings())
            .Add(c => c.Open, true)
            .Add(c => c.OpenChanged, EventCallback.Factory.Create<bool>(this, v => lastOpen = v)));

        sheet.Find(".dx-keys-backdrop").Click();

        Assert.False(lastOpen);
    }

    [Fact]
    public void Empty_bindings_show_a_placeholder()
    {
        IRenderedComponent<DxKeyboardShortcuts> sheet = RenderComponent<DxKeyboardShortcuts>(p => p
            .Add(c => c.Bindings, System.Array.Empty<Hotkey>())
            .Add(c => c.Open, true));

        Assert.Contains("No shortcuts", sheet.Markup);
    }
}
