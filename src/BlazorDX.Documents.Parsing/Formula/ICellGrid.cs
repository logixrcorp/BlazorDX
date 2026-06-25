namespace BlazorDX.Documents.Formula;

/// <summary>
/// The evaluator's view of a sheet: a read-only source of already-computed cell
/// values addressed by row/column. The recalc engine implements this over a fully
/// computed grid; callers may also implement it over their own storage to evaluate
/// a single expression against existing data.
/// </summary>
public interface ICellGrid
{
    /// <summary>Total number of rows the grid exposes.</summary>
    int RowCount { get; }

    /// <summary>Total number of columns the grid exposes.</summary>
    int ColumnCount { get; }

    /// <summary>
    /// Returns the computed value at <paramref name="address"/>. Out-of-bounds
    /// coordinates return <see cref="CellValue.Blank"/> (an empty cell), never throw.
    /// </summary>
    CellValue GetValue(CellAddress address);
}

/// <summary>
/// A simple in-memory <see cref="ICellGrid"/> backed by a dense array of
/// <see cref="CellValue"/>. Used by the recalc engine to feed already-computed
/// dependencies back into formulas, and convenient for standalone evaluation.
/// </summary>
public sealed class ArrayCellGrid : ICellGrid
{
    private readonly CellValue[][] _cells;

    /// <summary>Creates a grid of the given size with every cell blank.</summary>
    public ArrayCellGrid(int rowCount, int columnCount)
    {
        RowCount = rowCount < 0 ? 0 : rowCount;
        ColumnCount = columnCount < 0 ? 0 : columnCount;
        _cells = new CellValue[RowCount][];
        for (int r = 0; r < RowCount; r++)
        {
            var row = new CellValue[ColumnCount];
            for (int c = 0; c < ColumnCount; c++)
            {
                row[c] = CellValue.Blank;
            }

            _cells[r] = row;
        }
    }

    /// <inheritdoc />
    public int RowCount { get; }

    /// <inheritdoc />
    public int ColumnCount { get; }

    /// <inheritdoc />
    public CellValue GetValue(CellAddress address)
    {
        if (address.Row < 0 || address.Row >= RowCount ||
            address.Column < 0 || address.Column >= ColumnCount)
        {
            return CellValue.Blank;
        }

        return _cells[address.Row][address.Column];
    }

    /// <summary>Writes a value at the given coordinates (no-op if out of bounds).</summary>
    public void SetValue(int row, int column, CellValue value)
    {
        if (row < 0 || row >= RowCount || column < 0 || column >= ColumnCount)
        {
            return;
        }

        _cells[row][column] = value;
    }
}
