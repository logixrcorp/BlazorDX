namespace BlazorDX.Primitives.Grid;

/// <summary>
/// One entry in the grid's virtualized display list. The grid renders a flat list
/// of slots — group headers interleaved with data rows — so the same windowing math
/// drives both grouped and ungrouped views.
/// </summary>
/// <param name="IsGroupHeader">True for a group header, false for a data row.</param>
/// <param name="GroupIndex">Index into the grid's group list (header slots, and the owning group of a row).</param>
/// <param name="RowIndex">Absolute index into <c>Items</c> for data slots; -1 for header slots.</param>
public readonly record struct GridRowSlot(bool IsGroupHeader, int GroupIndex, int RowIndex);
