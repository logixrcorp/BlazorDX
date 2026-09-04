using BlazorDX.Primitives.Interaction;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Components;

/// <summary>Per-item render context handed to <see cref="DxFieldList{TItem}.ItemTemplate"/>.</summary>
public sealed class CollectionItemContext<TItem>
{
    public required int Index { get; init; }
    public required TItem Item { get; init; }

    /// <summary>Replaces this item's value in the list — for a scalar item (a nested item's own fields are mutated in place instead, via reference identity).</summary>
    public required Func<TItem, Task> SetItemAsync { get; init; }
}

/// <summary>
/// Tier 2 styled editable list: add/remove/reorder rows, each rendered by
/// <see cref="ItemTemplate"/>. Backs an array-kind form field — array-of-scalar
/// (<c>TItem = string</c>) and array-of-nested-object (<c>TItem</c> = the nested
/// model type) rows share this one component, differing only in what
/// <see cref="ItemTemplate"/> renders for each row. Inherits all reorder/add/remove
/// behavior from <see cref="CollectionEditPrimitive{T}"/>; row chrome mirrors
/// <see cref="DxSortableList"/>'s established convention, and the remove button
/// matches <c>DxChip</c>'s dismiss idiom.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
public sealed class DxFieldList<TItem> : CollectionEditPrimitive<TItem>
{
    /// <summary>Renders one row's editor for its current item.</summary>
    [Parameter, EditorRequired] public RenderFragment<CollectionItemContext<TItem>> ItemTemplate { get; set; } = default!;

    /// <summary>Text for the "add a row" button.</summary>
    [Parameter] public string? AddLabel { get; set; }

    /// <summary>Extra CSS classes appended to the list.</summary>
    [Parameter] public string? Class { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    // DxFieldListResources, not DxFieldList<TItem>: the default factory derives the resource name
    // from the closed generic type, so localizing against the component itself would look for a
    // different resource per TItem. Same rule as DxDataGridResources — see docs/localization.md.
    private DxStrings<DxFieldListResources>? s;

    private DxStrings<DxFieldListResources> S => s ??= new(Services);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", $"dx-fieldlist {Class}".TrimEnd());
        builder.AddAttribute(2, "role", "list");

        for (int index = 0; index < Items.Count; index++)
        {
            int captured = index;
            TItem item = Items[index];

            // No SetKey: keep DOM nodes position-stable so the captured element
            // references (and thus keyboard focus) follow the slot, not the item —
            // same reasoning DxSortableList already relies on.
            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "class", "dx-fieldlist-item");
            builder.AddAttribute(5, "role", "listitem");
            builder.AddAttribute(6, "draggable", "true");
            builder.AddAttribute(7, "tabindex", IsActive(index) ? "0" : "-1");
            builder.AddAttribute(8, "ondragstart", EventCallback.Factory.Create(this, () => OnDragStart(captured)));
            builder.AddAttribute(9, "ondragover", EventCallback.Factory.Create(this, () => { }));
            builder.AddEventPreventDefaultAttribute(10, "ondragover", true);
            builder.AddAttribute(11, "ondrop", EventCallback.Factory.Create(this, () => OnDropAsync(captured)));
            builder.AddAttribute(12, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, args => OnKeyDownAsync(args, captured)));
            builder.AddElementReferenceCapture(13, element => CaptureItem(captured, element));

            builder.OpenElement(14, "span");
            builder.AddAttribute(15, "class", "dx-fieldlist-handle");
            builder.AddAttribute(16, "aria-hidden", "true");
            builder.AddContent(17, "⠿");
            builder.CloseElement();

            builder.OpenElement(18, "div");
            builder.AddAttribute(19, "class", "dx-fieldlist-content");
            builder.AddContent(20, ItemTemplate(new CollectionItemContext<TItem>
            {
                Index = captured,
                Item = item,
                SetItemAsync = value => SetItemAsync(captured, value),
            }));
            builder.CloseElement();

            // Single-pointer (no-drag) reorder controls, same WCAG 2.5.7 alternative
            // DxSortableList already establishes, plus the remove button.
            builder.OpenElement(21, "span");
            builder.AddAttribute(22, "class", "dx-fieldlist-controls");

            builder.OpenElement(23, "button");
            builder.AddAttribute(24, "type", "button");
            builder.AddAttribute(25, "class", "dx-fieldlist-move");
            builder.AddAttribute(26, "tabindex", "-1");
            builder.AddAttribute(27, "aria-label", S["MoveItemUp", "Move item up"]);
            builder.AddAttribute(28, "disabled", captured == 0);
            builder.AddAttribute(29, "onclick", EventCallback.Factory.Create(this, () => MoveByAsync(captured, -1)));
            builder.AddContent(30, "▲");
            builder.CloseElement();

            builder.OpenElement(31, "button");
            builder.AddAttribute(32, "type", "button");
            builder.AddAttribute(33, "class", "dx-fieldlist-move");
            builder.AddAttribute(34, "tabindex", "-1");
            builder.AddAttribute(35, "aria-label", S["MoveItemDown", "Move item down"]);
            builder.AddAttribute(36, "disabled", captured == Items.Count - 1);
            builder.AddAttribute(37, "onclick", EventCallback.Factory.Create(this, () => MoveByAsync(captured, 1)));
            builder.AddContent(38, "▼");
            builder.CloseElement();

            builder.OpenElement(39, "button");
            builder.AddAttribute(40, "type", "button");
            builder.AddAttribute(41, "class", "dx-chip-remove dx-fieldlist-remove");
            builder.AddAttribute(42, "aria-label", S["RemoveItem", "Remove item"]);
            builder.AddAttribute(43, "onclick", EventCallback.Factory.Create(this, () => RemoveAtAsync(captured)));
            builder.AddContent(44, "×");
            builder.CloseElement();

            builder.CloseElement();   // controls
            builder.CloseElement();   // item
        }

        builder.OpenElement(50, "button");
        builder.AddAttribute(51, "type", "button");
        builder.AddAttribute(52, "class", "dx-fieldlist-add dx-btn-secondary");
        builder.AddAttribute(53, "onclick", EventCallback.Factory.Create(this, AddAsync));
        builder.AddContent(54, AddLabel ?? S["Add", "Add"]);
        builder.CloseElement();

        builder.CloseElement();
    }

    private Task SetItemAsync(int index, TItem value)
    {
        List<TItem> updated = new(Items);
        if (index >= 0 && index < updated.Count)
        {
            updated[index] = value;
        }

        return ItemsChanged.HasDelegate ? ItemsChanged.InvokeAsync(updated) : Task.CompletedTask;
    }
}

/// <summary>
/// Resource-name anchor for <see cref="DxFieldList{TItem}"/>, which is generic: the default
/// localizer factory derives a resource name from the <i>closed</i> type, so localizing against
/// the component itself would look for a different resource per <c>TItem</c>.
/// </summary>
public sealed class DxFieldListResources;
