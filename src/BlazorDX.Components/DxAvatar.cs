using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A circular avatar showing an image, or initials as a fallback. A leaf component;
/// styling is CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxAvatar : ComponentBase
{
    /// <summary>Image source. When set, the image is shown instead of initials.</summary>
    [Parameter] public string? ImageUrl { get; set; }

    /// <summary>Initials to show when there is no image (e.g. "AL").</summary>
    [Parameter] public string? Initials { get; set; }

    /// <summary>Accessible label / image alt text.</summary>
    [Parameter] public string? AltText { get; set; }

    /// <summary>Diameter in pixels.</summary>
    [Parameter] public int Size { get; set; } = 40;

    /// <summary>Extra CSS classes appended to the avatar.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string label = AltText ?? Initials ?? "Avatar";
        string style = string.Create(CultureInfo.InvariantCulture,
            $"width:{Size}px;height:{Size}px;font-size:{Size * 0.4:0.#}px;");

        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-avatar {Class}".TrimEnd());
        builder.AddAttribute(2, "style", style);
        builder.AddAttribute(3, "role", "img");
        builder.AddAttribute(4, "aria-label", label);

        if (!string.IsNullOrEmpty(ImageUrl))
        {
            builder.OpenElement(5, "img");
            builder.AddAttribute(6, "src", ImageUrl);
            builder.AddAttribute(7, "alt", label);
            builder.CloseElement();
        }
        else
        {
            builder.OpenElement(8, "span");
            builder.AddAttribute(9, "class", "dx-avatar-initials");
            builder.AddAttribute(10, "aria-hidden", "true");
            builder.AddContent(11, Initials);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
