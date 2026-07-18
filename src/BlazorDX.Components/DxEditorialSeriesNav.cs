using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Previous/next navigation for a multi-part <see cref="DxEditorialLayout"/> piece — the web
/// equivalent of a print magazine's jump line ("continued on p. 34"), except it moves forward
/// into the next part rather than just resuming the current one. Either side may be omitted
/// (the opening piece in a series has no previous; the closer has no next); when only one side
/// is given, it's rendered alone rather than leaving an empty grid cell.
/// </summary>
public sealed class DxEditorialSeriesNav : ComponentBase
{
    [Parameter] public string? PreviousTitle { get; set; }

    [Parameter] public string? PreviousRoute { get; set; }

    [Parameter] public string? NextTitle { get; set; }

    [Parameter] public string? NextRoute { get; set; }

    private bool HasPrevious => !string.IsNullOrEmpty(PreviousTitle) && !string.IsNullOrEmpty(PreviousRoute);

    private bool HasNext => !string.IsNullOrEmpty(NextTitle) && !string.IsNullOrEmpty(NextRoute);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!HasPrevious && !HasNext)
        {
            return;
        }

        builder.OpenElement(0, "nav");
        string modifier = HasPrevious && HasNext ? "" : " dx-editorial-series-nav--single";
        builder.AddAttribute(1, "class", $"dx-editorial-series-nav{modifier}");
        builder.AddAttribute(2, "aria-label", "Series navigation");

        if (HasPrevious)
        {
            RenderLink(builder, 3, "dx-editorial-series-link dx-editorial-series-link--previous",
                PreviousRoute!, "Previous", PreviousTitle!);
        }

        if (HasNext)
        {
            RenderLink(builder, 10, "dx-editorial-series-link dx-editorial-series-link--next",
                NextRoute!, "Next", NextTitle!);
        }

        builder.CloseElement(); // nav
    }

    private static void RenderLink(RenderTreeBuilder builder, int seq, string cssClass, string href, string eyebrow, string title)
    {
        builder.OpenElement(seq, "a");
        builder.AddAttribute(seq + 1, "class", cssClass);
        builder.AddAttribute(seq + 2, "href", href);

        builder.OpenElement(seq + 3, "span");
        builder.AddAttribute(seq + 4, "class", "dx-editorial-series-eyebrow");
        builder.AddContent(seq + 5, eyebrow);
        builder.CloseElement();

        builder.OpenElement(seq + 6, "span");
        builder.AddAttribute(seq + 7, "class", "dx-editorial-series-title");
        builder.AddContent(seq + 8, title);
        builder.CloseElement();

        builder.CloseElement(); // a
    }
}
