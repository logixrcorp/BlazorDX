using System.Text;
using BlazorDX.Primitives.Diagnostics;

namespace BlazorDX.Primitives.Grid;

/// <summary>
/// CSV export for <see cref="DataGridPrimitive{TRow}"/>. Kept in its own partial so
/// the core grid file stays under the 1000-line cap (this is the export concern,
/// not the data/ordering concern).
/// </summary>
/// <typeparam name="TRow">The row type, bound through a generated accessor.</typeparam>
public partial class DataGridPrimitive<TRow>
{
    /// <summary>
    /// Builds RFC 4180 CSV of the grid in display-column order over the currently
    /// filtered rows in their sorted order. Cell text comes from the generated
    /// accessor — no reflection. Hidden columns, group headers, and aggregates are
    /// excluded.
    /// </summary>
    protected string BuildCsv()
    {
        StringBuilder csv = new();
        AppendCsvRow(csv, display => Columns[ColumnOrder[display]].Header);
        foreach (int rowIndex in FilteredOrder())
        {
            int captured = rowIndex;
            AppendCsvRow(csv, display => Accessor.GetCellText(Items[captured], ColumnOrder[display]));
        }

        return csv.ToString();
    }

    /// <summary>Generates the CSV and triggers a client-side download.</summary>
    protected Task ExportCsvAsync() =>
        Dom.DownloadTextAsync(ExportFileName, "text/csv;charset=utf-8", BuildCsv()).AsTask();

    private void AppendCsvRow(StringBuilder csv, Func<int, string> cell)
    {
        bool first = true;
        for (int display = 0; display < Columns.Count; display++)
        {
            if (IsColumnHidden(ColumnOrder[display]))   // exclude hidden columns from export
            {
                continue;
            }

            if (!first)
            {
                csv.Append(',');
            }

            first = false;
            csv.Append(EscapeCsv(cell(display)));
        }

        csv.Append("\r\n");
    }

    private static string EscapeCsv(string value)
    {
        // Quote fields containing a delimiter, quote, or newline; double inner quotes.
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// Builds tab-separated text (header + rows) for the clipboard. Copies the
    /// selected rows, or all visible rows when nothing is selected. Hidden columns
    /// are excluded; tabs/newlines inside cells collapse to spaces.
    /// </summary>
    protected string BuildSelectionTsv()
    {
        IEnumerable<int> rows = selectedRows.Count > 0
            ? rowOrder.Where(selectedRows.Contains)
            : FilteredOrder();

        StringBuilder tsv = new();
        AppendTsvRow(tsv, display => Columns[ColumnOrder[display]].Header);
        foreach (int rowIndex in rows)
        {
            int captured = rowIndex;
            AppendTsvRow(tsv, display => Accessor.GetCellText(Items[captured], ColumnOrder[display]));
        }

        return tsv.ToString();
    }

    /// <summary>Copies <see cref="BuildSelectionTsv"/> to the system clipboard, reporting a failure.</summary>
    protected async Task CopySelectionAsync()
    {
        bool ok = await Dom.WriteClipboardAsync(BuildSelectionTsv());
        if (!ok)
        {
            Diagnostics.TryReportError("DxDataGrid.Clipboard", "Clipboard write failed (no permission or insecure context).");
        }
    }

    private void AppendTsvRow(StringBuilder tsv, Func<int, string> cell)
    {
        bool first = true;
        for (int display = 0; display < Columns.Count; display++)
        {
            if (IsColumnHidden(ColumnOrder[display]))
            {
                continue;
            }

            if (!first)
            {
                tsv.Append('\t');
            }

            first = false;
            tsv.Append(cell(display).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
        }

        tsv.Append('\n');
    }
}
