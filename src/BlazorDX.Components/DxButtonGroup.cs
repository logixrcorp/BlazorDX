using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Visually groups adjacent buttons into a single segmented control (shared borders,
/// rounded outer corners). A presentational wrapper with <c>role="group"</c>.
/// </summary>
public sealed class DxButtonGroup : ComponentBase
{
    /// <summary>The buttons to group.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Accessible label for the group.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes appended to the group.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-btn-group {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "group");
        if (AriaLabel is not null)
        {
            builder.AddAttribute(3, "aria-label", AriaLabel);
        }

        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}
