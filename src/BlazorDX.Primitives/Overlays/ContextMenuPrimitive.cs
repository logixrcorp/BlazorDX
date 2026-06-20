using BlazorDX.Interop;
using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless context menu: right-click a wrapped region to open a menu at the
/// cursor. Reuses the roving-tabindex and dismissal primitives (shares the
/// <see cref="MenuItem"/> model with the click menu). Renders no markup itself.
/// </summary>
public class ContextMenuPrimitive : ComponentBase, IAsyncDisposable
{
    /// <summary>Roving state shared with the styled layer.</summary>
    protected readonly RovingTabIndex Roving = new();

    private ElementReference[] itemElements = [];
    private bool behaviorsActive;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public IReadOnlyList<MenuItem> Items { get; set; } = [];

    [Parameter] public int ExitDurationMs { get; set; } = 120;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;

    protected string PanelId { get; } = $"dx-ctx-{Guid.NewGuid():N}";

    protected bool IsOpen { get; private set; }

    /// <summary>Viewport coordinates the menu opens at.</summary>
    protected double X { get; private set; }

    protected double Y { get; private set; }

    protected override void OnParametersSet()
    {
        Roving.Configure(Items.Count, index => !Items[index].Disabled);
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

    protected void OnContextMenu(MouseEventArgs args)
    {
        X = args.ClientX;
        Y = args.ClientY;
        Roving.MoveFirst();
        IsOpen = true;
        StateHasChanged();
    }

    protected async Task SelectAsync(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled)
        {
            return;
        }

        Items[index].OnSelect?.Invoke();
        Close();
        await Task.CompletedTask;
    }

    protected async Task OnPanelKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown": Roving.MoveNext(); await FocusActiveAsync(); break;
            case "ArrowUp": Roving.MovePrevious(); await FocusActiveAsync(); break;
            case "Home": Roving.MoveFirst(); await FocusActiveAsync(); break;
            case "End": Roving.MoveLast(); await FocusActiveAsync(); break;
            case "Enter" or " ": await SelectAsync(Roving.ActiveIndex); break;
            case "Escape": Close(); break;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !behaviorsActive)
        {
            behaviorsActive = true;
            await Overlay.OpenAsync(
                PanelId, ignoreElementId: "", trapFocus: false, lockScroll: false, closeOnEscape: true, closeOnOutsideClick: true, OnDismiss);
            await FocusActiveAsync();
        }
        else if (!IsOpen && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
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
                // not yet rendered
            }
        }
    }

    private void Close()
    {
        IsOpen = false;
        StateHasChanged();
    }

    private void OnDismiss() => _ = InvokeAsync(() =>
    {
        Close();
        return Task.CompletedTask;
    });

    public async ValueTask DisposeAsync()
    {
        if (behaviorsActive)
        {
            await Overlay.CloseAsync(PanelId);
        }
    }
}
