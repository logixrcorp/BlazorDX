using BlazorDX.Primitives.Navigation;
using BlazorDX.Primitives.Selection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDX.Primitives.Overlays;

/// <summary>
/// Tier 1 headless inline listbox: an always-visible list of options with roving
/// keyboard navigation and single- or multiple-selection. No positioning or
/// dismissal (it is inline, not a popup). Composes the roving-tabindex and
/// selection-state primitives; renders no markup itself.
/// </summary>
/// <typeparam name="TValue">The option value type.</typeparam>
public class ListboxPrimitive<TValue> : ComponentBase
{
    /// <summary>Roving-tabindex state shared with the styled layer.</summary>
    protected readonly RovingTabIndex Roving = new();

    private readonly SelectionState<TValue> selection = new();
    private ElementReference[] itemElements = [];

    [Parameter] public IReadOnlyList<ListOption<TValue>> Items { get; set; } = [];

    [Parameter] public bool Multiple { get; set; }

    /// <summary>Single-selection binding (used when <see cref="Multiple"/> is false).</summary>
    [Parameter] public TValue? Value { get; set; }

    [Parameter] public EventCallback<TValue> ValueChanged { get; set; }

    /// <summary>Multiple-selection binding (used when <see cref="Multiple"/> is true).</summary>
    [Parameter] public IReadOnlyCollection<TValue>? Values { get; set; }

    [Parameter] public EventCallback<IReadOnlyCollection<TValue>> ValuesChanged { get; set; }

    protected override void OnParametersSet()
    {
        selection.Multiple = Multiple;
        if (Multiple)
        {
            selection.Set(Values ?? []);
        }
        else if (Value is not null)
        {
            selection.Set([Value]);
        }
        else
        {
            selection.Clear();
        }

        Roving.Configure(Items.Count, index => !Items[index].Disabled);
        if (Roving.ActiveIndex < 0)
        {
            Roving.MoveFirst(); // keep one option tabbable
        }

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

    protected async Task ToggleAsync(int index)
    {
        if (index < 0 || index >= Items.Count || Items[index].Disabled)
        {
            return;
        }

        TValue value = Items[index].Value;
        selection.Toggle(value);

        if (Multiple)
        {
            if (ValuesChanged.HasDelegate)
            {
                await ValuesChanged.InvokeAsync(selection.Selected.ToList());
            }
        }
        else if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(value);
        }
    }

    protected async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown": Roving.MoveNext(); await FocusActiveAsync(); break;
            case "ArrowUp": Roving.MovePrevious(); await FocusActiveAsync(); break;
            case "Home": Roving.MoveFirst(); await FocusActiveAsync(); break;
            case "End": Roving.MoveLast(); await FocusActiveAsync(); break;
            case "Enter" or " ": await ToggleAsync(Roving.ActiveIndex); break;
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
