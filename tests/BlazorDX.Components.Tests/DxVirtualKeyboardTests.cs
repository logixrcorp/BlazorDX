using AngleSharp.Dom;
using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>On-screen keyboard: key entry, shift casing, backspace/space, and Enter.</summary>
public sealed class DxVirtualKeyboardTests : TestContext
{
    private static IElement Key(IRenderedComponent<DxVirtualKeyboard> kb, string face) =>
        kb.FindAll("button.dx-vkey").First(b => b.TextContent == face);

    [Fact]
    public void Renders_a_full_qwerty_with_control_keys()
    {
        IRenderedComponent<DxVirtualKeyboard> kb = RenderComponent<DxVirtualKeyboard>();

        // 10 + 10 + 9 + 7 character keys, plus Shift/Space/Backspace/Enter.
        Assert.Equal(36 + 4, kb.FindAll("button.dx-vkey").Count);
        Assert.NotNull(Key(kb, "q"));
        Assert.NotNull(Key(kb, "Space"));
    }

    [Fact]
    public void Clicking_a_letter_appends_it_to_the_value()
    {
        string value = "";
        IRenderedComponent<DxVirtualKeyboard> kb = RenderComponent<DxVirtualKeyboard>(p => p
            .Add(c => c.Value, "")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => value = v)));

        Key(kb, "h").Click();

        Assert.Equal("h", value);
    }

    [Fact]
    public void Shift_toggles_letter_case_and_number_symbols()
    {
        string value = "ab";
        IRenderedComponent<DxVirtualKeyboard> kb = RenderComponent<DxVirtualKeyboard>(p => p
            .Add(c => c.Value, "ab")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => value = v)));

        kb.Find("button[aria-label=Shift]").Click();

        Assert.Equal("true", kb.Find("button[aria-label=Shift]").GetAttribute("aria-pressed"));
        Assert.NotNull(Key(kb, "Q"));   // letters now upper-case
        Assert.NotNull(Key(kb, "!"));   // "1" shows its shifted symbol

        Key(kb, "Q").Click();
        Assert.Equal("abQ", value);
    }

    [Fact]
    public void Backspace_removes_the_last_character()
    {
        string value = "cat";
        IRenderedComponent<DxVirtualKeyboard> kb = RenderComponent<DxVirtualKeyboard>(p => p
            .Add(c => c.Value, "cat")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => value = v)));

        kb.Find("button[aria-label=Backspace]").Click();

        Assert.Equal("ca", value);
    }

    [Fact]
    public void Space_inserts_a_space_and_enter_raises_the_callback()
    {
        string value = "hi";
        bool entered = false;
        IRenderedComponent<DxVirtualKeyboard> kb = RenderComponent<DxVirtualKeyboard>(p => p
            .Add(c => c.Value, "hi")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => value = v))
            .Add(c => c.OnEnter, EventCallback.Factory.Create(this, () => entered = true)));

        Key(kb, "Space").Click();
        Assert.Equal("hi ", value);

        kb.Find("button[aria-label=Enter]").Click();
        Assert.True(entered);
    }
}
