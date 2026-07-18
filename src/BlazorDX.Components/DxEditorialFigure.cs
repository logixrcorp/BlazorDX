using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A full-width, full-bleed narrative-break image for a <see cref="DxEditorialLayout"/> piece.
/// <see cref="ChildContent"/> is the art itself — typically an &lt;img&gt;, but any markup
/// works (hand-authored SVG, etc.).
/// </summary>
public sealed class DxEditorialFigure : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Shown under the art. Keep it a plain factual caption, not restated body copy.</summary>
    [Parameter] public string? Caption { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "figure");
        builder.AddAttribute(1, "class", "dx-editorial-figure");

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-editorial-figure-art");
        builder.AddContent(4, ChildContent);
        builder.CloseElement();

        if (!string.IsNullOrEmpty(Caption))
        {
            builder.OpenElement(5, "figcaption");
            builder.AddAttribute(6, "class", "dx-editorial-figure-caption");
            builder.AddContent(7, Caption);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
