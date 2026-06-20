using BlazorDX.Interop;
using BlazorDX.Primitives.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless combo box: a text input that filters options (typeahead) with
/// an anchored dropdown and single selection. Focus stays in the input; the active
/// option is tracked via roving state and surfaced through aria-activedescendant,
/// the WAI-ARIA combobox pattern. Composes positioning, dismissal, and
/// roving-tabindex. Renders no markup itself.
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public class ComboBoxPrimitive<TValue> : ComponentBase, IAsyncDisposable
{
    /// <summary>Roving state over the filtered options (highlight only; focus stays in the input).</summary>
    protected readonly RovingTabIndex Roving = new();

    private List<ListOption<TValue>> filtered = [];
    private bool behaviorsActive;
    private bool initialized;

    [Parameter] public IReadOnlyList<ListOption<TValue>> Items { get; set; } = [];

    [Parameter] public TValue? Value { get; set; }

    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }

    [Parameter] public string Placeholder { get; set; } = "Type to search...";

    [Parameter] public string Side { get; set; } = "bottom";

    [Parameter] public string Align { get; set; } = "start";

    [Parameter] public int Offset { get; set; } = 4;

    [Parameter] public int ExitDurationMs { get; set; } = 120;

    [Inject] private IOverlayInterop Overlay { get; set; } = default!;
    [Inject] private IAnchorInterop Anchor { get; set; } = default!;

    protected string AnchorId { get; } = $"dx-combo-input-{Guid.NewGuid():N}";
    protected string PanelId { get; } = $"dx-combo-panel-{Guid.NewGuid():N}";

    /// <summary>Current text in the input (also the filter term).</summary>
    protected string Filter { get; private set; } = string.Empty;

    protected bool IsOpen { get; private set; }

    protected IReadOnlyList<ListOption<TValue>> Filtered => filtered;

    /// <summary>The DOM id of the active option, for aria-activedescendant (empty when none).</summary>
    protected string ActiveOptionId =>
        Roving.ActiveIndex >= 0 ? $"{PanelId}-opt-{Roving.ActiveIndex}" : string.Empty;

    protected string OptionId(int index) => $"{PanelId}-opt-{index}";

    protected bool IsActive(int index) => Roving.IsActive(index);

    protected override void OnParametersSet()
    {
        if (!initialized)
        {
            initialized = true;
            ListOption<TValue>? selected = FindByValue(Value);
            if (selected is not null)
            {
                Filter = selected.Text;
            }
        }

        ApplyFilter();
    }

    protected void OnInput(ChangeEventArgs args)
    {
        Filter = args.Value?.ToString() ?? string.Empty;
        ApplyFilter();
        IsOpen = true; // stay open even with no matches, to show the empty-state message
    }

    protected async Task OnInputKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                if (!IsOpen)
                {
                    Open();
                }
                else
                {
                    Roving.MoveNext();
                }

                break;
            case "ArrowUp": Roving.MovePrevious(); break;
            case "Home": Roving.MoveFirst(); break;
            case "End": Roving.MoveLast(); break;
            case "Enter":
                if (IsOpen)
                {
                    await SelectAsync(Roving.ActiveIndex);
                }

                break;
            case "Escape": Close(); break;
        }
    }

    protected void OpenFromFocus()
    {
        if (!IsOpen && filtered.Count > 0)
        {
            Open();
        }
    }

    protected async Task SelectAsync(int filteredIndex)
    {
        if (filteredIndex < 0 || filteredIndex >= filtered.Count || filtered[filteredIndex].Disabled)
        {
            return;
        }

        ListOption<TValue> option = filtered[filteredIndex];
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(option.Value);
        }

        Filter = option.Text;
        ApplyFilter();
        Close();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !behaviorsActive)
        {
            behaviorsActive = true;
            await Anchor.AttachAsync(PanelId, AnchorId, Side, Align, Offset);
            await Overlay.OpenAsync(
                PanelId, AnchorId, trapFocus: false, lockScroll: false, closeOnEscape: false, closeOnOutsideClick: true, OnDismiss);
        }
        else if (!IsOpen && behaviorsActive)
        {
            behaviorsActive = false;
            await Overlay.CloseAsync(PanelId);
            await Anchor.DetachAsync(PanelId);
        }
    }

    // Recomputes the visible options. When the input still shows the selected
    // option's exact text, show all options (so the user can pick another);
    // otherwise filter by case-insensitive substring.
    private void ApplyFilter()
    {
        ListOption<TValue>? selected = FindByValue(Value);
        bool showAll = Filter.Length == 0 || (selected is not null && Filter == selected.Text);

        filtered = showAll
            ? Items.ToList()
            : Items.Where(option => option.Text.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToList();

        Roving.Configure(filtered.Count, index => !filtered[index].Disabled);
        Roving.MoveFirst();
    }

    private ListOption<TValue>? FindByValue(TValue? value)
    {
        if (value is null)
        {
            return null;
        }

        foreach (ListOption<TValue> option in Items)
        {
            if (EqualityComparer<TValue>.Default.Equals(option.Value, value))
            {
                return option;
            }
        }

        return null;
    }

    private void Open()
    {
        IsOpen = true;
        StateHasChanged();
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
            await Anchor.DetachAsync(PanelId);
        }
    }
}
