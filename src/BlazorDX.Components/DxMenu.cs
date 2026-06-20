using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled menu. Inherits all behavior from <see cref="MenuPrimitive"/> and
/// renders a trigger plus an anchored, keyboard-navigable list of menu items with
/// WAI-ARIA menu semantics. Styling is CSS-variable driven (see dx-overlay.css).
/// </summary>
public sealed class DxMenu : MenuPrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "dx-menu-root");

        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "id", AnchorId);
        builder.AddAttribute(4, "class", "dx-menu-trigger");
        builder.AddAttribute(5, "aria-haspopup", "menu");
        builder.AddAttribute(6, "aria-expanded", Open ? "true" : "false");
        builder.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, ToggleAsync));
        builder.AddContent(8, Trigger);
        builder.CloseElement();

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
        builder.AddAttribute(2, "class", $"dx-menu-panel {Class}".TrimEnd());
        builder.AddAttribute(3, "role", "menu");
        builder.AddAttribute(4, "onkeydown", EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs>(this, OnPanelKeyDownAsync));
        // Stop arrow keys / Space from scrolling the page while navigating the menu.
        builder.AddEventPreventDefaultAttribute(5, "onkeydown", true);

        for (int index = 0; index < Items.Count; index++)
        {
            MenuItem item = Items[index];
            int captured = index;

            builder.OpenElement(6, "button");
            builder.SetKey(item);
            builder.AddAttribute(7, "type", "button");
            builder.AddAttribute(8, "role", "menuitem");
            builder.AddAttribute(9, "class", item.Disabled ? "dx-menu-item dx-menu-item-disabled" : "dx-menu-item");
            builder.AddAttribute(10, "tabindex", IsActive(index) ? "0" : "-1");
            builder.AddAttribute(11, "disabled", item.Disabled);
            if (!item.Disabled)
            {
                builder.AddAttribute(12, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            }

            builder.AddElementReferenceCapture(13, element => CaptureItem(captured, element));
            builder.AddContent(14, item.Text);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
