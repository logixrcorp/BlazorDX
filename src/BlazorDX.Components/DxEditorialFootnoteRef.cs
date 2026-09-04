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

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxEditorialFootnoteRef>? s;

    private DxStrings<DxEditorialFootnoteRef> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "sup");
        builder.AddAttribute(1, "class", "dx-editorial-footnote-ref");

        builder.OpenElement(2, "a");
        builder.AddAttribute(3, "id", $"fnref-{Number}");
        builder.AddAttribute(4, "href", $"#fn-{Number}");
        builder.AddAttribute(5, "aria-label", S["JumpFootnote", "Jump to footnote {0}", Number]);
        builder.AddContent(6, Number);
        builder.CloseElement();

        builder.CloseElement();
    }
}
