using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// A primary action button paired with a caret that opens a menu of secondary
/// actions. Composes <see cref="DxMenu"/> for the dropdown — no new overlay behavior,
/// just the existing anchored/dismiss/roving engine reused. Styling is CSS-variable
/// driven (see dx-display.css / dx-overlay.css).
/// </summary>
public sealed class DxSplitButton : ComponentBase
{
    /// <summary>Primary button content. Takes precedence over <see cref="Text"/>.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Primary button label, when no <see cref="ChildContent"/> is supplied.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>Visual variant: primary, secondary, danger, ghost.</summary>
    [Parameter] public string Variant { get; set; } = "primary";

    /// <summary>Disables the primary action.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Primary action click handler.</summary>
    [Parameter] public EventCallback<MouseEventArgs> OnClick { get; set; }

    /// <summary>The secondary actions shown in the dropdown menu.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<MenuItem> Items { get; set; } = [];

    /// <summary>Extra CSS classes appended to the wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    // DxMenu's Open is a controlled parameter, so the split button owns the state.
    private bool menuOpen;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-btn-group dx-split-button {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "group");

        builder.OpenElement(3, "button");
        builder.AddAttribute(4, "type", "button");
        builder.AddAttribute(5, "class", $"dx-btn dx-btn-{Variant} dx-split-primary");
        builder.AddAttribute(6, "disabled", Disabled);
        builder.AddAttribute(7, "onclick", OnClick);
        builder.AddContent(8, ChildContent ?? (b => b.AddContent(0, Text)));
        builder.CloseElement();

        // The dropdown reuses DxMenu, whose trigger handles open/close/dismiss/roving.
        builder.OpenComponent<DxMenu>(9);
        builder.AddComponentParameter(10, nameof(DxMenu.Items), Items);
        builder.AddComponentParameter(11, nameof(DxMenu.Trigger), (RenderFragment)RenderCaret);
        builder.AddComponentParameter(12, nameof(DxMenu.Open), menuOpen);
        builder.AddComponentParameter(13, nameof(DxMenu.OpenChanged),
            EventCallback.Factory.Create<bool>(this, open => menuOpen = open));
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderCaret(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", $"dx-btn dx-btn-{Variant} dx-split-toggle");
        builder.AddAttribute(2, "aria-label", "More actions");
        builder.AddContent(3, "▾");
        builder.CloseElement();
    }
}
