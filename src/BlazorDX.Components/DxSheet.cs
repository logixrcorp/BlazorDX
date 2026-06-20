using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// A sheet / offcanvas panel that slides in from a screen edge. It is a
/// <see cref="DialogPrimitive"/> (same focus-trap, scroll-lock, and dismissal) with
/// edge-anchored positioning and a slide transition instead of a centered panel.
/// Styling is CSS-variable driven (see dx-overlay.css).
/// </summary>
public sealed class DxSheet : DialogPrimitive
{
    /// <summary>Edge the sheet slides from: left, right, top, or bottom.</summary>
    [Parameter] public string Side { get; set; } = "right";

    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<PresenceBoundary>(0);
        builder.AddComponentParameter(1, nameof(PresenceBoundary.Visible), Open);
        builder.AddComponentParameter(2, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(3, nameof(PresenceBoundary.EnterClass), $"dx-sheet dx-sheet-{Side} dx-sheet-enter");
        builder.AddComponentParameter(4, nameof(PresenceBoundary.LeaveClass), $"dx-sheet dx-sheet-{Side} dx-sheet-leave");
        builder.AddComponentParameter(5, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderShell);
        builder.CloseComponent();
    }

    private void RenderShell(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-sheet-backdrop");

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "id", PanelId);
        builder.AddAttribute(4, "class", $"dx-sheet-panel dx-sheet-panel-{Side} {Class}".TrimEnd());
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
