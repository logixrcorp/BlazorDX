namespace BlazorDX.Primitives.Grid;

/// <summary>
/// In-memory row grouping for <see cref="DataGridPrimitive{TRow}"/>: buckets the current
/// (filtered, sorted) row order by the grouped column into collapsible groups, and flattens
/// them — a header per group plus the rows of expanded groups — into the slot list that
/// drives virtualization. Server-side grouping lives in the RemoteGroup partial; this is the
/// path used when all rows are held in memory.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    // Rebuilds the flat slot list from the current (filtered) row order and grouping.
    private void RebuildSlots()
    {
        int[] order = FilteredOrder();
        List<GridRowSlot> rebuilt = new(order.Length);

        if (!IsGrouped)
        {
            groups = [];
            foreach (int rowIndex in order)
            {
                rebuilt.Add(new GridRowSlot(false, -1, rowIndex));
            }

            slots = rebuilt;
            return;
        }

        groups = BuildGroups(order);
        for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            rebuilt.Add(new GridRowSlot(true, groupIndex, -1));
            if (IsExpanded(groups[groupIndex].Key))
            {
                foreach (int rowIndex in groups[groupIndex].RowIndices)
                {
                    rebuilt.Add(new GridRowSlot(false, groupIndex, rowIndex));
                }
            }
        }

        slots = rebuilt;
    }

    private List<Group> BuildGroups(int[] order)
    {
        List<Group> built = new();
        Dictionary<string, Group> byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (int rowIndex in order)
        {
            string key = Accessor.GetCellText(Items[rowIndex], GroupByColumn);
            if (!byKey.TryGetValue(key, out Group? group))
            {
                group = new Group(key);
                byKey[key] = group;
                built.Add(group);
            }

            group.RowIndices.Add(rowIndex);
        }

        built.Sort((left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));
        return built;
    }

    private bool IsExpanded(string key) => !groupExpanded.TryGetValue(key, out bool expanded) || expanded;

    private sealed class Group
    {
        public Group(string key) => Key = key;

        public string Key { get; }

        public List<int> RowIndices { get; } = [];
    }
}
