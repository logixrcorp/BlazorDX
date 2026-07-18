using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A three-card footer grid for a <see cref="DxEditorialLayout"/> piece — each card an eyebrow
/// label, a title, a one-line body, and a link. Ships with BlazorDX-flavored defaults so a page
/// always has something to show; pass <see cref="Cards"/> to customize per piece.
/// </summary>
public sealed class DxEditorialFooter : ComponentBase
{
    /// <summary>One footer card: an eyebrow label, a title, a one-line body, and where it links.</summary>
    public readonly record struct FooterCard(string Eyebrow, string Title, string Body, string Href, bool External = false);

    /// <summary>Defaults to <see cref="DefaultCards"/> when omitted.</summary>
    [Parameter] public IReadOnlyList<FooterCard>? Cards { get; set; }

    private static readonly IReadOnlyList<FooterCard> DefaultCards =
    [
        new("Explore", "Component catalog", "100+ components, headless primitives, zero runtime reflection.", "/docs"),
        new("Read", "More from Insights", "Articles, blog posts, and whitepapers on how BlazorDX is built.", "/insights"),
        new("Contribute", "BlazorDX on GitHub", "MIT-licensed. File an issue, open a PR, or just poke around.", "https://github.com/logixrcorp/BlazorDX", External: true),
    ];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "footer");
        builder.AddAttribute(1, "class", "dx-editorial-footer");

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-editorial-footer-grid");

        IReadOnlyList<FooterCard> cards = Cards ?? DefaultCards;
        for (int i = 0; i < cards.Count; i++)
        {
            FooterCard card = cards[i];

            builder.OpenElement(4, "a");
            builder.SetKey(card);
            builder.AddAttribute(5, "class", "dx-editorial-footer-card");
            builder.AddAttribute(6, "href", card.Href);
            if (card.External)
            {
                builder.AddAttribute(7, "target", "_blank");
                builder.AddAttribute(8, "rel", "noopener");
            }

            builder.OpenElement(9, "p");
            builder.AddAttribute(10, "class", "dx-editorial-footer-eyebrow");
            builder.AddContent(11, card.Eyebrow);
            builder.CloseElement();

            builder.OpenElement(12, "p");
            builder.AddAttribute(13, "class", "dx-editorial-footer-card-title");
            builder.AddContent(14, card.Title);
            builder.CloseElement();

            builder.OpenElement(15, "p");
            builder.AddAttribute(16, "class", "dx-editorial-footer-card-body");
            builder.AddContent(17, card.Body);
            builder.CloseElement();

            builder.CloseElement(); // a.dx-editorial-footer-card
        }

        builder.CloseElement(); // .dx-editorial-footer-grid
        builder.CloseElement(); // footer
    }
}
