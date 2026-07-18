using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The Dx*Editorial family: long-form/magazine-style layout components (hero shell, full-bleed
/// figures, a two-column "spread", pull-quotes, spec sidebars, scrollytelling, and the footer).
/// All are plain markup composition — no interop, no compute backend — so coverage here is
/// mostly "does the right markup/attributes come out for the params given."
/// </summary>
public sealed class EditorialTests : TestContext
{
    [Fact]
    public void Layout_renders_text_only_hero_by_default()
    {
        IRenderedComponent<DxEditorialLayout> layout = RenderComponent<DxEditorialLayout>(p => p
            .Add(l => l.Title, "A Title")
            .Add(l => l.Published, new DateOnly(2026, 7, 17)));

        var header = layout.Find("header");
        Assert.Equal("dx-editorial-hero", header.ClassName);
        Assert.Empty(layout.FindAll("img.dx-editorial-hero-image"));
        Assert.Equal("A Title", layout.Find("h1.dx-editorial-title").TextContent);
        Assert.Contains("July 17, 2026", layout.Find(".dx-editorial-meta").TextContent);
    }

    [Fact]
    public void Layout_renders_photo_hero_when_HeroImageSrc_set()
    {
        IRenderedComponent<DxEditorialLayout> layout = RenderComponent<DxEditorialLayout>(p => p
            .Add(l => l.Title, "A Title")
            .Add(l => l.Published, new DateOnly(2026, 7, 17))
            .Add(l => l.HeroImageSrc, "hero.jpg")
            .Add(l => l.HeroImageAlt, "alt text"));

        var header = layout.Find("header");
        Assert.Contains("dx-editorial-hero--photo", header.ClassName);
        var img = layout.Find("img.dx-editorial-hero-image");
        Assert.Equal("hero.jpg", img.GetAttribute("src"));
        Assert.Equal("alt text", img.GetAttribute("alt"));
        Assert.Equal("eager", img.GetAttribute("loading"));
    }

    [Fact]
    public void Layout_shows_reading_minutes_only_when_set_and_wraps_a_default_footer()
    {
        IRenderedComponent<DxEditorialLayout> withMinutes = RenderComponent<DxEditorialLayout>(p => p
            .Add(l => l.Title, "T").Add(l => l.Published, new DateOnly(2026, 1, 1)).Add(l => l.ReadingMinutes, 7));
        Assert.Contains("7 min read", withMinutes.Find(".dx-editorial-meta").TextContent);

        IRenderedComponent<DxEditorialLayout> withoutMinutes = RenderComponent<DxEditorialLayout>(p => p
            .Add(l => l.Title, "T").Add(l => l.Published, new DateOnly(2026, 1, 1)));
        Assert.DoesNotContain("min read", withoutMinutes.Find(".dx-editorial-meta").TextContent);

        // DxEditorialFooter is always wrapped on, with sensible defaults when Cards is unset.
        Assert.NotEmpty(withoutMinutes.FindAll("a.dx-editorial-footer-card"));
    }

    [Fact]
    public void Footer_renders_custom_cards_over_defaults()
    {
        DxEditorialFooter.FooterCard[] cards = [new("Eyebrow", "Title", "Body", "/somewhere", External: true)];
        IRenderedComponent<DxEditorialFooter> footer = RenderComponent<DxEditorialFooter>(p => p
            .Add(f => f.Cards, cards));

        var card = footer.Find("a.dx-editorial-footer-card");
        Assert.Equal("/somewhere", card.GetAttribute("href"));
        Assert.Equal("_blank", card.GetAttribute("target"));
        Assert.Equal("noopener", card.GetAttribute("rel"));
        Assert.Equal("Title", footer.Find(".dx-editorial-footer-card-title").TextContent);
    }

    [Fact]
    public void Figure_renders_caption_only_when_set()
    {
        IRenderedComponent<DxEditorialFigure> withCaption = RenderComponent<DxEditorialFigure>(p => p
            .Add(f => f.Caption, "A caption")
            .AddChildContent("<img src=\"x.jpg\" alt=\"x\" />"));
        Assert.Equal("A caption", withCaption.Find("figcaption").TextContent);
        Assert.NotNull(withCaption.Find(".dx-editorial-figure-art img"));

        IRenderedComponent<DxEditorialFigure> withoutCaption = RenderComponent<DxEditorialFigure>(p => p
            .AddChildContent("<img src=\"x.jpg\" alt=\"x\" />"));
        Assert.Empty(withoutCaption.FindAll("figcaption"));
    }

    [Fact]
    public void Spread_reverses_column_order_and_renders_spec_card_only_when_set()
    {
        IRenderedComponent<DxEditorialSpread> plain = RenderComponent<DxEditorialSpread>(p => p
            .Add(s => s.ImageSrc, "x.jpg").Add(s => s.ImageAlt, "alt")
            .Add(s => s.Title, "A Title"));
        Assert.DoesNotContain("dx-editorial-spread--reverse", plain.Find("section").ClassName);
        Assert.Empty(plain.FindAll(".dx-editorial-spread-spec"));

        IRenderedComponent<DxEditorialSpread> reversedWithSpec = RenderComponent<DxEditorialSpread>(p => p
            .Add(s => s.ImageSrc, "x.jpg").Add(s => s.ImageAlt, "alt")
            .Add(s => s.Title, "A Title").Add(s => s.Reverse, true)
            .Add(s => s.SpecLabel, "Verified by").Add(s => s.SpecValue, "A Test"));
        Assert.Contains("dx-editorial-spread--reverse", reversedWithSpec.Find("section").ClassName);
        Assert.Equal("Verified by", reversedWithSpec.Find(".dx-editorial-spread-spec-label").TextContent);
        Assert.Equal("A Test", reversedWithSpec.Find(".dx-editorial-spread-spec-value").TextContent);
    }

    [Fact]
    public void PullQuote_renders_attribution_only_when_set()
    {
        IRenderedComponent<DxEditorialPullQuote> withAttribution = RenderComponent<DxEditorialPullQuote>(p => p
            .Add(q => q.Attribution, "Someone").AddChildContent("A quote."));
        Assert.Equal("A quote.", withAttribution.Find("blockquote").TextContent);
        Assert.Equal("Someone", withAttribution.Find("cite").TextContent);

        IRenderedComponent<DxEditorialPullQuote> withoutAttribution = RenderComponent<DxEditorialPullQuote>(p => p
            .AddChildContent("A quote."));
        Assert.Empty(withoutAttribution.FindAll("cite"));
    }

    [Fact]
    public void Sidebar_renders_icon_and_caveat_only_when_set()
    {
        IRenderedComponent<DxEditorialSidebar> bare = RenderComponent<DxEditorialSidebar>(p => p
            .Add(s => s.Title, "A Title").AddChildContent("Body"));
        Assert.Empty(bare.FindAll(".dx-editorial-sidebar-title span"));
        Assert.Empty(bare.FindAll(".dx-editorial-sidebar-caveat"));

        IRenderedComponent<DxEditorialSidebar> full = RenderComponent<DxEditorialSidebar>(p => p
            .Add(s => s.Title, "A Title").Add(s => s.Icon, "🔒")
            .Add(s => s.Caveat, (RenderFragment)(b => b.AddContent(0, "A caveat")))
            .AddChildContent("Body"));
        var icon = full.Find(".dx-editorial-sidebar-title span");
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));
        Assert.Equal("A caveat", full.Find(".dx-editorial-sidebar-caveat").TextContent);
    }

    [Fact]
    public void Dissipation_renders_the_requested_dot_count_with_an_accessible_label()
    {
        IRenderedComponent<DxEditorialDissipation> dots = RenderComponent<DxEditorialDissipation>(p => p
            .Add(d => d.DotCount, 6).Add(d => d.AriaLabel, "Fading out"));

        var root = dots.Find(".dx-editorial-dissipation");
        Assert.Equal("img", root.GetAttribute("role"));
        Assert.Equal("Fading out", root.GetAttribute("aria-label"));
        Assert.Equal(6, dots.FindAll("span.dissolve").Count);
    }

    [Fact]
    public void ScrollyStage_carries_the_index_as_a_data_attribute_for_the_ghost_numeral()
    {
        IRenderedComponent<DxEditorialScrollyStage> stage = RenderComponent<DxEditorialScrollyStage>(p => p
            .Add(s => s.Index, "01").Add(s => s.Title, "A Stage").Add(s => s.Glyph, "✉")
            .AddChildContent("Body"));

        var root = stage.Find(".dx-editorial-scrolly-stage");
        Assert.Equal("01", root.GetAttribute("data-index"));
        Assert.Equal("01", stage.Find(".dx-editorial-scrolly-stage-index").TextContent);
        Assert.Equal("true", stage.Find(".dx-editorial-scrolly-glyph").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Scrollytelling_renders_intro_and_forwards_stage_children()
    {
        IRenderedComponent<DxEditorialScrollytelling> scrolly = RenderComponent<DxEditorialScrollytelling>(p => p
            .Add(s => s.Intro, "Follow along")
            .AddChildContent<DxEditorialScrollyStage>(stage => stage
                .Add(s => s.Index, "01").Add(s => s.Title, "A Stage")));

        Assert.Equal("Follow along", scrolly.Find(".dx-editorial-scrolly-intro").TextContent);
        Assert.NotEmpty(scrolly.FindAll(".dx-editorial-scrolly-stage"));
    }
}
