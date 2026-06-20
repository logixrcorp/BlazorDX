namespace BlazorDX.Primitives.Selection;

/// <summary>
/// Tracks selected values for single- or multiple-selection components (Select,
/// ComboBox, Listbox, DataGrid rows, TransferList). Pure logic — no Blazor or DOM
/// dependency — so it is unit-testable in isolation. Values are compared with the
/// default equality comparer for <typeparamref name="TValue"/>.
/// </summary>
public sealed class SelectionState<TValue>
{
    private readonly HashSet<TValue> selected = new();

    /// <summary>When true, multiple values may be selected; otherwise selecting replaces.</summary>
    public bool Multiple { get; set; }

    /// <summary>The selected values, in no particular order.</summary>
    public IReadOnlyCollection<TValue> Selected => selected;

    /// <summary>The single selected value (or default when nothing is selected).</summary>
    public TValue? Current => selected.Count > 0 ? selected.First() : default;

    public bool IsSelected(TValue value) => selected.Contains(value);

    /// <summary>Replaces the selection with the given values.</summary>
    public void Set(IEnumerable<TValue> values)
    {
        selected.Clear();
        foreach (TValue value in values)
        {
            selected.Add(value);
        }
    }

    public void Clear() => selected.Clear();

    /// <summary>
    /// Toggles a value. In single mode this selects exactly the value; in multiple
    /// mode it adds the value if absent or removes it if present.
    /// </summary>
    public void Toggle(TValue value)
    {
        if (Multiple)
        {
            if (!selected.Remove(value))
            {
                selected.Add(value);
            }
        }
        else
        {
            selected.Clear();
            selected.Add(value);
        }
    }
}
