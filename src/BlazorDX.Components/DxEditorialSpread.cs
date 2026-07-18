using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A two-column "classic meets modern" magazine spread for a <see cref="DxEditorialLayout"/>
/// piece: an elevated, drop-shadowed photo collaged against body copy, with a small labeled
/// spec card overlapping its corner — the fashion-editorial "swatch card" device, adapted here
/// to show a real fact (a cipher suite, a test name) instead of a color chip. Alternates
/// media/text sides via <see cref="Reverse"/> for rhythm against the full-bleed
/// <see cref="DxEditorialFigure"/> breaks elsewhere in a piece. Stays on dx-theme.css tokens.
/// </summary>
public sealed class DxEditorialSpread : ComponentBase
{
    [Parameter, EditorRequired] public string ImageSrc { get; set; } = "";

    [Parameter, EditorRequired] public string ImageAlt { get; set; } = "";

    /// <summary>A short serif-italic label above the title — the "classic" counterpoint to the bold sans title.</summary>
    [Parameter] public string? Kicker { get; set; }

    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>Overlaps the photo's corner. Use a real fact, not decoration — e.g. a cipher suite or a test name.</summary>
    [Parameter] public string? SpecLabel { get; set; }

    [Parameter] public string? SpecValue { get; set; }

    [Parameter] public string? SpecCaption { get; set; }

    /// <summary>Puts the photo on the right instead of the left, for rhythm across a piece with more than one spread.</summary>
    [Parameter] public bool Reverse { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", Reverse ? "dx-editorial-spread dx-editorial-spread--reverse" : "dx-editorial-spread");

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-editorial-spread-media");

        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "class", "dx-editorial-spread-photo");

        builder.OpenElement(6, "img");
        builder.AddAttribute(7, "src", ImageSrc);
        builder.AddAttribute(8, "alt", ImageAlt);
        builder.AddAttribute(9, "loading", "lazy");
        builder.CloseElement();

        builder.CloseElement(); // .dx-editorial-spread-photo

        if (!string.IsNullOrEmpty(SpecLabel))
        {
            builder.OpenElement(10, "div");
            builder.AddAttribute(11, "class", "dx-editorial-spread-spec");

            builder.OpenElement(12, "span");
            builder.AddAttribute(13, "class", "dx-editorial-spread-spec-label");
            builder.AddContent(14, SpecLabel);
            builder.CloseElement();

            builder.OpenElement(15, "span");
            builder.AddAttribute(16, "class", "dx-editorial-spread-spec-value");
            builder.AddContent(17, SpecValue);
            builder.CloseElement();

            if (!string.IsNullOrEmpty(SpecCaption))
            {
                builder.OpenElement(18, "span");
                builder.AddAttribute(19, "class", "dx-editorial-spread-spec-caption");
                builder.AddContent(20, SpecCaption);
                builder.CloseElement();
            }

            builder.CloseElement(); // .dx-editorial-spread-spec
        }

        builder.CloseElement(); // .dx-editorial-spread-media

        builder.OpenElement(21, "div");
        builder.AddAttribute(22, "class", "dx-editorial-spread-text");

        if (!string.IsNullOrEmpty(Kicker))
        {
            builder.OpenElement(23, "span");
            builder.AddAttribute(24, "class", "dx-editorial-spread-kicker");
            builder.AddContent(25, Kicker);
            builder.CloseElement();
        }

        builder.OpenElement(26, "h2");
        builder.AddAttribute(27, "class", "dx-editorial-spread-title");
        builder.AddContent(28, Title);
        builder.CloseElement();

        builder.OpenElement(29, "div");
        builder.AddAttribute(30, "class", "dx-editorial-spread-body");
        builder.AddContent(31, ChildContent);
        builder.CloseElement();

        builder.CloseElement(); // .dx-editorial-spread-text
        builder.CloseElement(); // section
    }
}
