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

    private async Task MergeCellRightAsync()
    {
        if (_rte is not null && TryParseCell(await _rte.GetTableCellAsync(), out int t, out int r, out int c))
        {
            await CommitModelEditAsync(MergeCellRight(Current, t, r, c));
        }
    }

    private async Task SplitCellAsync()
    {
        if (_rte is not null && TryParseCell(await _rte.GetTableCellAsync(), out int t, out int r, out int c))
        {
            await CommitModelEditAsync(UnmergeCell(Current, t, r, c));
        }
    }

    // Merges the caret cell with the next visible cell to its right (horizontal merge): the anchor
    // absorbs the neighbour's span and content; the row stays rectangular via covered cells. The
    // column is a VISUAL index (covered cells aren't rendered), mapped back to the model.
    private static WordDocument MergeCellRight(WordDocument document, int tableIndex, int row, int visualCol) =>
        MapTable(document, tableIndex, table =>
        {
            if (row < 0 || row >= table.Rows.Count)
            {
                return table;
            }

            var cells = table.Rows[row].Cells.ToList();
            int a = VisibleToModel(cells, visualCol);
            int b = a < 0 ? -1 : NextVisible(cells, a + 1);
            if (b < 0)
            {
                return table; // nothing to the right
            }

            int newSpan = cells[a].ColSpan + cells[b].ColSpan;
            cells[a] = cells[a] with { ColSpan = newSpan, Runs = cells[a].Runs.Concat(cells[b].Runs).ToList() };
            for (int i = a + 1; i < a + newSpan && i < cells.Count; i++)
            {
                cells[i] = new WordTableCell([], null, 0);
            }

            return ReplaceRow(table, row, cells);
        });

    // Splits a merged cell back into individual cells (anchor ColSpan -> 1, covered cells restored).
    private static WordDocument UnmergeCell(WordDocument document, int tableIndex, int row, int visualCol) =>
        MapTable(document, tableIndex, table =>
        {
            if (row < 0 || row >= table.Rows.Count)
            {
                return table;
            }

            var cells = table.Rows[row].Cells.ToList();
            int a = VisibleToModel(cells, visualCol);
            if (a < 0 || cells[a].ColSpan <= 1)
            {
                return table;
            }

            int span = cells[a].ColSpan;
            cells[a] = cells[a] with { ColSpan = 1 };
            for (int i = a + 1; i < a + span && i < cells.Count; i++)
            {
                cells[i] = new WordTableCell([]);
            }

            return ReplaceRow(table, row, cells);
        });

    private static WordTable ReplaceRow(WordTable table, int row, List<WordTableCell> cells)
    {
        var rows = table.Rows.ToArray();
        rows[row] = new WordTableRow(cells);
        return new WordTable(rows);
    }

    // The model index of the visualCol-th rendered (non-covered) cell, or -1.
    private static int VisibleToModel(List<WordTableCell> cells, int visualCol)
    {
        int visible = -1;
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].ColSpan != 0 && ++visible == visualCol)
            {
                return i;
            }
        }

        return -1;
    }

    private static int NextVisible(List<WordTableCell> cells, int from)
    {
        for (int i = from; i < cells.Count; i++)
        {
            if (cells[i].ColSpan != 0)
            {
                return i;
            }
        }

        return -1;
    }

    // Applies a transform to the tableIndex-th table (document order); other blocks pass through.
    private static WordDocument MapTable(WordDocument document, int tableIndex, Func<WordTable, WordTable> map)
    {
        var blocks = new WordBlock[document.Blocks.Count];
        int seen = -1;
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is WordTable table)
            {
                seen++;
                blocks[i] = seen == tableIndex ? map(table) : table;
            }
            else
            {
                blocks[i] = document.Blocks[i];
            }
        }

        return new WordDocument(blocks);
    }

    // Returns a copy of the document with the given cell of the tableIndex-th table shaded.
    // Out-of-range targets are a no-op.
    private static WordDocument SetCellShading(WordDocument document, int tableIndex, int row, int col, string color)
    {
        var blocks = new WordBlock[document.Blocks.Count];
        int seen = -1;
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is WordTable table)
            {
                seen++;
                blocks[i] = seen == tableIndex ? ShadeCell(table, row, col, color) : table;
            }
            else
            {
                blocks[i] = document.Blocks[i];
            }
        }

        return new WordDocument(blocks);
    }

    private static WordTable ShadeCell(WordTable table, int row, int col, string color)
    {
        if (row < 0 || row >= table.Rows.Count || col < 0 || col >= table.Rows[row].Cells.Count)
        {
            return table;
        }

        var rows = table.Rows.ToArray();
        var cells = rows[row].Cells.ToArray();
        cells[col] = cells[col] with { Shading = color };
        rows[row] = new WordTableRow(cells);
        return new WordTable(rows);
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
