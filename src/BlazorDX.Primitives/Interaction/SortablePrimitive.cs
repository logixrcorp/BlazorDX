using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Interaction;

/// <summary>
/// Tier 1 headless sortable list: reorder items by dragging or with the keyboard
/// (Alt+Arrow moves the focused item; Arrow alone moves focus). Drag uses native
/// HTML5 drag events — no JS interop. Composes the roving-tabindex primitive for
/// keyboard focus. Renders no markup itself.
/// </summary>
public class SortablePrimitive : ComponentBase
{
    /// <summary>Roving state shared with the styled layer for focus/tabindex.</summary>
    protected readonly RovingTabIndex Roving = new();

    private ElementReference[] itemElements = [];
    private int dragIndex = -1;
    private int pendingFocus = -1;

    [Parameter] public IReadOnlyList<string> Items { get; set; } = [];

    [Parameter] public EventCallback<IReadOnlyList<string>> ItemsChanged { get; set; }

    protected override void OnParametersSet()
    {
        Roving.Configure(Items.Count);
        if (Roving.ActiveIndex < 0)
        {
            Roving.MoveFirst();
        }

        if (itemElements.Length != Items.Count)
        {
            itemElements = new ElementReference[Items.Count];
        }
    }

    protected bool IsActive(int index) => Roving.IsActive(index);

    protected void CaptureItem(int index, ElementReference element)
    {
        if (index < itemElements.Length)
        {
            itemElements[index] = element;
        }
    }

    protected void OnDragStart(int index) => dragIndex = index;

    protected async Task OnDropAsync(int index)
    {
        if (dragIndex >= 0 && dragIndex != index)
        {
            await ReorderAsync(dragIndex, index);
        }

        dragIndex = -1;
    }

    protected async Task OnKeyDownAsync(KeyboardEventArgs args, int index)
    {
        switch (args)
        {
            case { AltKey: true, Key: "ArrowUp" }: await ReorderAsync(index, index - 1); break;
            case { AltKey: true, Key: "ArrowDown" }: await ReorderAsync(index, index + 1); break;
            case { Key: "ArrowUp" }: Roving.MovePrevious(); await FocusActiveAsync(); break;
            case { Key: "ArrowDown" }: Roving.MoveNext(); await FocusActiveAsync(); break;
            case { Key: "Home" }: Roving.MoveFirst(); await FocusActiveAsync(); break;
            case { Key: "End" }: Roving.MoveLast(); await FocusActiveAsync(); break;
        }
    }

    private async Task ReorderAsync(int from, int to)
    {
        if (to < 0 || to >= Items.Count || from == to)
        {
            return;
        }

        List<string> reordered = ListReorder.Move(Items, from, to);
        pendingFocus = to; // keep focus on the moved item after the parent re-renders
        Roving.MoveTo(to);

        if (ItemsChanged.HasDelegate)
        {
            await ItemsChanged.InvokeAsync(reordered);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (pendingFocus >= 0 && pendingFocus < itemElements.Length)
        {
            int index = pendingFocus;
            pendingFocus = -1;
            Roving.MoveTo(index);
            try
            {
                await itemElements[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Element not yet rendered; ignore.
            }
        }
    }

    private async Task FocusActiveAsync()
    {
        StateHasChanged();
        int index = Roving.ActiveIndex;
        if (index >= 0 && index < itemElements.Length)
        {
            try
            {
                await itemElements[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Element not yet rendered; the next render will focus it.
            }
        }
    }
}
