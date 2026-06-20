namespace BlazorDX.Primitives.Navigation;

/// <summary>
/// Roving-tabindex state: tracks which item in a list is the active (tabbable)
/// one and moves it with arrow / Home / End semantics, wrapping around and
/// skipping disabled items. This is the keyboard-navigation backbone shared by
/// Menu, Listbox, Select, ComboBox, RadioGroup, and grid cells. Pure logic — no
/// Blazor or DOM dependency — so it is unit-testable in isolation.
/// </summary>
public sealed class RovingTabIndex
{
    private int count;
    private Func<int, bool> isEnabled = static _ => true;

    /// <summary>The active index, or -1 when nothing is active.</summary>
    public int ActiveIndex { get; private set; } = -1;

    /// <summary>Sets the item count and an optional enabled predicate (to skip disabled items).</summary>
    public void Configure(int itemCount, Func<int, bool>? enabled = null)
    {
        count = itemCount;
        isEnabled = enabled ?? (static _ => true);
        if (ActiveIndex >= count)
        {
            ActiveIndex = -1;
        }
    }

    public bool IsActive(int index) => index == ActiveIndex;

    public void Clear() => ActiveIndex = -1;

    /// <summary>Sets the active item directly (ignored if out of range or disabled).</summary>
    public void MoveTo(int index)
    {
        if (index >= 0 && index < count && isEnabled(index))
        {
            ActiveIndex = index;
        }
    }

    public void MoveFirst() => ActiveIndex = FirstEnabled();

    public void MoveLast() => ActiveIndex = LastEnabled();

    public void MoveNext() => ActiveIndex = ActiveIndex < 0 ? FirstEnabled() : Step(ActiveIndex, +1);

    public void MovePrevious() => ActiveIndex = ActiveIndex < 0 ? LastEnabled() : Step(ActiveIndex, -1);

    private int FirstEnabled()
    {
        for (int i = 0; i < count; i++)
        {
            if (isEnabled(i))
            {
                return i;
            }
        }

        return -1;
    }

    private int LastEnabled()
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if (isEnabled(i))
            {
                return i;
            }
        }

        return -1;
    }

    // Walks in `direction` from `from`, wrapping, until it finds an enabled item.
    private int Step(int from, int direction)
    {
        if (count == 0)
        {
            return -1;
        }

        int index = from;
        for (int taken = 0; taken < count; taken++)
        {
            index = (index + direction + count) % count;
            if (isEnabled(index))
            {
                return index;
            }
        }

        return from;
    }
}
