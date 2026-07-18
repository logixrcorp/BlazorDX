using BlazorDX.Components;
using BlazorDX.Interop;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>
/// The Dx*Editorial family: long-form/magazine-style layout components (hero shell, full-bleed
/// figures, a two-column "spread", pull-quotes, spec sidebars, scrollytelling, and the footer).
/// Mostly plain markup composition — no interop, no compute backend — so coverage here is mostly
/// "does the right markup/attributes come out for the params given." DxEditorialGlossaryTerm is
/// the one exception (it composes DxTooltip, which needs IAnchorInterop), so the DI registration
/// below covers the whole class rather than adding a second test class just for that.
/// </summary>
public sealed class EditorialTests : TestContext
{
    public EditorialTests()
    {
        Services.AddScoped<IAnchorInterop, NullAnchorInterop>();
    }

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

    [Fact]
    public void TableOfContents_renders_a_link_per_entry_targeting_its_id()
    {
        DxEditorialTableOfContents.TocEntry[] entries =
        [
            new("First section", "first"),
            new("Second section", "second"),
        ];
        IRenderedComponent<DxEditorialTableOfContents> toc = RenderComponent<DxEditorialTableOfContents>(p => p
            .Add(t => t.Entries, entries).Add(t => t.Heading, "On this page"));

        Assert.Equal("On this page", toc.Find(".dx-editorial-toc-heading").TextContent);
        Assert.Equal("On this page", toc.Find("nav").GetAttribute("aria-label"));
        var links = toc.FindAll(".dx-editorial-toc-list a");
        Assert.Equal(2, links.Count);
        Assert.Equal("#first", links[0].GetAttribute("href"));
        Assert.Equal("Second section", links[1].TextContent);
    }

