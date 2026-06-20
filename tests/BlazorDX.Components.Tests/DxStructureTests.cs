using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Breadcrumbs, divider, drawer, timeline, and carousel rendering + behavior.</summary>
public sealed class DxStructureTests : TestContext
{
    private static IReadOnlyList<BreadcrumbItem> Crumbs() =>
    [
        new BreadcrumbItem("Home", "/"),
        new BreadcrumbItem("Section", "/section"),
        new BreadcrumbItem("Current"),
    ];

    [Fact]
    public void Breadcrumbs_links_all_but_last_and_marks_current()
    {
        IRenderedComponent<DxBreadcrumbs> bc = RenderComponent<DxBreadcrumbs>(parameters => parameters
            .Add(b => b.Items, Crumbs()));

        Assert.Equal(2, bc.FindAll("a.dx-breadcrumb-link").Count);
        var current = bc.Find("[aria-current=page]");
        Assert.Equal("Current", current.TextContent);
        // Two separators between three items.
        Assert.Equal(2, bc.FindAll(".dx-breadcrumb-sep").Count);
    }

    [Fact]
    public void Divider_labelled_has_separator_role_and_label()
    {
        IRenderedComponent<DxDivider> d = RenderComponent<DxDivider>(parameters => parameters
            .Add(x => x.Label, "Section"));

        var el = d.Find("[role=separator]");
        Assert.Equal("horizontal", el.GetAttribute("aria-orientation"));
        Assert.Contains("Section", el.TextContent);
    }

    [Fact]
    public void Divider_vertical_sets_orientation()
    {
        IRenderedComponent<DxDivider> d = RenderComponent<DxDivider>(parameters => parameters
            .Add(x => x.Orientation, "vertical"));

        Assert.Equal("vertical", d.Find("[role=separator]").GetAttribute("aria-orientation"));
    }

    [Fact]
    public void Drawer_reflects_open_state_in_class_and_aria()
    {
        IRenderedComponent<DxDrawer> drawer = RenderComponent<DxDrawer>(parameters => parameters
            .Add(d => d.Open, true)
            .Add(d => d.Panel, (RenderFragment)(b => b.AddContent(0, "Nav")))
            .Add(d => d.ChildContent, (RenderFragment)(b => b.AddContent(0, "Body"))));

        Assert.Contains("dx-drawer-open", drawer.Markup);
        Assert.Equal("false", drawer.Find("[role=complementary]").GetAttribute("aria-hidden"));

        drawer.SetParametersAndRender(parameters => parameters.Add(d => d.Open, false));
        Assert.Contains("dx-drawer-closed", drawer.Markup);
        Assert.Equal("true", drawer.Find("[role=complementary]").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Timeline_renders_marker_variants_and_optional_fields()
    {
        IReadOnlyList<TimelineItem> items =
        [
            new TimelineItem("Created", "Jan 1", "desc", "success"),
            new TimelineItem("Plain"),
        ];
        IRenderedComponent<DxTimeline> tl = RenderComponent<DxTimeline>(parameters => parameters
            .Add(t => t.Items, items));

        Assert.Equal(2, tl.FindAll(".dx-timeline-item").Count);
        Assert.Single(tl.FindAll(".dx-timeline-success"));
        Assert.Single(tl.FindAll(".dx-timeline-time"));   // only the first has a time
        Assert.Single(tl.FindAll(".dx-timeline-desc"));   // only the first has a description
    }

    private static IReadOnlyList<RenderFragment> ThreeSlides() =>
    [
        b => b.AddContent(0, "One"),
        b => b.AddContent(0, "Two"),
        b => b.AddContent(0, "Three"),
    ];

    [Fact]
    public void Carousel_marks_active_slide_and_dots()
    {
        IRenderedComponent<DxCarousel> car = RenderComponent<DxCarousel>(parameters => parameters
            .Add(c => c.Slides, ThreeSlides())
            .Add(c => c.Index, 1));

        var active = car.FindAll(".dx-carousel-active");
        Assert.Single(active);
        Assert.Equal("Two", active[0].TextContent);
        Assert.Equal("true", car.FindAll(".dx-carousel-dot")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public void Carousel_next_arrow_advances_and_wraps()
    {
        int bound = 2;
        IRenderedComponent<DxCarousel> car = RenderComponent<DxCarousel>(parameters => parameters
            .Add(c => c.Slides, ThreeSlides())
            .Add(c => c.Index, bound)
            .Add(c => c.IndexChanged, i => bound = i));

        // From the last slide, Next wraps to the first.
        car.Find("[aria-label='Next slide']").Click();
        Assert.Equal(0, bound);
    }

    [Fact]
    public void Carousel_arrow_key_navigates()
    {
        int bound = 0;
        IRenderedComponent<DxCarousel> car = RenderComponent<DxCarousel>(parameters => parameters
            .Add(c => c.Slides, ThreeSlides())
            .Add(c => c.Index, bound)
            .Add(c => c.IndexChanged, i => bound = i));

        // ArrowLeft from the first slide wraps to the last.
        car.Find(".dx-carousel").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        Assert.Equal(2, bound);
    }
}
