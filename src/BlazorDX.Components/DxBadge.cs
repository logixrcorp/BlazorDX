using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A small status or count badge. A leaf component (no separate primitive needed);
/// styling is CSS-variable driven (see dx-display.css). Use <see cref="Dot"/> for a
/// bare status dot, otherwise the content (or <see cref="Text"/>) is shown.
/// </summary>
public sealed class DxBadge : ComponentBase
{
    /// <summary>Badge content. Takes precedence over <see cref="Text"/>.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Plain-text content, when no <see cref="ChildContent"/> is supplied.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Visual variant: default, primary, success, warning, danger, info.</summary>
    [Parameter] public string Variant { get; set; } = "default";

    /// <summary>Render a bare status dot instead of textual content.</summary>
    [Parameter] public bool Dot { get; set; }

    /// <summary>Extra CSS classes appended to the badge.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        string dot = Dot ? " dx-badge-dot" : string.Empty;
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-badge dx-badge-{Variant}{dot} {Class}".TrimEnd());
        if (Dot)
        {
            builder.AddAttribute(2, "aria-hidden", "true");
        }
        else if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Text);
        }

        builder.CloseElement();
    }
}
