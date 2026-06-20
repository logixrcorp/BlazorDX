using BlazorDX.Components;
using Bunit;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + navigation behavior for the pager.</summary>
public sealed class DxPagerTests : TestContext
{
    [Fact]
    public void Computes_page_count_from_total_and_size()
    {
        IRenderedComponent<DxPager> pager = RenderComponent<DxPager>(parameters => parameters
            .Add(p => p.TotalItems, 47)
            .Add(p => p.PageSize, 10)
            .Add(p => p.Page, 1));

        Assert.Equal(5, pager.Instance.PageCount);
    }

    [Fact]
    public void Marks_the_current_page()
    {
        IRenderedComponent<DxPager> pager = RenderComponent<DxPager>(parameters => parameters
            .Add(p => p.TotalItems, 50)
            .Add(p => p.PageSize, 10)
            .Add(p => p.Page, 3));

        var current = pager.Find(".dx-pager-current");
        Assert.Equal("3", current.TextContent);
        Assert.Equal("page", current.GetAttribute("aria-current"));
    }

    [Fact]
    public void Next_button_raises_PageChanged_with_the_next_page()
    {
        int changed = 0;
        IRenderedComponent<DxPager> pager = RenderComponent<DxPager>(parameters => parameters
            .Add(p => p.TotalItems, 50)
            .Add(p => p.PageSize, 10)
            .Add(p => p.Page, 2)
            .Add(p => p.PageChanged, page => changed = page));

        pager.Find("[aria-label='Next page']").Click();
        Assert.Equal(3, changed);
    }

    [Fact]
    public void Clicking_a_page_number_navigates_to_it()
    {
        int changed = 0;
        IRenderedComponent<DxPager> pager = RenderComponent<DxPager>(parameters => parameters
            .Add(p => p.TotalItems, 50)
            .Add(p => p.PageSize, 10)
            .Add(p => p.Page, 1)
            .Add(p => p.PageChanged, page => changed = page));

        pager.FindAll(".dx-pager-page").First(b => b.TextContent == "5").Click();
        Assert.Equal(5, changed);
    }

    [Fact]
    public void Edge_buttons_are_disabled_at_the_boundaries()
    {
        IRenderedComponent<DxPager> pager = RenderComponent<DxPager>(parameters => parameters
            .Add(p => p.TotalItems, 50)
            .Add(p => p.PageSize, 10)
            .Add(p => p.Page, 1));

        Assert.True(pager.Find("[aria-label='Previous page']").HasAttribute("disabled"));
        Assert.False(pager.Find("[aria-label='Next page']").HasAttribute("disabled"));
    }

    [Fact]
    public void Shows_ellipsis_for_large_page_counts()
    {
        IRenderedComponent<DxPager> pager = RenderComponent<DxPager>(parameters => parameters
            .Add(p => p.TotalItems, 200)
            .Add(p => p.PageSize, 10)
            .Add(p => p.Page, 10));

        // Page 10 of 20: window is 1 … 9 10 11 … 20, so two ellipses.
        Assert.Equal(2, pager.FindAll(".dx-pager-ellipsis").Count);
    }
}
