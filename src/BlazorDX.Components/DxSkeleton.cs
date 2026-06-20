using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>A shimmering placeholder block shown while content loads. Decorative
/// (aria-hidden). Styling via dx-layout.css.</summary>
public sealed class DxSkeleton : ComponentBase
{
    [Parameter] public string Width { get; set; } = "100%";

    [Parameter] public string Height { get; set; } = "1rem";

    [Parameter] public bool Circle { get; set; }

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", Circle ? $"dx-skeleton dx-skeleton-circle {Class}".TrimEnd() : $"dx-skeleton {Class}".TrimEnd());
        builder.AddAttribute(2, "aria-hidden", "true");
        builder.AddAttribute(3, "style", $"width:{Width};height:{Height};");
        builder.CloseElement();
    }
}
