using BlazorDX.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Rendering of the &lt;kbd&gt; key-cap component: splitting, prettifying, a11y label.</summary>
public sealed class DxKbdTests : TestContext
{
    [Fact]
    public void Splits_the_combo_into_one_kbd_per_key()
    {
        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, "Ctrl+Shift+P"));

        var keys = kbd.FindAll("kbd.dx-kbd");
        Assert.Equal(3, keys.Count);
        Assert.Equal("Ctrl", keys[0].TextContent);
        Assert.Equal("Shift", keys[1].TextContent);
        Assert.Equal("P", keys[2].TextContent);   // single letters are upper-cased
        Assert.Equal(2, kbd.FindAll(".dx-kbd-plus").Count);   // separators between three keys
    }

    [Fact]
    public void Prettifies_special_keys()
    {
        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, "Cmd+ArrowUp"));

        var keys = kbd.FindAll("kbd.dx-kbd");
        Assert.Equal("⌘", keys[0].TextContent);
        Assert.Equal("↑", keys[1].TextContent);
    }

    [Fact]
    public void Exposes_a_spoken_aria_label()
    {
        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, "Ctrl+ArrowLeft"));

        Assert.Equal("Control plus Left arrow", kbd.Find(".dx-kbd-combo").GetAttribute("aria-label"));
    }

    [Fact]
    public void Empty_combo_renders_no_keys()
    {
        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, ""));

        Assert.Empty(kbd.FindAll("kbd"));
    }

    [Fact]
    public void Key_names_are_routed_through_the_localizer()
    {
        // Sentinel, not the English value: "Ctrl" is also the English fallback, so asserting it
        // would pass whether or not the switch arm actually calls the localizer. DxKbd is the
        // rollout's worked example for indirect text -- its user-facing strings live in switch
        // arms rather than at AddContent/AddAttribute call sites.
        Services.AddSingleton<IStringLocalizer<DxKbd>>(new FakeStringLocalizer<DxKbd>());

        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, "Ctrl"));

        Assert.Contains("§§KEYCAPCONTROL§§", kbd.Markup);
        // The spoken form is a separate key: a screen reader must not be given the abbreviation.
        Assert.Contains("§§SPOKENCONTROL§§", kbd.Markup);
    }

    [Fact]
    public void Key_names_fall_back_to_the_invariant_resource()
    {
        // The real factory, so this proves DxKbd.resx's own values round-trip.
        Services.AddLocalization();

        IRenderedComponent<DxKbd> kbd = RenderComponent<DxKbd>(p => p.Add(c => c.Combo, "Ctrl+Shift"));

        Assert.Equal("Ctrl", kbd.FindAll("kbd.dx-kbd")[0].TextContent);
        Assert.Equal("Control plus Shift", kbd.Find(".dx-kbd-combo").GetAttribute("aria-label"));
    }
}