    [Fact]
    public void ReadingProgress_renders_a_decorative_fixed_bar()
    {
        IRenderedComponent<DxEditorialReadingProgress> bar = RenderComponent<DxEditorialReadingProgress>();

        var root = bar.Find(".dx-editorial-reading-progress");
        Assert.Equal("true", root.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void DropCap_wraps_content_in_a_marker_paragraph_without_altering_it()
    {
        IRenderedComponent<DxEditorialDropCap> p = RenderComponent<DxEditorialDropCap>(cfg => cfg
            .AddChildContent("Once upon a time."));

        var root = p.Find("p.dx-editorial-dropcap");
        Assert.Equal("Once upon a time.", root.TextContent);
    }

    [Fact]
    public void AuthorBio_derives_initials_from_name_and_links_the_name_when_a_profile_url_is_set()
    {
        IRenderedComponent<DxEditorialAuthorBio> bio = RenderComponent<DxEditorialAuthorBio>(p => p
            .Add(a => a.Name, "Ada Lovelace")
            .Add(a => a.Role, "Contributor")
            .Add(a => a.ProfileUrl, "/authors/ada")
            .AddChildContent("A short bio."));

        Assert.Equal("AL", bio.Find(".dx-avatar-initials").TextContent);
        var nameLink = bio.Find(".dx-editorial-author-bio-name a");
        Assert.Equal("/authors/ada", nameLink.GetAttribute("href"));
        Assert.Equal("Ada Lovelace", nameLink.TextContent);
        Assert.Equal("Contributor", bio.Find(".dx-editorial-author-bio-role").TextContent);
        Assert.Equal("A short bio.", bio.Find(".dx-editorial-author-bio-body").TextContent);
    }

    [Fact]
    public void AuthorBio_renders_plain_name_text_without_a_profile_url()
    {
        IRenderedComponent<DxEditorialAuthorBio> bio = RenderComponent<DxEditorialAuthorBio>(p => p
            .Add(a => a.Name, "Grace Hopper"));

        Assert.Empty(bio.FindAll(".dx-editorial-author-bio-name a"));
        Assert.Equal("Grace Hopper", bio.Find(".dx-editorial-author-bio-name").TextContent);
        Assert.Equal("GH", bio.Find(".dx-avatar-initials").TextContent);
    }

    [Fact]
    public void TagList_renders_a_link_per_tag()
    {
        DxEditorialTagList.Tag[] tags = [new("Security", "/topics/security"), new("WebAssembly", "/topics/wasm")];
        IRenderedComponent<DxEditorialTagList> list = RenderComponent<DxEditorialTagList>(p => p.Add(t => t.Tags, tags));

        var links = list.FindAll("a.dx-editorial-tag");
        Assert.Equal(2, links.Count);
        Assert.Equal("/topics/security", links[0].GetAttribute("href"));
        Assert.Equal("WebAssembly", links[1].TextContent);
    }

    [Fact]
    public void Related_renders_a_card_per_entry_and_omits_the_category_line_when_unset()
    {
        DxEditorialRelated.RelatedEntry[] entries =
        [
            new("Title A", "Summary A", "/a", "Article"),
            new("Title B", "Summary B", "/b"),
        ];
        IRenderedComponent<DxEditorialRelated> related = RenderComponent<DxEditorialRelated>(p => p
            .Add(r => r.Entries, entries).Add(r => r.Heading, "More reading"));

        Assert.Equal("More reading", related.Find(".dx-editorial-related-heading").TextContent);
        var cards = related.FindAll("a.dx-editorial-related-card");
        Assert.Equal(2, cards.Count);
        Assert.Equal("Article", related.FindAll(".dx-editorial-related-card-category")[0].TextContent);
        Assert.Single(related.FindAll(".dx-editorial-related-card-category"));
    }

    [Fact]
    public void Related_renders_nothing_when_entries_are_empty()
    {
        IRenderedComponent<DxEditorialRelated> related = RenderComponent<DxEditorialRelated>(p => p
            .Add(r => r.Entries, Array.Empty<DxEditorialRelated.RelatedEntry>()));

        Assert.Empty(related.FindAll("section"));
    }

    [Fact]
    public void SeriesNav_renders_both_links_when_both_sides_given()
    {
        IRenderedComponent<DxEditorialSeriesNav> nav = RenderComponent<DxEditorialSeriesNav>(p => p
            .Add(n => n.PreviousTitle, "Part One").Add(n => n.PreviousRoute, "/part-one")
            .Add(n => n.NextTitle, "Part Three").Add(n => n.NextRoute, "/part-three"));

        var root = nav.Find("nav.dx-editorial-series-nav");
        Assert.DoesNotContain("dx-editorial-series-nav--single", root.ClassName);
        Assert.Equal("Part One", nav.Find(".dx-editorial-series-link--previous .dx-editorial-series-title").TextContent);
        Assert.Equal("Part Three", nav.Find(".dx-editorial-series-link--next .dx-editorial-series-title").TextContent);
    }

    [Fact]
    public void SeriesNav_renders_only_next_and_applies_the_single_modifier_when_previous_is_omitted()
    {
        IRenderedComponent<DxEditorialSeriesNav> nav = RenderComponent<DxEditorialSeriesNav>(p => p
            .Add(n => n.NextTitle, "Part Two").Add(n => n.NextRoute, "/part-two"));

        var root = nav.Find("nav.dx-editorial-series-nav");
        Assert.Contains("dx-editorial-series-nav--single", root.ClassName);
        Assert.Empty(nav.FindAll(".dx-editorial-series-link--previous"));
        Assert.Single(nav.FindAll(".dx-editorial-series-link--next"));
    }

    [Fact]
    public void SeriesNav_renders_nothing_when_neither_side_is_given()
    {
        IRenderedComponent<DxEditorialSeriesNav> nav = RenderComponent<DxEditorialSeriesNav>();

        Assert.Empty(nav.FindAll("nav"));
    }

    [Fact]
    public void InsetFigure_applies_the_right_modifier_and_renders_the_caption_when_set()
    {
        IRenderedComponent<DxEditorialInsetFigure> left = RenderComponent<DxEditorialInsetFigure>(p => p
            .Add(f => f.Caption, "A caption").AddChildContent("<img src=\"x.jpg\" alt=\"x\" />"));
        Assert.DoesNotContain("dx-editorial-inset-figure--right", left.Find("figure").ClassName);
        Assert.Equal("A caption", left.Find("figcaption").TextContent);

        IRenderedComponent<DxEditorialInsetFigure> right = RenderComponent<DxEditorialInsetFigure>(p => p
            .Add(f => f.Right, true).AddChildContent("<img src=\"x.jpg\" alt=\"x\" />"));
        Assert.Contains("dx-editorial-inset-figure--right", right.Find("figure").ClassName);
        Assert.Empty(right.FindAll("figcaption"));
    }

    [Fact]
    public void StatRow_renders_a_stat_per_entry_and_omits_detail_when_unset()
    {
        DxEditorialStatRow.Stat[] stats =
        [
            new("256-bit", "AES-GCM key size", "Authenticated encryption"),
            new("0", "Plaintext copies stored"),
        ];
        IRenderedComponent<DxEditorialStatRow> row = RenderComponent<DxEditorialStatRow>(p => p.Add(s => s.Stats, stats));

        var values = row.FindAll(".dx-editorial-stat-value");
        Assert.Equal(2, values.Count);
        Assert.Equal("256-bit", values[0].TextContent);
        Assert.Single(row.FindAll(".dx-editorial-stat-detail"));
    }

    [Fact]
    public void FootnoteRef_and_Footnotes_link_to_each_other_by_number()
    {
        IRenderedComponent<DxEditorialFootnoteRef> reference = RenderComponent<DxEditorialFootnoteRef>(p => p.Add(r => r.Number, 1));
        var refLink = reference.Find("sup.dx-editorial-footnote-ref a");
        Assert.Equal("fnref-1", refLink.GetAttribute("id"));
        Assert.Equal("#fn-1", refLink.GetAttribute("href"));

        DxEditorialFootnotes.FootnoteEntry[] entries = [new(1, "MCP: Model Context Protocol.")];
        IRenderedComponent<DxEditorialFootnotes> footnotes = RenderComponent<DxEditorialFootnotes>(p => p.Add(f => f.Entries, entries));
        var item = footnotes.Find("li");
        Assert.Equal("fn-1", item.GetAttribute("id"));
        Assert.Contains("MCP: Model Context Protocol.", item.TextContent);
        Assert.Equal("#fnref-1", footnotes.Find(".dx-editorial-footnote-back").GetAttribute("href"));
    }

    [Fact]
    public void Footnotes_renders_nothing_when_entries_are_empty()
    {
        IRenderedComponent<DxEditorialFootnotes> footnotes = RenderComponent<DxEditorialFootnotes>(p => p
            .Add(f => f.Entries, Array.Empty<DxEditorialFootnotes.FootnoteEntry>()));

        Assert.Empty(footnotes.FindAll("section"));
    }

    [Fact]
    public void GlossaryTerm_renders_a_focusable_trigger_and_the_definition_panel()
    {
        IRenderedComponent<DxEditorialGlossaryTerm> term = RenderComponent<DxEditorialGlossaryTerm>(p => p
            .Add(t => t.Term, "ECDH").Add(t => t.Definition, "Elliptic-Curve Diffie-Hellman."));

        var trigger = term.Find(".dx-editorial-glossary-term");
        Assert.Equal("ECDH", trigger.TextContent);
        Assert.Equal("0", trigger.GetAttribute("tabindex"));
    }

    [Fact]
    public void ShareBar_encodes_url_and_title_into_real_share_intent_links()
    {
        IRenderedComponent<DxEditorialShareBar> bar = RenderComponent<DxEditorialShareBar>(p => p
            .Add(s => s.Url, "https://example.com/a b").Add(s => s.Title, "A & B"));

        var links = bar.FindAll("a.dx-editorial-share-link");
        Assert.Equal(3, links.Count);
        string encodedUrl = System.Net.WebUtility.UrlEncode("https://example.com/a b")!;
        string encodedTitle = System.Net.WebUtility.UrlEncode("A & B")!;
        Assert.Contains("twitter.com/intent/tweet", links[0].GetAttribute("href"));
        Assert.Contains($"url={encodedUrl}", links[0].GetAttribute("href"));
        Assert.Contains("linkedin.com/sharing", links[1].GetAttribute("href"));
        Assert.StartsWith($"mailto:?subject={encodedTitle}", links[2].GetAttribute("href"));
        Assert.Null(links[2].GetAttribute("target"));
    }

    [Fact]
    public void ShareBar_omits_the_email_link_when_ShowEmail_is_false()
    {
        IRenderedComponent<DxEditorialShareBar> bar = RenderComponent<DxEditorialShareBar>(p => p
            .Add(s => s.Url, "https://example.com").Add(s => s.Title, "T").Add(s => s.ShowEmail, false));

        Assert.Equal(2, bar.FindAll("a.dx-editorial-share-link").Count);
    }

    [Fact]
    public void NewsletterSignup_invokes_OnSubscribe_with_the_typed_email_on_submit()
    {
        string? submitted = null;
        IRenderedComponent<DxEditorialNewsletterSignup> form = RenderComponent<DxEditorialNewsletterSignup>(p => p
            .Add(f => f.OnSubscribe, EventCallback.Factory.Create<string>(this, v => submitted = v)));

        Assert.Empty(form.FindAll(".dx-editorial-newsletter-description"));
        form.Find("input").Change("reader@example.com");
        form.Find("form").Submit();

        Assert.Equal("reader@example.com", submitted);
    }

    [Fact]
    public void NewsletterSignup_does_not_invoke_OnSubscribe_when_the_email_is_blank()
    {
        var invoked = false;
        IRenderedComponent<DxEditorialNewsletterSignup> form = RenderComponent<DxEditorialNewsletterSignup>(p => p
            .Add(f => f.OnSubscribe, EventCallback.Factory.Create<string>(this, _ => invoked = true)));

        form.Find("form").Submit();

        Assert.False(invoked);
    }

    [Fact]
    public void Listen_renders_a_native_audio_element_with_the_given_source()
    {
        IRenderedComponent<DxEditorialListen> listen = RenderComponent<DxEditorialListen>(p => p
            .Add(l => l.AudioSrc, "narration.mp3"));

        var audio = listen.Find("audio.dx-editorial-listen-audio");
        Assert.Equal("narration.mp3", audio.GetAttribute("src"));
        Assert.NotNull(audio.GetAttribute("controls"));
        Assert.Equal("Listen to this article", listen.Find(".dx-editorial-listen-label").TextContent);
    }
}
