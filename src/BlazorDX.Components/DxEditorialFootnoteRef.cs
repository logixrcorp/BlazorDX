using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// An inline superscript marker jumping to the matching entry in a
/// <see cref="DxEditorialFootnotes"/> list — the web analogue of a print footnote/endnote.
/// </summary>
public sealed class DxEditorialFootnoteRef : ComponentBase
{
    [Parameter, EditorRequired] public int Number { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "sup");
        builder.AddAttribute(1, "class", "dx-editorial-footnote-ref");

        builder.OpenElement(2, "a");
        builder.AddAttribute(3, "id", $"fnref-{Number}");
        builder.AddAttribute(4, "href", $"#fn-{Number}");
        builder.AddAttribute(5, "aria-label", $"Jump to footnote {Number}");
        builder.AddContent(6, Number);
        builder.CloseElement();

        builder.CloseElement();
    }
}
