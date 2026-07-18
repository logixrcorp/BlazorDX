using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A social/email share row for a <see cref="DxEditorialLayout"/> piece — real share-intent
/// links (X, LinkedIn, email), no clipboard "copy link" button, since that needs JS interop
/// this library's Editorial family deliberately avoids (the one exception,
/// <see cref="DxEditorialScrollytelling"/>'s reveal, is an explicit opt-in static asset, not
/// per-component interop). A non-functional "copy" button would be worse than omitting it.
/// </summary>
public sealed class DxEditorialShareBar : ComponentBase
{
    [Parameter, EditorRequired] public string Url { get; set; } = "";

    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>Whether to include the "share by email" link.</summary>
    [Parameter] public bool ShowEmail { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string encodedUrl = WebUtility.UrlEncode(Url);
        string encodedTitle = WebUtility.UrlEncode(Title);

        builder.OpenElement(0, "nav");
        builder.AddAttribute(1, "class", "dx-editorial-share");
        builder.AddAttribute(2, "aria-label", "Share this article");

        RenderLink(builder, 3, "Share on X", $"https://twitter.com/intent/tweet?url={encodedUrl}&text={encodedTitle}");
        RenderLink(builder, 10, "Share on LinkedIn", $"https://www.linkedin.com/sharing/share-offsite/?url={encodedUrl}");

        if (ShowEmail)
        {
            RenderLink(builder, 17, "Share by email", $"mailto:?subject={encodedTitle}&body={encodedUrl}", newTab: false);
        }

        builder.CloseElement();
    }

    private static void RenderLink(RenderTreeBuilder builder, int seq, string label, string href, bool newTab = true)
    {
        builder.OpenElement(seq, "a");
        builder.AddAttribute(seq + 1, "class", "dx-editorial-share-link");
        builder.AddAttribute(seq + 2, "href", href);
        if (newTab)
        {
            builder.AddAttribute(seq + 3, "target", "_blank");
            builder.AddAttribute(seq + 4, "rel", "noopener");
        }
        builder.AddContent(seq + 5, label);
        builder.CloseElement();
    }
}
