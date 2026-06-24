using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The skip-to-content link for WCAG 2.4.1 (Bypass Blocks).</summary>
public sealed class DxSkipLinkTests : TestContext
{
    [Fact]
    public void Renders_an_anchor_to_the_default_main_target()
    {
        IRenderedComponent<DxSkipLink> link = RenderComponent<DxSkipLink>();

        var a = link.Find("a.dx-skip-link");
        Assert.Equal("#main-content", a.GetAttribute("href"));
        Assert.Equal("Skip to main content", a.TextContent.Trim());
    }

    [Fact]
    public void Target_and_text_are_configurable()
    {
        IRenderedComponent<DxSkipLink> link = RenderComponent<DxSkipLink>(p => p
            .Add(s => s.TargetId, "content")
            .Add(s => s.Text, "Skip navigation"));

        var a = link.Find("a.dx-skip-link");
        Assert.Equal("#content", a.GetAttribute("href"));
        Assert.Equal("Skip navigation", a.TextContent.Trim());
    }
}
