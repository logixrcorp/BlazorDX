using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled tabs. Inherits all behavior from <see cref="TabsPrimitive"/> and
/// renders a tab list plus the selected panel with WAI-ARIA tab semantics. Styling
/// is CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxTabs : TabsPrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-tabs {Class}".TrimEnd());

        // Tab list
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", "dx-tablist");
        builder.AddAttribute(4, "role", "tablist");
        builder.AddAttribute(5, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, OnTablistKeyDownAsync));
        builder.AddEventPreventDefaultAttribute(6, "onkeydown", true);

        for (int index = 0; index < Items.Count; index++)
        {
            TabItem tab = Items[index];
            int captured = index;
            bool selected = IsSelected(index);

            string css = "dx-tab";
            if (selected)
            {
                css += " dx-tab-selected";
            }

            if (tab.Disabled)
            {
                css += " dx-tab-disabled";
            }

            builder.OpenElement(7, "button");
            builder.SetKey(tab);
            builder.AddAttribute(8, "type", "button");
            builder.AddAttribute(9, "id", TabId(index));
            builder.AddAttribute(10, "class", css);
            builder.AddAttribute(11, "role", "tab");
            builder.AddAttribute(12, "aria-selected", selected ? "true" : "false");
            builder.AddAttribute(13, "aria-controls", PanelId(index));
            builder.AddAttribute(14, "tabindex", selected ? "0" : "-1");
            builder.AddAttribute(15, "disabled", tab.Disabled);
            if (!tab.Disabled)
            {
                builder.AddAttribute(16, "onclick", EventCallback.Factory.Create(this, () => ActivateAsync(captured)));
            }

            builder.AddElementReferenceCapture(17, element => CaptureTab(captured, element));
            builder.AddContent(18, tab.Title);
            builder.CloseElement();
        }

        builder.CloseElement();

        // Selected panel
        builder.OpenElement(19, "div");
        builder.AddAttribute(20, "class", "dx-tabpanel");
        builder.AddAttribute(21, "role", "tabpanel");
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            builder.AddAttribute(22, "id", PanelId(SelectedIndex));
            builder.AddAttribute(23, "aria-labelledby", TabId(SelectedIndex));
        }

        builder.AddContent(24, SelectedContent);
        builder.CloseElement();

        builder.CloseElement();
    }
}
