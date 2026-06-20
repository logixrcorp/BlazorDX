using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled popover. Inherits all behavior from <see cref="PopoverPrimitive"/>
/// and renders the trigger plus an anchored, animated floating panel. Styling is
/// CSS-variable driven (see dx-overlay.css).
/// </summary>
public sealed class DxPopover : PopoverPrimitive
{
    /// <summary>Optional extra CSS classes appended to the panel.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "dx-popover-root");

        // Trigger (the positioning anchor); clicking toggles the panel.
        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "id", AnchorId);
        builder.AddAttribute(4, "class", "dx-popover-trigger");
        builder.AddAttribute(5, "aria-haspopup", "dialog");
        builder.AddAttribute(6, "aria-expanded", Open ? "true" : "false");
        builder.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, ToggleAsync));
        builder.AddContent(8, Trigger);
        builder.CloseElement();

        // Panel, kept mounted through its exit animation by PresenceBoundary.
        builder.OpenComponent<PresenceBoundary>(9);
        builder.AddComponentParameter(10, nameof(PresenceBoundary.Visible), Open);
        builder.AddComponentParameter(11, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(12, nameof(PresenceBoundary.EnterClass), "dx-popover dx-popover-enter");
        builder.AddComponentParameter(13, nameof(PresenceBoundary.LeaveClass), "dx-popover dx-popover-leave");
        builder.AddComponentParameter(14, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderPanel);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderPanel(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", PanelId);
        builder.AddAttribute(2, "class", $"dx-popover-panel {Class}".TrimEnd());
        builder.AddAttribute(3, "role", "dialog");
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}
