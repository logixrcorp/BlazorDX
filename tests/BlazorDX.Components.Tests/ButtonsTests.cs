using BlazorDX.Components;
using BlazorDX.Compute;
using BlazorDX.Interop;
using BlazorDX.Primitives.Overlays;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Button family: Button, ButtonGroup, Toolbar, and the DxMenu-composing SplitButton.</summary>
public sealed class ButtonsTests : TestContext
{
    public ButtonsTests()
    {
        // DxSplitButton composes DxMenu, which needs the overlay interop (null bridge off-browser).
        Services.AddScoped<IOverlayInterop, NullOverlayInterop>();
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

    [Fact]
    public void Button_renders_variant_and_fires_click()
    {
        int clicks = 0;
        IRenderedComponent<DxButton> button = RenderComponent<DxButton>(p => p
            .Add(b => b.Text, "Save")
            .Add(b => b.Variant, "danger")
            .Add(b => b.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, _ => clicks++)));

        var el = button.Find("button.dx-btn");
        Assert.Contains("dx-btn-danger", el.ClassName);
        Assert.Equal("Save", el.TextContent);
        el.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Button_disabled_renders_disabled_attribute()
    {
        IRenderedComponent<DxButton> button = RenderComponent<DxButton>(p => p
            .Add(b => b.Text, "X")
            .Add(b => b.Disabled, true));
        Assert.True(button.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Button_splats_extra_attributes()
    {
        IRenderedComponent<DxButton> button = RenderComponent<DxButton>(p => p
            .Add(b => b.Text, "X")
            .AddUnmatched("title", "Tooltip text")
            .AddUnmatched("data-test", "y"));

        var el = button.Find("button");
        Assert.Equal("Tooltip text", el.GetAttribute("title"));
        Assert.Equal("y", el.GetAttribute("data-test"));
    }

    [Fact]
    public void ButtonGroup_and_Toolbar_have_the_right_roles()
    {
        IRenderedComponent<DxButtonGroup> group = RenderComponent<DxButtonGroup>(p => p
            .Add(g => g.AriaLabel, "Style")
            .AddChildContent("<button class=\"dx-btn\">A</button>"));
        Assert.Equal("group", group.Find(".dx-btn-group").GetAttribute("role"));
        Assert.Equal("Style", group.Find(".dx-btn-group").GetAttribute("aria-label"));

        IRenderedComponent<DxToolbar> toolbar = RenderComponent<DxToolbar>(p => p
            .AddChildContent("<span>x</span>"));
        Assert.Equal("toolbar", toolbar.Find(".dx-toolbar").GetAttribute("role"));
    }

    [Fact]
    public void SplitButton_renders_primary_and_a_menu_trigger()
    {
        string action = "(none)";
        IRenderedComponent<DxSplitButton> split = RenderComponent<DxSplitButton>(p => p
            .Add(s => s.Text, "Save")
            .Add(s => s.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, _ => action = "primary"))
            .Add(s => s.Items, new List<MenuItem>
            {
                new("Save as draft", () => action = "draft"),
            }));

        // Primary action button.
        var primary = split.Find(".dx-split-primary");
        Assert.Equal("Save", primary.TextContent);
        primary.Click();
        Assert.Equal("primary", action);

        // The caret toggle (DxMenu trigger) is present.
        Assert.Single(split.FindAll(".dx-split-toggle"));
        Assert.Single(split.FindAll(".dx-menu-trigger"));
    }
}
