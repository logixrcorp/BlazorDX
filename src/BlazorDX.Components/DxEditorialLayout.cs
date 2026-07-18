using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Shared "Architecture of Silence" editorial shell for long-form, magazine-style pieces: a
/// hero (kicker, title, subtitle, byline, optionally a full-bleed photo) followed by a
/// <see cref="ChildContent"/> slot for the body, with a <see cref="DxEditorialFooter"/> wrapped
/// on automatically. Pure CSS (dx-editorial.css) — no Tailwind, no new color system; reuses the
/// library's own dx-theme.css tokens throughout.
/// </summary>
public sealed class DxEditorialLayout : ComponentBase
{
    [Parameter] public string? Kicker { get; set; }

    [Parameter, EditorRequired] public string Title { get; set; } = "";

    [Parameter] public string? Subtitle { get; set; }

    [Parameter] public string Author { get; set; } = "BlazorDX Team";

    [Parameter, EditorRequired] public DateOnly Published { get; set; }

    [Parameter] public int? ReadingMinutes { get; set; }

    /// <summary>Optional full-bleed image under the hero meta row. Omit for a text-only hero.</summary>
    [Parameter] public string? HeroImageSrc { get; set; }

    [Parameter] public string? HeroImageAlt { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public IReadOnlyList<DxEditorialFooter.FooterCard>? FooterCards { get; set; }

    private bool HasHeroImage => !string.IsNullOrEmpty(HeroImageSrc);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "article");
        builder.AddAttribute(1, "class", "dx-editorial");

        builder.OpenElement(2, "header");
        builder.AddAttribute(3, "class", HasHeroImage ? "dx-editorial-hero dx-editorial-hero--photo" : "dx-editorial-hero");

        if (HasHeroImage)
        {
            builder.OpenElement(4, "img");
            builder.AddAttribute(5, "class", "dx-editorial-hero-image");
            builder.AddAttribute(6, "src", HeroImageSrc);
            builder.AddAttribute(7, "alt", HeroImageAlt);
            builder.AddAttribute(8, "loading", "eager");
            builder.CloseElement();
        }

        builder.OpenElement(9, "div");
        builder.AddAttribute(10, "class", "dx-editorial-hero-content");

        if (!string.IsNullOrEmpty(Kicker))
        {
            builder.OpenElement(11, "span");
            builder.AddAttribute(12, "class", "dx-editorial-kicker");
            builder.AddContent(13, Kicker);
            builder.CloseElement();
        }

        builder.OpenElement(14, "h1");
        builder.AddAttribute(15, "class", "dx-editorial-title");
        builder.AddContent(16, Title);
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Subtitle))
        {
            builder.OpenElement(17, "p");
            builder.AddAttribute(18, "class", "dx-editorial-subtitle");
            builder.AddContent(19, Subtitle);
            builder.CloseElement();
        }

        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "dx-editorial-meta");

        builder.OpenElement(22, "span");
        builder.AddContent(23, Author);
        builder.CloseElement();

        builder.OpenElement(24, "span");
        builder.AddAttribute(25, "class", "dx-editorial-meta-dot");
        builder.AddAttribute(26, "aria-hidden", "true");
        builder.CloseElement();

        builder.OpenElement(27, "span");
        builder.AddContent(28, Published.ToString("MMMM d, yyyy"));
        builder.CloseElement();

        if (ReadingMinutes is { } minutes)
        {
            builder.OpenElement(29, "span");
            builder.AddAttribute(30, "class", "dx-editorial-meta-dot");
            builder.AddAttribute(31, "aria-hidden", "true");
            builder.CloseElement();

            builder.OpenElement(32, "span");
            builder.AddContent(33, $"{minutes} min read");
            builder.CloseElement();
        }

        builder.CloseElement(); // .dx-editorial-meta
        builder.CloseElement(); // .dx-editorial-hero-content
        builder.CloseElement(); // header

        builder.AddContent(34, ChildContent);

        builder.OpenComponent<DxEditorialFooter>(35);
        builder.AddComponentParameter(36, nameof(DxEditorialFooter.Cards), FooterCards);
        builder.CloseComponent();

        builder.CloseElement(); // article
    }
}
