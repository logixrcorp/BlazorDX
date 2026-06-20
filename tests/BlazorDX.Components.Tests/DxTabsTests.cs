using BlazorDX.Components;
using BlazorDX.Primitives.Navigation;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + activation behavior for the styled tabs.</summary>
public sealed class DxTabsTests : TestContext
{
    private static IReadOnlyList<TabItem> BuildTabs() =>
    [
        new TabItem("One", b => b.AddContent(0, "First panel")),
        new TabItem("Two", b => b.AddContent(0, "Second panel")),
        new TabItem("Three", b => b.AddContent(0, "Third panel")),
    ];

    [Fact]
    public void Shows_the_selected_panel_and_marks_the_tab()
    {
        IRenderedComponent<DxTabs> tabs = RenderComponent<DxTabs>(parameters => parameters
            .Add(t => t.Items, BuildTabs())
            .Add(t => t.SelectedIndex, 0));

        Assert.Contains("First panel", tabs.Markup);
        Assert.DoesNotContain("Second panel", tabs.Markup);
        var tabButtons = tabs.FindAll("[role=tab]");
        Assert.Equal("true", tabButtons[0].GetAttribute("aria-selected"));
        Assert.Equal("0", tabButtons[0].GetAttribute("tabindex"));
        Assert.Equal("-1", tabButtons[1].GetAttribute("tabindex"));
    }

    [Fact]
    public void Clicking_a_tab_raises_SelectedIndexChanged()
    {
        int selected = 0;
        IRenderedComponent<DxTabs> tabs = RenderComponent<DxTabs>(parameters => parameters
            .Add(t => t.Items, BuildTabs())
            .Add(t => t.SelectedIndex, selected)
            .Add(t => t.SelectedIndexChanged, index => selected = index));

        tabs.FindAll("[role=tab]")[1].Click();
        Assert.Equal(1, selected);
    }

    [Fact]
    public void ArrowRight_activates_the_next_tab()
    {
        int selected = 0;
        IRenderedComponent<DxTabs> tabs = RenderComponent<DxTabs>(parameters => parameters
            .Add(t => t.Items, BuildTabs())
            .Add(t => t.SelectedIndex, selected)
            .Add(t => t.SelectedIndexChanged, index => selected = index));

        tabs.Find("[role=tablist]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(1, selected);
    }
}
