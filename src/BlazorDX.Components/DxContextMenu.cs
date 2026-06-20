using System.Globalization;
using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled context menu. Wrap a region in it; right-clicking opens a menu at
/// the cursor with keyboard navigation and dismissal. Styling is CSS-variable
/// driven (see dx-overlay.css).
/// </summary>
public sealed class DxContextMenu : ContextMenuPrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-ctx-region {Class}".TrimEnd());
        builder.AddAttribute(2, "oncontextmenu", EventCallback.Factory.Create<MouseEventArgs>(this, OnContextMenu));
        builder.AddEventPreventDefaultAttribute(3, "oncontextmenu", true);
        builder.AddContent(4, ChildContent);
        builder.CloseElement();

        builder.OpenComponent<PresenceBoundary>(5);
        builder.AddComponentParameter(6, nameof(PresenceBoundary.Visible), IsOpen);
        builder.AddComponentParameter(7, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(8, nameof(PresenceBoundary.EnterClass), "dx-popover dx-popover-enter");
        builder.AddComponentParameter(9, nameof(PresenceBoundary.LeaveClass), "dx-popover dx-popover-leave");
        builder.AddComponentParameter(10, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderMenu);
        builder.CloseComponent();
    }

    private void RenderMenu(RenderTreeBuilder builder)
    {
        string left = X.ToString("0", CultureInfo.InvariantCulture);
        string top = Y.ToString("0", CultureInfo.InvariantCulture);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", PanelId);
        builder.AddAttribute(2, "class", "dx-menu-panel dx-ctx-menu");
        builder.AddAttribute(3, "role", "menu");
        builder.AddAttribute(4, "style", $"position:fixed;left:{left}px;top:{top}px;");
        builder.AddAttribute(5, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnPanelKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(6, "onkeydown", true);

        for (int index = 0; index < Items.Count; index++)
        {
            MenuItem item = Items[index];
            int captured = index;

            builder.OpenElement(7, "button");
            builder.SetKey(item);
            builder.AddAttribute(8, "type", "button");
            builder.AddAttribute(9, "role", "menuitem");
            builder.AddAttribute(10, "class", item.Disabled ? "dx-menu-item dx-menu-item-disabled" : "dx-menu-item");
            builder.AddAttribute(11, "tabindex", IsActive(index) ? "0" : "-1");
            builder.AddAttribute(12, "disabled", item.Disabled);
            if (!item.Disabled)
            {
                builder.AddAttribute(13, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            }

            builder.AddElementReferenceCapture(14, element => CaptureItem(captured, element));
            builder.AddContent(15, item.Text);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
