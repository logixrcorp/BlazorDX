using BlazorDX.Interop;
using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless menu: an anchored, dismissible list of actions with full
/// keyboard navigation (arrows, Home/End, Enter/Space to select, Escape/Tab to
/// close). Composes anchored positioning + dismissal + the roving-tabindex
/// primitive. Renders no markup itself.
/// </summary>
public class MenuPrimitive : ComponentBase, IAsyncDisposable
{
    /// <summary>Roving-tabindex state shared with the styled layer for highlight/tabindex.</summary>
    protected readonly RovingTabIndex Roving = new();

    private ElementReference[] itemElements = [];
    private bool behaviorsActive;

    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>The clickable anchor; clicking toggles the menu.</summary>
    [Parameter] public RenderFragment? Trigger { get; set; }

    [Parameter] public IReadOnlyList<MenuItem> Items { get; set; } = [];

    [Parameter] public string Side { get; set; } = "bottom";

    [Parameter] public string Align { get; set; } = "start";

    [Parameter] public int Offset { get; set; } = 4;

    [Parameter] public int ExitDurationMs { get; set; } = 120;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;
    [Inject] private IAnchorInterop Anchor { get; set; } = default!;

    protected string AnchorId { get; } = $"dx-menu-anchor-{Guid.NewGuid():N}";
    protected string PanelId { get; } = $"dx-menu-panel-{Guid.NewGuid():N}";

    protected override void OnParametersSet()
    {
        Roving.Configure(Items.Count, index => !Items[index].Disabled);
        if (itemElements.Length != Items.Count)
        {
            itemElements = new ElementReference[Items.Count];
        }
    }

    protected Task ToggleAsync() => SetOpenAsync(!Open);

    protected bool IsActive(int index) => Roving.IsActive(index);

    protected void CaptureItem(int index, ElementReference element)
    {
        if (index < itemElements.Length)
        {
            itemElements[index] = element;
        }
    }

    protected async Task SelectAsync(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled)
        {
            return;
        }

        Items[index].OnSelect?.Invoke();
        await SetOpenAsync(false);
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
            case "Escape" or "Tab": await SetOpenAsync(false); break;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open && !behaviorsActive)
        {
            behaviorsActive = true;
            Roving.MoveFirst();
            await Anchor.AttachAsync(PanelId, AnchorId, Side, Align, Offset);
            await Overlay.OpenAsync(
                PanelId, AnchorId, trapFocus: false, lockScroll: false, closeOnEscape: true, closeOnOutsideClick: true, OnDismiss);
            await FocusActiveAsync();
        }
        else if (!Open && behaviorsActive)
        {
            behaviorsActive = false;
            Roving.Clear();
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }

    private async Task FocusActiveAsync()
    {
        StateHasChanged(); // update tabindex/highlight before moving focus
        int index = Roving.ActiveIndex;
        if (index >= 0 && index < itemElements.Length)
        {
            try
            {
                await itemElements[index].FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // Element not yet rendered (race during open); next render will focus.
            }
        }
    }

    private void OnDismiss() => _ = InvokeAsync(() => SetOpenAsync(false));

    private async Task SetOpenAsync(bool open)
    {
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(open);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (behaviorsActive)
        {
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }
}
