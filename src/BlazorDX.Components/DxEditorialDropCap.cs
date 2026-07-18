using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Wraps a paragraph — normally the opening paragraph of a <see cref="DxEditorialLayout"/>
/// piece — in an enlarged first-letter treatment, the oldest device in the magazine glossary
/// (scribes used it to mark the start of a new section as early as the 15th century). Pure
/// CSS (<c>::first-letter</c>) — the component's only job is applying the marker class to a
/// real paragraph, since splitting the first character out in C# would break any inline markup
/// (an <c>&lt;em&gt;</c>, a link) that happens to start the text.
/// </summary>
public sealed class DxEditorialDropCap : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", "dx-editorial-dropcap");
        builder.AddContent(2, ChildContent);
        builder.CloseElement();
    }
}
