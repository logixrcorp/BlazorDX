using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A compact chip / tag, optionally dismissible. A leaf component; styling is
/// CSS-variable driven (see dx-display.css).
/// </summary>
public sealed class DxChip : ComponentBase
{
    /// <summary>Chip content. Takes precedence over <see cref="Text"/>.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Plain-text content, when no <see cref="ChildContent"/> is supplied.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Visual variant: default, primary, success, warning, danger, info.</summary>
    [Parameter] public string Variant { get; set; } = "default";

    /// <summary>When true, shows a remove button that raises <see cref="OnDismiss"/>.</summary>
    [Parameter] public bool Dismissible { get; set; }

    /// <summary>Raised when the remove button is clicked.</summary>
    [Parameter] public EventCallback OnDismiss { get; set; }

    /// <summary>Extra CSS classes appended to the chip.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-chip dx-chip-{Variant} {Class}".TrimEnd());

        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "class", "dx-chip-label");
        builder.AddContent(4, ChildContent ?? (builder2 => builder2.AddContent(0, Text)));
        builder.CloseElement();

        if (Dismissible)
        {
            builder.OpenElement(5, "button");
            builder.AddAttribute(6, "type", "button");
            builder.AddAttribute(7, "class", "dx-chip-remove");
            builder.AddAttribute(8, "aria-label", "Remove");
            builder.AddAttribute(9, "onclick", EventCallback.Factory.Create(this, () => OnDismiss.InvokeAsync()));
            builder.AddContent(10, "×");
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
