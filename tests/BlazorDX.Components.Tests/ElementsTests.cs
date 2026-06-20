using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Badge, Chip, Avatar, Card, and Slider — the display/element leaf components.</summary>
public sealed class ElementsTests : TestContext
{
    [Fact]
    public void Badge_renders_text_and_variant_class()
    {
        IRenderedComponent<DxBadge> badge = RenderComponent<DxBadge>(p => p
            .Add(b => b.Text, "Active")
            .Add(b => b.Variant, "success"));

        Assert.Contains("dx-badge-success", badge.Markup);
        Assert.Contains("Active", badge.Markup);
    }

    [Fact]
    public void Badge_dot_renders_no_text()
    {
        IRenderedComponent<DxBadge> badge = RenderComponent<DxBadge>(p => p
            .Add(b => b.Dot, true)
            .Add(b => b.Variant, "danger"));

        Assert.Single(badge.FindAll(".dx-badge-dot"));
        Assert.Empty(badge.Find(".dx-badge").TextContent.Trim());
    }

    [Fact]
    public void Chip_shows_a_remove_button_only_when_dismissible_and_raises_on_dismiss()
    {
        Assert.Empty(RenderComponent<DxChip>(p => p.Add(c => c.Text, "Tag")).FindAll(".dx-chip-remove"));

        int dismissed = 0;
        IRenderedComponent<DxChip> chip = RenderComponent<DxChip>(p => p
            .Add(c => c.Text, "Rust")
            .Add(c => c.Dismissible, true)
            .Add(c => c.OnDismiss, EventCallback.Factory.Create(this, () => dismissed++)));

        Assert.Contains("Rust", chip.Find(".dx-chip-label").TextContent);
        chip.Find(".dx-chip-remove").Click();
        Assert.Equal(1, dismissed);
    }

    [Fact]
    public void Avatar_renders_an_image_when_given_a_url()
    {
        IRenderedComponent<DxAvatar> avatar = RenderComponent<DxAvatar>(p => p
            .Add(a => a.ImageUrl, "/me.png")
            .Add(a => a.AltText, "Me"));

        var img = avatar.Find("img");
        Assert.Equal("/me.png", img.GetAttribute("src"));
        Assert.Equal("Me", img.GetAttribute("alt"));
        Assert.Equal("img", avatar.Find(".dx-avatar").GetAttribute("role"));
    }

    [Fact]
    public void Avatar_falls_back_to_initials()
    {
        IRenderedComponent<DxAvatar> avatar = RenderComponent<DxAvatar>(p => p
            .Add(a => a.Initials, "AL")
            .Add(a => a.AltText, "Ada Lovelace"));

        Assert.Empty(avatar.FindAll("img"));
        Assert.Equal("AL", avatar.Find(".dx-avatar-initials").TextContent);
        Assert.Equal("Ada Lovelace", avatar.Find(".dx-avatar").GetAttribute("aria-label"));
    }

    [Fact]
    public void Card_renders_header_and_footer_only_when_supplied()
    {
        IRenderedComponent<DxCard> bare = RenderComponent<DxCard>(p => p
            .Add(c => c.ChildContent, "Body"));
        Assert.Empty(bare.FindAll(".dx-card-header"));
        Assert.Empty(bare.FindAll(".dx-card-footer"));
        Assert.Equal("Body", bare.Find(".dx-card-body").TextContent);

        IRenderedComponent<DxCard> full = RenderComponent<DxCard>(p => p
            .Add(c => c.Header, "Title")
            .Add(c => c.ChildContent, "Body")
            .Add(c => c.Footer, "Footer"));
        Assert.Equal("Title", full.Find(".dx-card-header").TextContent);
        Assert.Equal("Footer", full.Find(".dx-card-footer").TextContent);
    }

    [Fact]
    public void Slider_renders_a_range_input_with_bounds_and_value()
    {
        IRenderedComponent<DxSlider> slider = RenderComponent<DxSlider>(p => p
            .Add(s => s.Value, 7.0)
            .Add(s => s.Min, 0.0)
            .Add(s => s.Max, 11.0)
            .Add(s => s.Step, 1.0)
            .Add(s => s.ShowValue, true)
            .Add(s => s.AriaLabel, "Volume"));

        var input = slider.Find("input.dx-slider-input");
        Assert.Equal("range", input.GetAttribute("type"));
        Assert.Equal("0", input.GetAttribute("min"));
        Assert.Equal("11", input.GetAttribute("max"));
        Assert.Equal("7", input.GetAttribute("value"));
        Assert.Equal("Volume", input.GetAttribute("aria-label"));
        Assert.Equal("7", slider.Find(".dx-slider-value").TextContent);
    }

    [Fact]
    public void Slider_raises_value_changed_on_input()
    {
        double captured = -1;
        IRenderedComponent<DxSlider> slider = RenderComponent<DxSlider>(p => p
            .Add(s => s.Value, 7.0)
            .Add(s => s.Max, 11.0)
            .Add(s => s.ValueChanged, EventCallback.Factory.Create<double>(this, v => captured = v)));

        slider.Find("input.dx-slider-input").Input("4");
        Assert.Equal(4, captured);
    }
}
