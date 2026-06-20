using BlazorDX.Primitives.Interaction;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>
/// Tier 2 styled sortable list. Inherits all behavior from
/// <see cref="SortablePrimitive"/> and renders draggable, keyboard-reorderable
/// rows. Styling is CSS-variable driven (see dx-layout.css).
/// </summary>
public sealed class DxSortableList : SortablePrimitive
{
    [Parameter] public string? Class { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-sortable {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "list");

        for (int index = 0; index < Items.Count; index++)
        {
            int captured = index;

            // No SetKey: keep DOM nodes position-stable so the captured element
            // references (and thus keyboard focus) follow the slot, not the item.
            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "class", "dx-sortable-item");
            builder.AddAttribute(5, "role", "listitem");
            builder.AddAttribute(6, "draggable", "true");
            builder.AddAttribute(7, "tabindex", IsActive(index) ? "0" : "-1");
            builder.AddAttribute(8, "aria-label", $"{Items[index]}. Press Alt plus Arrow keys to reorder.");
            builder.AddAttribute(9, "ondragstart", EventCallback.Factory.Create(this, () => OnDragStart(captured)));
            builder.AddAttribute(10, "ondragover", EventCallback.Factory.Create(this, () => { }));
            builder.AddEventPreventDefaultAttribute(11, "ondragover", true);
            builder.AddAttribute(12, "ondrop", EventCallback.Factory.Create(this, () => OnDropAsync(captured)));
            builder.AddAttribute(13, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, args => OnKeyDownAsync(args, captured)));
            builder.AddElementReferenceCapture(14, element => CaptureItem(captured, element));

            builder.OpenElement(15, "span");
            builder.AddAttribute(16, "class", "dx-sortable-handle");
            builder.AddAttribute(17, "aria-hidden", "true");
            builder.AddContent(18, "⠿");
            builder.CloseElement();

            builder.AddContent(19, Items[index]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
