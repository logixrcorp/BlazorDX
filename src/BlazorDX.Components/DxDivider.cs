using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A horizontal or vertical separator. With a <see cref="Label"/> it becomes a
/// labelled section break; without one it is a decorative rule
/// (<c>role="separator"</c>). Styling is token-driven (see dx-structure.css).
/// </summary>
public sealed class DxDivider : ComponentBase
{
    /// <summary>"horizontal" (default) or "vertical".</summary>
    [Parameter] public string Orientation { get; set; } = "horizontal";

    /// <summary>Optional centered label (horizontal only).</summary>
    [Parameter] public string? Label { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        bool vertical = Orientation == "vertical";
        bool labelled = !vertical && !string.IsNullOrEmpty(Label);

        builder.OpenElement(0, "div");
        string orientationClass = vertical ? "dx-divider-vertical" : "dx-divider-horizontal";
        string labelClass = labelled ? " dx-divider-labelled" : string.Empty;
        builder.AddAttribute(1, "class", $"dx-divider {orientationClass}{labelClass} {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "separator");
        builder.AddAttribute(3, "aria-orientation", vertical ? "vertical" : "horizontal");

        if (labelled)
        {
            builder.AddAttribute(4, "aria-label", Label);
            builder.OpenElement(5, "span");
            builder.AddAttribute(6, "class", "dx-divider-label");
            builder.AddContent(7, Label);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
