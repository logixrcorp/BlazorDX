namespace BlazorDX.Primitives.Grid;

/// <summary>
/// PDF export for <see cref="DataGridPrimitive{TRow}"/>. Sits beside the CSV and
/// Excel exports so the core grid file stays under the 1000-line cap; the actual
/// PDF generation lives in <see cref="PdfWriter"/>.
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    /// <summary>
    /// Builds a paginated table PDF of the grid in display-column order over the
    /// currently filtered rows in their sorted order. Cell text comes from the
    /// generated accessor — no reflection. Hidden columns, group headers, and
    /// aggregates are excluded, matching the CSV and Excel exports.
    /// </summary>
    protected byte[] BuildPdf()
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

        return PdfWriter.Write(headers, rows);
    }

    /// <summary>Generates the PDF and triggers a client-side download.</summary>
    protected Task ExportPdfAsync() =>
        Dom.DownloadBytesAsync(PdfFileName, PdfWriter.MimeType, BuildPdf()).AsTask();
}
