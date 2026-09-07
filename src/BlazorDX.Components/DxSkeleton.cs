using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A shimmering placeholder block shown while content loads. Styling via dx-layout.css.
/// </summary>
/// <remarks>
/// The shimmer itself is always <c>aria-hidden</c> — it is decorative, and hiding it is correct.
/// What it does not do by default is tell anyone the region is loading at all: several
/// <see cref="DxSkeleton"/>s typically stand in for one paragraph or card, and if each announced
/// itself, a screen reader user would hear "Loading" repeated once per line. See
/// <see cref="Announce"/> for the one-per-region opt-in.
/// </remarks>
public sealed class DxSkeleton : ComponentBase
{
    [Parameter] public string Width { get; set; } = "100%";

    [Parameter] public string Height { get; set; } = "1rem";

    [Parameter] public bool Circle { get; set; }

    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// When true, this instance also renders a visually-hidden <c>role="status"</c> text node
    /// announcing that the region is loading — the same <c>DxStrings</c>/localized-default pattern
    /// <see cref="DxSpinner"/> uses. Off by default: a group of skeletons standing in for one
    /// paragraph should set this on exactly one of them, not all of them, or a screen reader hears
    /// "Loading" once per line instead of once per region.
    /// </summary>
    [Parameter] public bool Announce { get; set; }

    /// <summary>Overrides the announced text. Defaults to the localized word for "Loading".</summary>
    [Parameter] public string? Label { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxSkeleton>? s;

    private DxStrings<DxSkeleton> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", Circle ? $"dx-skeleton dx-skeleton-circle {Class}".TrimEnd() : $"dx-skeleton {Class}".TrimEnd());
        builder.AddAttribute(2, "aria-hidden", "true");
        builder.AddAttribute(3, "style", $"width:{Width};height:{Height};");
        builder.CloseElement();

        if (Announce)
        {
            // A sibling top-level element, not a wrapper around the div above: wrapping would
            // change the root element every existing consumer and stylesheet selector assumes.
            // role="status" carries its own implicit aria-live="polite" + aria-atomic="true", so
            // no explicit aria-live is needed.
            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "class", "dx-skeleton-sr");
            builder.AddAttribute(6, "role", "status");
            builder.AddContent(7, Label ?? S["Loading", "Loading"]);
            builder.CloseElement();
        }
    }
}
