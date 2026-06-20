using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A horizontal toolbar container with <c>role="toolbar"</c>. Lays out buttons,
/// button groups, and other controls with consistent spacing; styling is
/// CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxToolbar : ComponentBase
{
    /// <summary>The toolbar contents.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Accessible label for the toolbar.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the toolbar.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-toolbar {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "toolbar");
        if (AriaLabel is not null)
        {
            builder.AddAttribute(3, "aria-label", AriaLabel);
        }

        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}
