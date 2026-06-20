using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Interaction;

/// <summary>A dashboard tile: a titled card whose body is arbitrary content.</summary>
/// <param name="Title">Header text (also the drag handle label).</param>
/// <param name="Content">The tile body.</param>
/// <param name="ColumnSpan">How many grid columns the tile spans (default 1).</param>
public readonly record struct TileItem(string Title, RenderFragment Content, int ColumnSpan = 1);

/// <summary>
/// Tier 1 headless tile dashboard: a set of tiles the user can reorder by dragging
/// a tile's header onto another, or with the keyboard (Alt+Arrow moves the focused
/// tile; Arrow alone moves focus). Order is a permutation over the tile list — the
/// tiles themselves never move — so it composes with the shared reorder helper.
/// Native HTML5 drag, no JS interop. Renders no markup itself.
/// </summary>
public class TileLayoutPrimitive : ComponentBase
{
    /// <summary>Roving state shared with the styled layer for focus/tabindex.</summary>
    protected readonly RovingTabIndex Roving = new();

    private int[] order = [];
    private ElementReference[] handles = [];
    private int dragPosition = -1;
    private int pendingFocus = -1;

    [Parameter, EditorRequired] public IReadOnlyList<TileItem> Tiles { get; set; } = [];

    /// <summary>Raised with the new display order (positions hold tile indices) after a reorder.</summary>
    [Parameter] public EventCallback<IReadOnlyList<int>> OrderChanged { get; set; }

    protected override void OnParametersSet()
    {
        if (order.Length != Tiles.Count)
        {
            order = new int[Tiles.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            handles = new ElementReference[Tiles.Count];
        }

        Roving.Configure(Tiles.Count);
        if (Roving.ActiveIndex < 0)
        {
            Roving.MoveFirst();
        }
    }

    /// <summary>The tile shown at a display position (left-to-right, top-to-bottom).</summary>
    protected TileItem TileAt(int displayPosition) => Tiles[order[displayPosition]];

    /// <summary>Display order: each position holds the index of the tile shown there.</summary>
    protected IReadOnlyList<int> Order => order;

    protected int TileCount => Tiles.Count;

    protected bool IsActive(int displayPosition) => Roving.IsActive(displayPosition);

    protected void CaptureHandle(int displayPosition, ElementReference element)
    {
        if (displayPosition < handles.Length)
        {
            handles[displayPosition] = element;
        }
    }

    protected void OnDragStart(int displayPosition) => dragPosition = displayPosition;

    protected async Task OnDropAsync(int displayPosition)
    {
        if (dragPosition >= 0 && dragPosition != displayPosition)
        {
            await ReorderAsync(dragPosition, displayPosition);
        }

        dragPosition = -1;
    }

    protected async Task OnKeyDownAsync(KeyboardEventArgs args, int displayPosition)
    {
        switch (args)
        {
            case { AltKey: true, Key: "ArrowLeft" or "ArrowUp" }:
                await ReorderAsync(displayPosition, displayPosition - 1);
                break;
            case { AltKey: true, Key: "ArrowRight" or "ArrowDown" }:
                await ReorderAsync(displayPosition, displayPosition + 1);
                break;
            case { Key: "ArrowLeft" or "ArrowUp" }:
                Roving.MovePrevious();
                await FocusActiveAsync();
                break;
            case { Key: "ArrowRight" or "ArrowDown" }:
                Roving.MoveNext();
                await FocusActiveAsync();
                break;
            case { Key: "Home" }: Roving.MoveFirst(); await FocusActiveAsync(); break;
            case { Key: "End" }: Roving.MoveLast(); await FocusActiveAsync(); break;
        }
    }

    private async Task ReorderAsync(int from, int to)
    {
        if (to < 0 || to >= order.Length || from == to)
        {
            return;
        }

        order = ListReorder.Move(order, from, to).ToArray();
        pendingFocus = to;          // keep focus on the moved tile after re-render
        Roving.MoveTo(to);
        StateHasChanged();

        if (OrderChanged.HasDelegate)
        {
            await OrderChanged.InvokeAsync(order);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (pendingFocus >= 0 && pendingFocus < handles.Length)
        {
            int index = pendingFocus;
            pendingFocus = -1;
            Roving.MoveTo(index);
            try
            {
                await handles[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Not yet rendered; ignore.
            }
        }
    }

    private async Task FocusActiveAsync()
    {
        StateHasChanged();
        int index = Roving.ActiveIndex;
        if (index >= 0 && index < handles.Length)
        {
            try
            {
                await handles[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Element not yet rendered; the next render will focus it.
            }
        }
    }
}
