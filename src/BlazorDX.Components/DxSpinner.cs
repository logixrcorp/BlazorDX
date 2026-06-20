using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>An accessible loading spinner (CSS-animated ring). Styling via dx-layout.css.</summary>
public sealed class DxSpinner : ComponentBase
{
    [Parameter] public int Size { get; set; } = 22;

    [Parameter] public string Label { get; set; } = "Loading";

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string size = Size.ToString(CultureInfo.InvariantCulture);

        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-spinner {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "status");
        builder.AddAttribute(3, "aria-label", Label);
        builder.AddAttribute(4, "style", $"width:{size}px;height:{size}px;");
        builder.CloseElement();
    }
}
