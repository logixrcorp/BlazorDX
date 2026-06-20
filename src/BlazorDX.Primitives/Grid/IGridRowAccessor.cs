namespace BlazorDX.Primitives.Grid;

/// <summary>Describes one grid column. Emitted by the source generator.</summary>
/// <param name="Header">Column header text.</param>
/// <param name="Order">Left-to-right position.</param>
/// <param name="IsNumeric">Whether the column's values are numeric (and sortable by the Rust kernels).</param>
public sealed record GridColumnInfo(string Header, int Order, bool IsNumeric);

/// <summary>
/// Strongly-typed, reflection-free access to a row type's columns. The source
/// generator implements this for every <see cref="GridRowAttribute"/> type, so
/// the grid reads cell text and numeric values through generated <c>switch</c>
/// expressions rather than <c>PropertyInfo.GetValue</c>.
/// </summary>
/// <typeparam name="TRow">The row type being bound.</typeparam>
public interface IGridRowAccessor<in TRow>
{
    /// <summary>The columns, already ordered by <see cref="GridColumnInfo.Order"/>.</summary>
    IReadOnlyList<GridColumnInfo> Columns { get; }

    /// <summary>Returns the display text for a cell.</summary>
    string GetCellText(TRow row, int columnIndex);

    /// <summary>Returns a cell's numeric value, or <see cref="double.NaN"/> for non-numeric columns.</summary>
    double GetCellValue(TRow row, int columnIndex);

    /// <summary>Whether a column can be edited (its property has a usable setter).</summary>
    bool CanEditColumn(int columnIndex);

    /// <summary>
    /// Parses <paramref name="value"/> and writes it back to the row's property for
    /// an editable column. No-op for non-editable columns or unparseable input.
    /// Generated as a typed <c>switch</c> — no reflection.
    /// </summary>
    void SetCellText(TRow row, int columnIndex, string value);
}
