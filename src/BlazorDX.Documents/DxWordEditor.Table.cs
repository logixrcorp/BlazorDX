namespace BlazorDX.Documents;

/// <summary>
/// Table-editing for <see cref="DxWordEditor"/>: insert/delete the row or column the caret
/// is in. The caret's cell is resolved via the editor's selection bridge
/// (<c>GetTableCellAsync</c>); the edit then runs on the document model and re-seeds the
/// editor through the shared commit path (so it is undoable and round-trips to .docx).
/// </summary>
public sealed partial class DxWordEditor
{
    private enum TableOp
    {
        InsertRow,
        DeleteRow,
        InsertColumn,
        DeleteColumn,
    }

    private async Task TableEditAsync(TableOp op)
    {
        if (_rte is null)
        {
            return;
        }

        // "tableIndex,rowIndex,colIndex" of the caret, or "" when not in a table (no-op).
        if (!TryParseCell(await _rte.GetTableCellAsync(), out int table, out int row, out int col))
        {
            return;
        }

        await CommitModelEditAsync(ApplyTableEdit(Current, table, op, row, col));
    }

    private static bool TryParseCell(string value, out int table, out int row, out int col)
    {
        table = row = col = 0;
        string[] parts = value.Split(',');
        return parts.Length == 3
            && int.TryParse(parts[0], out table)
            && int.TryParse(parts[1], out row)
            && int.TryParse(parts[2], out col);
    }

    // Returns a copy of the document with the requested edit applied to the tableIndex-th
    // table (in document order). Out-of-range targets and last-row/last-column deletes are
    // no-ops. Pure and side-effect-free, so it is unit-testable without the editor.
    private static WordDocument ApplyTableEdit(WordDocument document, int tableIndex, TableOp op, int row, int col)
    {
        var blocks = new WordBlock[document.Blocks.Count];
        int seen = -1;
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is WordTable table)
            {
                seen++;
                blocks[i] = seen == tableIndex ? EditTable(table, op, row, col) : table;
            }
            else
            {
                blocks[i] = document.Blocks[i];
            }
        }

        return new WordDocument(blocks);
    }

    private static WordTable EditTable(WordTable table, TableOp op, int row, int col)
    {
        var rows = table.Rows.Select(r => new List<WordTableCell>(r.Cells)).ToList();
        int columns = rows.Count > 0 ? rows[0].Count : 0;

        switch (op)
        {
            case TableOp.InsertRow:
                var blank = new List<WordTableCell>(columns);
                for (int c = 0; c < columns; c++)
                {
                    blank.Add(new WordTableCell([]));
                }

                rows.Insert(Math.Clamp(row, 0, rows.Count), blank);
                break;

            case TableOp.DeleteRow:
                if (rows.Count > 1 && row >= 0 && row < rows.Count)
                {
                    rows.RemoveAt(row);
                }

                break;

            case TableOp.InsertColumn:
                foreach (List<WordTableCell> r in rows)
                {
                    r.Insert(Math.Clamp(col, 0, r.Count), new WordTableCell([]));
                }

                break;

            case TableOp.DeleteColumn:
                if (columns > 1)
                {
                    foreach (List<WordTableCell> r in rows)
                    {
                        if (col >= 0 && col < r.Count)
                        {
                            r.RemoveAt(col);
                        }
                    }
                }

                break;
        }

        return new WordTable(rows.Select(r => new WordTableRow(r)).ToArray());
    }
}
