using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// One stage of a <see cref="DxEditorialScrollytelling"/> sequence — starts hidden/offset, then
/// CSS-transitions in once the shared reveal script marks it visible.
/// </summary>
public sealed class DxEditorialScrollyStage : ComponentBase
{
    /// <summary>A short label, e.g. "01" or "Stage 1". Also drives the ghost-numeral watermark via a data attribute.</summary>
    [Parameter, EditorRequired] public string Index { get; set; } = "";

    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>A single decorative character/emoji shown in a small glyph box (aria-hidden).</summary>
    [Parameter] public string? Glyph { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Optional custom visual (e.g. <see cref="DxEditorialDissipation"/>) rendered below the body text.</summary>
    [Parameter] public RenderFragment? Visual { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-editorial-scrolly-stage");
        builder.AddAttribute(2, "data-index", Index);

        if (!string.IsNullOrEmpty(Glyph))
        {
            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "class", "dx-editorial-scrolly-glyph");
            builder.AddAttribute(5, "aria-hidden", "true");
            builder.AddContent(6, Glyph);
            builder.CloseElement();
        }

        builder.OpenElement(7, "span");
        builder.AddAttribute(8, "class", "dx-editorial-scrolly-stage-index");
        builder.AddContent(9, Index);
        builder.CloseElement();

        builder.OpenElement(10, "h3");
        builder.AddAttribute(11, "class", "dx-editorial-scrolly-stage-title");
        builder.AddContent(12, Title);
        builder.CloseElement();

        builder.OpenElement(13, "div");
        builder.AddAttribute(14, "class", "dx-editorial-scrolly-stage-body");
        builder.AddContent(15, ChildContent);
        builder.CloseElement();

        builder.AddContent(16, Visual);

        builder.CloseElement();
    }
}
