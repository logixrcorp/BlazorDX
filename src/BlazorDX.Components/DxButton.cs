using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A styled button. A leaf component; styling is CSS-variable driven (see
/// dx-display.css). Unmatched attributes (e.g. <c>title</c>, <c>aria-*</c>) are
/// splatted onto the underlying <c>button</c>.
/// </summary>
public sealed class DxButton : ComponentBase
{
    /// <summary>Button content. Takes precedence over <see cref="Text"/>.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Plain-text label, when no <see cref="ChildContent"/> is supplied.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Visual variant: primary, secondary, danger, ghost.</summary>
    [Parameter] public string Variant { get; set; } = "primary";

    /// <summary>The native button type (button, submit, reset).</summary>
    [Parameter] public string Type { get; set; } = "button";

    /// <summary>Disables the button.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Click handler.</summary>
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>Extra CSS classes appended to the button.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Arbitrary extra attributes splatted onto the button element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", Type);
        builder.AddAttribute(2, "class", $"dx-btn dx-btn-{Variant} {Class}".TrimEnd());
        builder.AddAttribute(3, "disabled", Disabled);
        builder.AddAttribute(4, "onclick", OnClick);
        builder.AddMultipleAttributes(5, AdditionalAttributes);
        builder.AddContent(6, ChildContent ?? (b => b.AddContent(0, Text)));
        builder.CloseElement();
    }
}
