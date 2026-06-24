using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled accordion. Inherits all behavior from
/// <see cref="AccordionPrimitive"/> and renders a list of disclosure sections with
/// WAI-ARIA semantics. Styling is CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxAccordion : AccordionPrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-accordion {Class}".TrimEnd());

        for (int index = 0; index < Items.Count; index++)
        {
            AccordionItem item = Items[index];
            int captured = index;
            bool open = IsExpanded(index);

            builder.OpenElement(2, "div");
            builder.SetKey(item);
            builder.AddAttribute(3, "class", "dx-accordion-item");

            // Header (disclosure button)
            builder.OpenElement(4, "button");
            builder.AddAttribute(5, "type", "button");
            builder.AddAttribute(6, "id", HeaderId(index));
            builder.AddAttribute(7, "class", open ? "dx-accordion-header dx-accordion-open" : "dx-accordion-header");
            builder.AddAttribute(8, "aria-expanded", open ? "true" : "false");
            builder.AddAttribute(9, "aria-controls", RegionId(index));
            builder.AddAttribute(10, "disabled", item.Disabled);
            if (!item.Disabled)
            {
                builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, () => Toggle(captured)));
            }

            builder.OpenElement(12, "span");
            builder.AddAttribute(13, "class", "dx-accordion-icon");
            // Decorative: the button is already named by its title, so the chevron is
            // hidden from assistive tech (the state is conveyed by aria-expanded).
            builder.AddAttribute(14, "aria-hidden", "true");
            builder.AddContent(15, open ? "▾" : "▸");
            builder.CloseElement();

            builder.AddContent(16, item.Title);
            builder.CloseElement();

            // Region (rendered only when open)
            if (open)
            {
                builder.OpenElement(17, "div");
                builder.AddAttribute(18, "id", RegionId(index));
                builder.AddAttribute(19, "class", "dx-accordion-region");
                builder.AddAttribute(20, "role", "region");
                builder.AddAttribute(21, "aria-labelledby", HeaderId(index));
                builder.AddContent(22, item.Content);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
