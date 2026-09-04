using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A slide carousel with previous/next controls, dot indicators, and
/// Left/Right-arrow keyboard navigation. Slides wrap around. Follows the WAI-ARIA
/// carousel pattern (group + roledescription). Two-way bind the active slide via
/// <c>@bind-Index</c>. Styling is token-driven (see dx-structure.css).
/// </summary>
public sealed class DxCarousel : ComponentBase
{
    [Parameter] public IReadOnlyList<RenderFragment> Slides { get; set; } = [];

    [Parameter] public int Index { get; set; }

    [Parameter] public EventCallback<int> IndexChanged { get; set; }

    [Parameter] public string? AriaLabel { get; set; }

    [Parameter] public bool ShowDots { get; set; } = true;

    [Parameter] public string? Class { get; set; }

    private int Count => Slides.Count;

    protected override void OnParametersSet()
    {
        if (Count == 0)
        {
            Index = 0;
        }
        else if (Index < 0 || Index >= Count)
        {
            Index = ((Index % Count) + Count) % Count;
        }
    }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxCarousel>? s;

    private DxStrings<DxCarousel> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-carousel {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "group");
        builder.AddAttribute(3, "aria-roledescription", S["Carousel", "carousel"]);
        builder.AddAttribute(4, "aria-label", AriaLabel ?? S["CarouselLabel", "Carousel"]);
        builder.AddAttribute(5, "tabindex", "0");
        builder.AddAttribute(6, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));

        BuildArrow(builder, 10, "‹", "Previous slide", () => GoAsync(Index - 1));

        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "dx-carousel-viewport");

        for (int i = 0; i < Count; i++)
        {
            builder.OpenElement(22, "div");
            builder.SetKey(i);
            builder.AddAttribute(23, "class", i == Index ? "dx-carousel-slide dx-carousel-active" : "dx-carousel-slide");
            builder.AddAttribute(24, "role", "group");
            builder.AddAttribute(25, "aria-roledescription", S["Slide", "slide"]);
            builder.AddAttribute(26, "aria-label", S["SlidePosition", "{0} of {1}", i + 1, Count]);
            builder.AddAttribute(27, "aria-hidden", i == Index ? "false" : "true");
            builder.AddContent(28, Slides[i]);
            builder.CloseElement();
        }

        builder.CloseElement();

        BuildArrow(builder, 40, "›", "Next slide", () => GoAsync(Index + 1));

        if (ShowDots && Count > 1)
        {
            builder.OpenElement(50, "div");
            builder.AddAttribute(51, "class", "dx-carousel-dots");
            builder.AddAttribute(52, "role", "tablist");
            for (int i = 0; i < Count; i++)
            {
                int captured = i;
                builder.OpenElement(53, "button");
                builder.SetKey(i);
                builder.AddAttribute(54, "type", "button");
                builder.AddAttribute(55, "class", i == Index ? "dx-carousel-dot dx-carousel-dot-active" : "dx-carousel-dot");
                builder.AddAttribute(56, "role", "tab");
                builder.AddAttribute(57, "aria-selected", i == Index ? "true" : "false");
                // Was string.Create(InvariantCulture, ...): a spoken label, so it needs the
                // current culture for the number as well as a translation for the sentence.
                // DX1003 could not see this one — the argument was a method call, not a literal.
                builder.AddAttribute(58, "aria-label", S["GoToSlide", "Go to slide {0}", captured + 1]);
                builder.AddAttribute(59, "onclick", EventCallback.Factory.Create(this, () => GoAsync(captured)));
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void BuildArrow(RenderTreeBuilder builder, int seq, string glyph, string label, Func<Task> onClick)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-carousel-arrow");
        builder.AddAttribute(seq + 3, "aria-label", label);
        builder.AddAttribute(seq + 4, "disabled", Count < 2);
        builder.AddAttribute(seq + 5, "onclick", EventCallback.Factory.Create(this, onClick));
        builder.AddContent(seq + 6, glyph);
        builder.CloseElement();
    }

    private Task OnKeyDownAsync(KeyboardEventArgs args) => args.Key switch
    {
        "ArrowLeft" => GoAsync(Index - 1),
        "ArrowRight" => GoAsync(Index + 1),
        "Home" => GoAsync(0),
        "End" => GoAsync(Count - 1),
        _ => Task.CompletedTask,
    };

    private Task GoAsync(int target)
    {
        if (Count == 0)
        {
            return Task.CompletedTask;
        }

        int next = ((target % Count) + Count) % Count;
        Index = next;
        return IndexChanged.HasDelegate ? IndexChanged.InvokeAsync(next) : Task.CompletedTask;
    }
}
