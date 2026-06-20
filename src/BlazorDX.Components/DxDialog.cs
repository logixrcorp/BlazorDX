using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled modal dialog. Inherits all behavior from
/// <see cref="DialogPrimitive"/> and supplies a backdrop + centered panel, wrapped
/// in a <see cref="PresenceBoundary"/> so it animates both in and out. Styling is
/// CSS-variable driven (see dx-overlay.css); the component imposes no design system.
/// </summary>
public sealed class DxDialog : DialogPrimitive
{
    /// <summary>Optional extra CSS classes appended to the panel.</summary>
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // PresenceBoundary keeps the backdrop+panel mounted through the exit
        // transition, then releases them — Blazor cannot do this natively.
        builder.OpenComponent<PresenceBoundary>(0);
        builder.AddComponentParameter(1, nameof(PresenceBoundary.Visible), Open);
        builder.AddComponentParameter(2, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(3, nameof(PresenceBoundary.EnterClass), "dx-dialog dx-dialog-enter");
        builder.AddComponentParameter(4, nameof(PresenceBoundary.LeaveClass), "dx-dialog dx-dialog-leave");
        builder.AddComponentParameter(5, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderShell);
        builder.CloseComponent();
    }

    private void RenderShell(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-dialog-backdrop");

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "id", PanelId);
        builder.AddAttribute(4, "class", $"dx-dialog-panel {Class}".TrimEnd());
        builder.AddAttribute(5, "role", "dialog");
        builder.AddAttribute(6, "aria-modal", "true");
        if (!string.IsNullOrEmpty(AriaLabel))
        {
            builder.AddAttribute(7, "aria-label", AriaLabel);
        }

        builder.AddContent(8, ChildContent);
        builder.CloseElement();

        builder.CloseElement();
    }
}
