using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A floating technical-specification card for a <see cref="DxEditorialLayout"/> piece —
/// subtle background shift, doesn't interrupt the surrounding narrative's reading flow. Use for
/// FSM states, crypto primitives, caveats, etc.
/// </summary>
public sealed class DxEditorialSidebar : ComponentBase
{
    [Parameter, EditorRequired] public string Title { get; set; } = "";

    /// <summary>Optional leading glyph (a single emoji/character kept decorative via aria-hidden).</summary>
    [Parameter] public string? Icon { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>An honest limitation/caveat, set off from the main spec body — for claims that need one.</summary>
    [Parameter] public RenderFragment? Caveat { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "aside");
        builder.AddAttribute(1, "class", "dx-editorial-sidebar");

        builder.OpenElement(2, "p");
        builder.AddAttribute(3, "class", "dx-editorial-sidebar-title");

        if (!string.IsNullOrEmpty(Icon))
        {
            builder.OpenElement(4, "span");
            builder.AddAttribute(5, "aria-hidden", "true");
            builder.AddContent(6, Icon);
            builder.CloseElement();
        }

        builder.AddContent(7, Title);
        builder.CloseElement(); // .dx-editorial-sidebar-title

        builder.OpenElement(8, "div");
        builder.AddAttribute(9, "class", "dx-editorial-sidebar-body");
        builder.AddContent(10, ChildContent);
        builder.CloseElement();

        if (Caveat is not null)
        {
            builder.OpenElement(11, "p");
            builder.AddAttribute(12, "class", "dx-editorial-sidebar-caveat");
            builder.AddContent(13, Caveat);
            builder.CloseElement();
        }

        builder.CloseElement(); // aside
    }
}
