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

            // Single-pointer (no-drag) reorder controls — the WCAG 2.5.7 alternative
            // to the drag gesture. tabindex=-1 keeps the roving model (one tab stop per
            // list); keyboard users reorder with Alt+Arrow on the row itself.
            builder.OpenElement(20, "span");
            builder.AddAttribute(21, "class", "dx-sortable-controls");

            builder.OpenElement(22, "button");
            builder.AddAttribute(23, "type", "button");
            builder.AddAttribute(24, "class", "dx-sortable-move");
            builder.AddAttribute(25, "tabindex", "-1");
            builder.AddAttribute(26, "aria-label", $"Move {Items[index]} up");
            builder.AddAttribute(27, "disabled", captured == 0);
            builder.AddAttribute(28, "onclick", EventCallback.Factory.Create(this, () => MoveByAsync(captured, -1)));
            builder.AddContent(29, "▲");
            builder.CloseElement();

            builder.OpenElement(30, "button");
            builder.AddAttribute(31, "type", "button");
            builder.AddAttribute(32, "class", "dx-sortable-move");
            builder.AddAttribute(33, "tabindex", "-1");
            builder.AddAttribute(34, "aria-label", $"Move {Items[index]} down");
            builder.AddAttribute(35, "disabled", captured == Items.Count - 1);
            builder.AddAttribute(36, "onclick", EventCallback.Factory.Create(this, () => MoveByAsync(captured, 1)));
            builder.AddContent(37, "▼");
            builder.CloseElement();

            builder.CloseElement();   // controls

            builder.CloseElement();   // item
        }

        builder.CloseElement();
    }
}
