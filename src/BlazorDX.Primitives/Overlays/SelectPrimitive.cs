using BlazorDX.Interop;
using BlazorDX.Primitives.Navigation;
using BlazorDX.Primitives.Selection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless single-select dropdown. Composes the shared primitives —
/// anchored positioning, dismissal, roving-tabindex keyboard navigation, and
/// selection state — into one self-contained widget. Renders no markup itself.
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public class SelectPrimitive<TValue> : ComponentBase, IAsyncDisposable
{
    /// <summary>Roving-tabindex state, shared with the styled layer for highlight/tabindex.</summary>
    protected readonly RovingTabIndex Roving = new();

    private readonly SelectionState<TValue> selection = new() { Multiple = false };
    private ElementReference[] itemElements = [];
    private bool behaviorsActive;

    [Parameter] public IReadOnlyList<ListOption<TValue>> Items { get; set; } = [];

    [Parameter] public TValue? Value { get; set; }

    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Select...";

    [Parameter] public string Side { get; set; } = "bottom";

    [Parameter] public string Align { get; set; } = "start";

    [Parameter] public int Offset { get; set; } = 4;

    [Parameter] public int ExitDurationMs { get; set; } = 120;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;
    [Inject] private IAnchorInterop Anchor { get; set; } = default!;

    protected string AnchorId { get; } = $"dx-select-anchor-{Guid.NewGuid():N}";
    protected string PanelId { get; } = $"dx-select-panel-{Guid.NewGuid():N}";

    protected bool IsOpen { get; private set; }

    /// <summary>Whether any option is currently selected (false shows the placeholder).</summary>
    protected bool HasSelection => SelectedIndex() >= 0;

    /// <summary>The label of the selected option, or the placeholder when none is selected.</summary>
    protected string DisplayText
    {
        get
        {
            int index = SelectedIndex();
            return index >= 0 ? Items[index].Text : Placeholder;
        }
    }

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            selection.Set([Value]);
        }
        else
        {
            selection.Clear();
        }

        Roving.Configure(Items.Count, index => !Items[index].Disabled);
        if (itemElements.Length != Items.Count)
        {
            itemElements = new ElementReference[Items.Count];
        }
    }

    protected bool IsActive(int index) => Roving.IsActive(index);

    protected bool IsSelected(int index) => selection.IsSelected(Items[index].Value);

    protected void CaptureItem(int index, ElementReference element)
    {
        if (index < itemElements.Length)
        {
            itemElements[index] = element;
        }
    }

    protected void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    protected async Task SelectAsync(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled)
        {
            return;
        }

        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(Items[index].Value);
        }

        Close();
    }

    protected async Task OnTriggerKeyDownAsync(KeyboardEventArgs args)
    {
        if (!IsOpen && args.Key is "ArrowDown" or "Enter" or " ")
        {
            Open();
        }
        else if (IsOpen)
        {
            await OnPanelKeyDownAsync(args);
        }
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
            case "Escape" or "Tab": Close(); break;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !behaviorsActive)
        {
            behaviorsActive = true;
            await Anchor.AttachAsync(PanelId, AnchorId, Side, Align, Offset);
            await Overlay.OpenAsync(
                PanelId, AnchorId, trapFocus: false, lockScroll: false, closeOnEscape: true, closeOnOutsideClick: true, OnDismiss);
            await FocusActiveAsync();
        }
        else if (!IsOpen && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }

    private void Open()
    {
        // Start keyboard navigation on the selected option, or the first enabled one.
        int selected = SelectedIndex();
        if (selected >= 0)
        {
            Roving.MoveFirst();
            while (Roving.ActiveIndex != selected && Roving.ActiveIndex >= 0)
            {
                int before = Roving.ActiveIndex;
                Roving.MoveNext();
                if (Roving.ActiveIndex == before)
                {
                    break;
                }
            }
        }
        else
        {
            Roving.MoveFirst();
        }

        IsOpen = true;
        StateHasChanged();
    }

    private void Close()
    {
        IsOpen = false;
        Roving.Clear();
        StateHasChanged();
    }

    private int SelectedIndex()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (selection.IsSelected(Items[i].Value))
            {
                return i;
            }
        }

        return -1;
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
            await Anchor.DetachAsync(PanelId);
        }
    }
}
