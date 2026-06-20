using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled tooltip. Inherits all behavior from <see cref="TooltipPrimitive"/>
/// and shows an anchored panel on hover/focus of its trigger. Styling is
/// CSS-variable driven (see dx-overlay.css).
/// </summary>
public sealed class DxTooltip : TooltipPrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "dx-tooltip-root");

        // Trigger: anchor + hover/focus handlers (focus support keeps it keyboard-accessible).
        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "id", AnchorId);
        builder.AddAttribute(4, "class", "dx-tooltip-trigger");
        builder.AddAttribute(5, "aria-describedby", PanelId);
        builder.AddAttribute(6, "onmouseover", EventCallback.Factory.Create(this, Show));
        builder.AddAttribute(7, "onmouseout", EventCallback.Factory.Create(this, Hide));
        builder.AddAttribute(8, "onfocusin", EventCallback.Factory.Create(this, Show));
        builder.AddAttribute(9, "onfocusout", EventCallback.Factory.Create(this, Hide));
        builder.AddContent(10, Trigger);
        builder.CloseElement();

        builder.OpenComponent<PresenceBoundary>(11);
        builder.AddComponentParameter(12, nameof(PresenceBoundary.Visible), Visible);
        builder.AddComponentParameter(13, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(14, nameof(PresenceBoundary.EnterClass), "dx-popover dx-popover-enter");
        builder.AddComponentParameter(15, nameof(PresenceBoundary.LeaveClass), "dx-popover dx-popover-leave");
        builder.AddComponentParameter(16, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderPanel);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderPanel(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", PanelId);
        builder.AddAttribute(2, "class", $"dx-tooltip-panel {Class}".TrimEnd());
        builder.AddAttribute(3, "role", "tooltip");
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}
