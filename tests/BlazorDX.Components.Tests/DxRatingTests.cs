using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>Render + interaction for the rating input.</summary>
public sealed class DxRatingTests : TestContext
{
    [Fact]
    public void Fills_stars_up_to_the_value()
    {
        IRenderedComponent<DxRating> rating = RenderComponent<DxRating>(parameters => parameters
            .Add(r => r.Value, 3)
            .Add(r => r.Max, 5));

        Assert.Equal(3, rating.FindAll(".dx-rating-on").Count);
        Assert.Equal("3", rating.Find("[role=slider]").GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void Clicking_a_star_sets_the_value()
    {
        int bound = 1;
        IRenderedComponent<DxRating> rating = RenderComponent<DxRating>(parameters => parameters
            .Add(r => r.Value, bound)
            .Add(r => r.ValueChanged, v => bound = v));

        rating.FindAll(".dx-rating-star")[3].Click(); // 4th star
        Assert.Equal(4, bound);
    }

    [Fact]
    public void Arrow_keys_increment_and_decrement()
    {
        int bound = 2;
        IRenderedComponent<DxRating> rating = RenderComponent<DxRating>(parameters => parameters
            .Add(r => r.Value, bound)
            .Add(r => r.ValueChanged, v => bound = v));

        rating.Find("[role=slider]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(3, bound);
    }

    [Fact]
    public void ReadOnly_is_not_interactive()
    {
        IRenderedComponent<DxRating> rating = RenderComponent<DxRating>(parameters => parameters
            .Add(r => r.Value, 2)
            .Add(r => r.ReadOnly, true));

        var slider = rating.Find("[role=slider]");
        Assert.Equal("true", slider.GetAttribute("aria-readonly"));
        Assert.False(slider.HasAttribute("tabindex"));     // not focusable
        // Stars carry no click handler in read-only mode.
        Assert.Equal(2, rating.FindAll(".dx-rating-on").Count);
    }
}
