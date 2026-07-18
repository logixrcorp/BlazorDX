using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A small, floated image with text wrapping around it via CSS <c>shape-outside</c> — a third
/// image treatment for a <see cref="DxEditorialLayout"/> piece, distinct from the full-bleed
/// <see cref="DxEditorialFigure"/> and the two-column <see cref="DxEditorialSpread"/>. A classic
/// print-magazine technique (text flowing around an inset image), well-supported natively in
/// CSS since 2020. <see cref="ChildContent"/> is the art itself — typically an &lt;img&gt;, but
/// hand-authored SVG works too, same convention as <see cref="DxEditorialFigure"/>.
/// </summary>
public sealed class DxEditorialInsetFigure : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Caption { get; set; }

    /// <summary>Floats right instead of left.</summary>
    [Parameter] public bool Right { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "figure");
        builder.AddAttribute(1, "class",
            Right ? "dx-editorial-inset-figure dx-editorial-inset-figure--right" : "dx-editorial-inset-figure");

        builder.AddContent(2, ChildContent);

        if (!string.IsNullOrEmpty(Caption))
        {
            builder.OpenElement(3, "figcaption");
            builder.AddAttribute(4, "class", "dx-editorial-inset-figure-caption");
            builder.AddContent(5, Caption);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
