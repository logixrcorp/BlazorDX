using System.Globalization;
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
            csv.Append(EscapeCsv(Field(cell(display))));
        }

        csv.Append("\r\n");
    }

    // Applies formula-injection neutralization (when enabled) before any CSV/TSV quoting.
    private string Field(string value) => SanitizeExportFormulas ? NeutralizeFormula(value) : value;

    // CSV/Formula injection (CWE-1236): when a spreadsheet opens a CSV/TSV, a cell whose first
    // character is one of these is evaluated as a formula or command. Prefixing a single quote
    // forces the cell to text and defeats it, while leaving the visible value otherwise intact.
    private static string NeutralizeFormula(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        char first = value[0];
        if (first is not ('=' or '+' or '-' or '@' or '\t' or '\r' or '\n'))
        {
            return value;
        }

        // A leading + or - on an otherwise-numeric value is a signed number, not a formula — don't
        // mangle legitimate data. '=', '@', and the control characters are never valid leading data.
        if (first is '+' or '-'
            && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            return value;
        }

        return "'" + value;
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
            tsv.Append(Field(cell(display)).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
        }

        tsv.Append('\n');
    }
}
