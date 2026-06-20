namespace BlazorDX.Primitives.Grid;

/// <summary>
/// Excel (.xlsx) export for <see cref="DataGridPrimitive{TRow}"/>. Sits in its own
/// partial alongside the CSV export so the core grid file stays under the
/// 1000-line cap; the actual OOXML packaging lives in <see cref="XlsxWriter"/>.
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    /// <summary>
    /// Builds a real .xlsx workbook of the grid in display-column order over the
    /// currently filtered rows in their sorted order. Cell text comes from the
    /// generated accessor — no reflection. Hidden columns, group headers, and
    /// aggregates are excluded, matching the CSV export.
    /// </summary>
    protected byte[] BuildXlsx()
    {
        List<string> headers = new();
        List<int> exportedColumns = new();
        for (int display = 0; display < Columns.Count; display++)
        {
            int column = ColumnOrder[display];
            if (IsColumnHidden(column))   // exclude hidden columns from export
            {
                continue;
            }

            exportedColumns.Add(column);
            headers.Add(Columns[column].Header);
        }

        List<IReadOnlyList<string>> rows = new();
        foreach (int rowIndex in FilteredOrder())
        {
            string[] cells = new string[exportedColumns.Count];
            for (int c = 0; c < exportedColumns.Count; c++)
            {
                cells[c] = Accessor.GetCellText(Items[rowIndex], exportedColumns[c]);
            }

            rows.Add(cells);
        }

        return XlsxWriter.Write(headers, rows);
    }

    /// <summary>Generates the workbook and triggers a client-side download.</summary>
    protected Task ExportXlsxAsync() =>
        Dom.DownloadBytesAsync(ExcelFileName, XlsxWriter.MimeType, BuildXlsx()).AsTask();
}
