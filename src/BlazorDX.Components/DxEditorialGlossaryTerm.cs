using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// An inline term with a hover/focus definition for a <see cref="DxEditorialLayout"/> piece —
/// composes the library's own <see cref="DxTooltip"/> rather than a new interaction pattern.
/// The visible term is a plain <c>&lt;span&gt;</c>, not naturally focusable, so it carries
/// <c>tabindex="0"</c> itself to stay keyboard-reachable.
/// </summary>
public sealed class DxEditorialGlossaryTerm : ComponentBase
{
    [Parameter, EditorRequired] public string Term { get; set; } = "";

    [Parameter, EditorRequired] public string Definition { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<DxTooltip>(0);
        builder.AddComponentParameter(1, nameof(DxTooltip.Class), "dx-editorial-glossary-panel");
        builder.AddComponentParameter(2, nameof(DxTooltip.Trigger), (RenderFragment)RenderTrigger);
        builder.AddComponentParameter(3, nameof(DxTooltip.ChildContent), (RenderFragment)RenderDefinition);
        builder.CloseComponent();
    }

    private void RenderTrigger(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "dx-editorial-glossary-term");
        builder.AddAttribute(2, "tabindex", "0");
        builder.AddContent(3, Term);
        builder.CloseElement();
    }

    private void RenderDefinition(RenderTreeBuilder builder) => builder.AddContent(0, Definition);
}
