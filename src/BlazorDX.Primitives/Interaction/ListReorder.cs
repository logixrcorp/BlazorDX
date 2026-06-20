namespace BlazorDX.Primitives.Interaction;

/// <summary>
/// Pure list-reordering helper shared by drag-and-drop and keyboard reordering.
/// No Blazor or DOM dependency, so it is unit-testable in isolation.
/// </summary>
public static class ListReorder
{
    /// <summary>
    /// Returns a new list with the item at <paramref name="from"/> moved to
    /// <paramref name="to"/>. Out-of-range or no-op moves return an unchanged copy.
    /// </summary>
    public static List<T> Move<T>(IReadOnlyList<T> items, int from, int to)
    {
        List<T> result = new(items);
        if (from < 0 || from >= result.Count || to < 0 || to >= result.Count || from == to)
        {
            return result;
        }

        T moved = result[from];
        result.RemoveAt(from);
        result.Insert(to, moved);
        return result;
    }
}
