using BlazorDX.Primitives.Motion;
using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled combo box (typeahead). Inherits all behavior from
/// <see cref="ComboBoxPrimitive{TValue}"/> and renders a filtering text input plus
/// an anchored dropdown of matching options, following the WAI-ARIA combobox
/// pattern (focus stays in the input; the active option is exposed via
/// aria-activedescendant). Styling is CSS-variable driven (see dx-overlay.css).
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public sealed class DxComboBox<TValue> : ComboBoxPrimitive<TValue>
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-combo-root {Class}".TrimEnd());

        builder.OpenElement(2, "input");
        builder.AddAttribute(3, "id", AnchorId);
        builder.AddAttribute(4, "class", "dx-combo-input");
        builder.AddAttribute(5, "type", "text");
        builder.AddAttribute(6, "role", "combobox");
        builder.AddAttribute(7, "aria-autocomplete", "list");
        builder.AddAttribute(8, "aria-expanded", IsOpen ? "true" : "false");
        builder.AddAttribute(9, "aria-controls", PanelId);
        builder.AddAttribute(10, "placeholder", Placeholder);
        builder.AddAttribute(11, "value", Filter);
        if (ActiveOptionId.Length > 0)
        {
            builder.AddAttribute(12, "aria-activedescendant", ActiveOptionId);
        }

        builder.AddAttribute(13, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnInput));
        builder.AddAttribute(14, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnInputKeyDownAsync));
        builder.AddAttribute(15, "onfocus", EventCallback.Factory.Create(this, OpenFromFocus));
        builder.CloseElement();

        builder.OpenComponent<PresenceBoundary>(16);
        builder.AddComponentParameter(17, nameof(PresenceBoundary.Visible), IsOpen);
        builder.AddComponentParameter(18, nameof(PresenceBoundary.ExitDurationMs), ExitDurationMs);
        builder.AddComponentParameter(19, nameof(PresenceBoundary.EnterClass), "dx-popover dx-popover-enter");
        builder.AddComponentParameter(20, nameof(PresenceBoundary.LeaveClass), "dx-popover dx-popover-leave");
        builder.AddComponentParameter(21, nameof(PresenceBoundary.ChildContent), (RenderFragment)RenderPanel);
        builder.CloseComponent();

        builder.CloseElement();
    }

    private void RenderPanel(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", PanelId);
        builder.AddAttribute(2, "class", "dx-select-panel");
        builder.AddAttribute(3, "role", "listbox");

        if (Filtered.Count == 0)
        {
            builder.OpenElement(4, "div");
            builder.AddAttribute(5, "class", "dx-combo-empty");
            builder.AddContent(6, "No matches");
            builder.CloseElement();
        }

        for (int index = 0; index < Filtered.Count; index++)
        {
            ListOption<TValue> option = Filtered[index];
            int captured = index;

            string css = "dx-select-option";
            if (option.Disabled)
            {
                css += " dx-select-option-disabled";
            }

            if (IsActive(index))
            {
                css += " dx-select-option-active";
            }

            builder.OpenElement(7, "div");
            builder.SetKey(option);
            builder.AddAttribute(8, "id", OptionId(index));
            builder.AddAttribute(9, "role", "option");
            builder.AddAttribute(10, "class", css);
            if (!option.Disabled)
            {
                // preventDefault on mousedown keeps focus in the input (no blur, panel stays
                // open); selection itself runs on click — the up-event — so a press that moves
                // off the option before release is cancelled (WCAG 2.5.2 Pointer Cancellation).
                builder.AddAttribute(11, "onmousedown", EventCallback.Factory.Create(this, () => { }));
                builder.AddEventPreventDefaultAttribute(12, "onmousedown", true);
                builder.AddAttribute(13, "onclick", EventCallback.Factory.Create(this, () => SelectAsync(captured)));
            }

            builder.AddContent(14, option.Text);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
