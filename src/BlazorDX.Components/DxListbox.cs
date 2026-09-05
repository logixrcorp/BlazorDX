using BlazorDX.Primitives.Overlays;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled inline listbox. Inherits all behavior from
/// <see cref="ListboxPrimitive{TValue}"/> and renders a bordered, scrollable list
/// of selectable options with WAI-ARIA listbox semantics. Styling is
/// CSS-variable driven (see dx-overlay.css).
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public sealed class DxListbox<TValue> : ListboxPrimitive<TValue>
{
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Accessible name for the listbox. A listbox with no name is announced as an unlabelled
    /// widget, so this falls back to a generic localized label rather than nothing.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    private DxStrings<DxListboxResources>? s;

    private DxStrings<DxListboxResources> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-listbox {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "listbox");
        builder.AddAttribute(101, "aria-label", AriaLabel ?? S["ListboxLabel", "Options"]);
        builder.AddAttribute(3, "aria-multiselectable", Multiple ? "true" : "false");
        builder.AddAttribute(4, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(5, "onkeydown", true);

        for (int index = 0; index < Items.Count; index++)
        {
            ListOption<TValue> option = Items[index];
            int captured = index;
            bool selected = IsSelected(index);

            string css = "dx-listbox-option";
            if (option.Disabled)
            {
                css += " dx-listbox-option-disabled";
            }

            if (selected)
            {
                css += " dx-listbox-option-selected";
            }

            if (IsActive(index))
            {
                css += " dx-listbox-option-active";
            }

            builder.OpenElement(6, "div");
            builder.SetKey(option);
            builder.AddAttribute(7, "role", "option");
            builder.AddAttribute(8, "class", css);
            builder.AddAttribute(9, "aria-selected", selected ? "true" : "false");
            builder.AddAttribute(10, "tabindex", IsActive(index) ? "0" : "-1");
            if (!option.Disabled)
            {
                builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, () => ToggleAsync(captured)));
            }

            builder.AddElementReferenceCapture(12, element => CaptureItem(captured, element));
            builder.AddContent(13, option.Text);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}

/// <summary>
/// Resource-name anchor for <see cref="DxListbox{TValue}"/>, which is generic: the default
/// localizer factory derives a resource name from the <i>closed</i> type, so localizing against
/// the component itself would look for a different resource per <c>TValue</c>.
/// </summary>
public sealed class DxListboxResources;
