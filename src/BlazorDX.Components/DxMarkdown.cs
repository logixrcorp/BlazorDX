using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Renders a Markdown string as sanitized HTML via <see cref="MarkdownRenderer"/>.
/// The HTML is safe by construction (text encoded, fixed tag set, scheme-checked
/// links). Styling is token-driven (see dx-markdown.css).
/// </summary>
public sealed class DxMarkdown : ComponentBase
{
    [Parameter] public string? Value { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-markdown {Class}".TrimEnd());
        builder.AddContent(2, MarkdownRenderer.Render(Value));
        builder.CloseElement();
    }
}
