using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled single-select dropdown. Inherits all behavior from
/// <see cref="SelectPrimitive{TValue}"/> and renders a trigger plus an anchored,
/// keyboard-navigable option list with WAI-ARIA listbox semantics. Styling is
/// CSS-variable driven (see dx-overlay.css).
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public sealed class DxSelect<TValue> : SelectPrimitive<TValue>
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-select-root {Class}".TrimEnd());

        builder.OpenElement(2, "button");
        builder.AddAttribute(3, "id", AnchorId);
        builder.AddAttribute(4, "type", "button");
        builder.AddAttribute(5, "class", "dx-select-trigger");
        builder.AddAttribute(6, "aria-haspopup", "listbox");
        builder.AddAttribute(7, "aria-expanded", IsOpen ? "true" : "false");
        builder.AddAttribute(8, "onclick", EventCallback.Factory.Create(this, Toggle));
        builder.AddAttribute(9, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnTriggerKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(10, "onkeydown", true);

        builder.OpenElement(11, "span");
        builder.AddAttribute(12, "class", HasSelection ? "dx-select-value" : "dx-select-value dx-select-placeholder");
        builder.AddContent(13, DisplayText);
        builder.CloseElement();

        builder.OpenElement(14, "span");
        builder.AddAttribute(15, "class", "dx-select-caret");
        builder.AddAttribute(16, "aria-hidden", "true");
        builder.AddContent(17, "▾");
        builder.CloseElement();

        builder.CloseElement();

        builder.OpenComponent<PresenceBoundary>(18);
        builder.AddComponentParameter(19, nameof(PresenceBoundary.Visible), IsOpen);
        builder.AddComponentParameter(20, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(21, nameof(PresenceBoundary.EnterClass), "dx-popover dx-popover-enter");
        builder.AddComponentParameter(22, nameof(PresenceBoundary.LeaveClass), "dx-popover dx-popover-leave");
        builder.AddComponentParameter(23, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderPanel);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderPanel(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", PanelId);
        builder.AddAttribute(2, "class", "dx-select-panel");
        builder.AddAttribute(3, "role", "listbox");
        builder.AddAttribute(4, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnPanelKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(5, "onkeydown", true);

        for (int index = 0; index < Items.Count; index++)
        {
            ListOption<TValue> option = Items[index];
            int captured = index;
            bool selected = IsSelected(index);

            string css = "dx-select-option";
            if (option.Disabled)
            {
                css += " dx-select-option-disabled";
            }

            if (selected)
            {
                css += " dx-select-option-selected";
            }

            if (IsActive(index))
            {
                css += " dx-select-option-active";
            }

            builder.OpenElement(6, "div");
            builder.SetKey(option);
            builder.AddAttribute(7, "role", "option");
            builder.AddAttribute(8, "class", css);
            builder.AddAttribute(9, "aria-selected", selected ? "true" : "false");
            builder.AddAttribute(10, "tabindex", IsActive(index) ? "0" : "-1");
            if (!option.Disabled)
            {
                builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            }

            builder.AddElementReferenceCapture(12, element => CaptureItem(captured, element));
            builder.AddContent(13, option.Text);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
