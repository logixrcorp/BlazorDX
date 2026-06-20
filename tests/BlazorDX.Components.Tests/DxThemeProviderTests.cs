using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>The theme provider just scopes tokens; verify the scope attributes.</summary>
public sealed class DxThemeProviderTests : TestContext
{
    [Fact]
    public void Applies_the_theme_scope_attribute()
    {
        IRenderedComponent<DxThemeProvider> provider = RenderComponent<DxThemeProvider>(parameters => parameters
            .Add(p => p.Theme, "dark")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "content"))));

        var root = provider.Find(".dx-theme-root");
        Assert.Equal("dark", root.GetAttribute("data-dx-theme"));
        Assert.Contains("content", root.TextContent);
    }

    [Fact]
    public void Applies_an_accent_token_override()
    {
        IRenderedComponent<DxThemeProvider> provider = RenderComponent<DxThemeProvider>(parameters => parameters
            .Add(p => p.Theme, "light")
            .Add(p => p.Accent, "#16a34a"));

        Assert.Contains("--dx-accent:#16a34a", provider.Find(".dx-theme-root").GetAttribute("style"));
    }

    [Fact]
    public void Omits_the_style_when_no_accent_override()
    {
        IRenderedComponent<DxThemeProvider> provider = RenderComponent<DxThemeProvider>(parameters => parameters
            .Add(p => p.Theme, "light"));

        Assert.False(provider.Find(".dx-theme-root").HasAttribute("style"));
    }
}
