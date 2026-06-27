using Microsoft.AspNetCore.Components;

namespace BlazorDX.Documents;

/// <summary>Which editing engine a <see cref="DxWordEditor"/> uses for inline formatting.</summary>
public enum EditingCore
{
    /// <summary>
    /// The default: formatting goes through the browser's <c>contentEditable</c> +
    /// <c>document.execCommand</c>, and the model is re-derived from the edited HTML.
    /// </summary>
    ContentEditable,

    /// <summary>
    /// The model-driven core (ADR-0015): inline-format commands mutate the authoritative
    /// <see cref="WordDocument"/> over an owned selection, the surface is re-rendered from
    /// the model, and the selection is restored. No <c>execCommand</c>.
    /// </summary>
    ModelDriven,
}

/// <summary>
/// The model-driven inline-formatting commands for <see cref="DxWordEditor"/> (ADR-0015,
/// Phase B). When <see cref="EditingCore"/> is <see cref="EditingCore.ModelDriven"/>, the
/// bold/italic/underline/strikethrough toolbar buttons are intercepted here: the caret's
/// owned selection is read as a run-container + character range, the edit is applied to the
/// immutable model as a pure function, and the surface is re-seeded in place and the
/// selection restored — so the model, not the DOM, is the source of truth.
/// </summary>
public sealed partial class DxWordEditor
{
    private enum InlineFormat
    {
        Bold,
        Italic,
        Underline,
        Strike,
    }

    /// <summary>
    /// The editing engine for inline formatting. Defaults to
    /// <see cref="EditingCore.ContentEditable"/>; opt into <see cref="EditingCore.ModelDriven"/>
    /// to drive bold/italic/underline/strikethrough through the model (ADR-0015).
    /// </summary>
    [Parameter] public EditingCore EditingCore { get; set; } = EditingCore.ContentEditable;

    // Handles an intercepted inline-format command from the editor toolbar: read the owned
    // selection, toggle the format on the model over that range, re-seed in place, restore
    // the selection. A no-op when nothing is selected or the selection isn't addressable.
    private async Task HandleModelCommandAsync(string command)
    {
        if (_rte is null || MapFormat(command) is not InlineFormat format)
        {
            return;
        }

        if (!TryParseRange(await _rte.GetSelectionRangeAsync(), out int container, out int start, out int end)
            || start >= end)
        {
            return; // no selection, collapsed caret, or a selection we can't address yet
        }

        await CommitModelEditInPlaceAsync(ToggleInline(Current, container, start, end, format));
        // Toggling preserves text length, so the offsets are unchanged — restore the selection.
        await _rte.SetSelectionRangeAsync(container, start, end);
    }

    private static InlineFormat? MapFormat(string command) => command switch
    {
        "bold" => InlineFormat.Bold,
        "italic" => InlineFormat.Italic,
        "underline" => InlineFormat.Underline,
        "strikeThrough" => InlineFormat.Strike,
        _ => null,
    };

    private static bool TryParseRange(string value, out int container, out int start, out int end)
    {
        container = start = end = 0;
        string[] parts = value.Split(',');
        return parts.Length == 3
            && int.TryParse(parts[0], out container)
            && int.TryParse(parts[1], out start)
            && int.TryParse(parts[2], out end);
    }

    // Returns a copy of the document with the inline format toggled over characters
    // [start, end) of the run-container at containerIndex (run-containers numbered in
    // document order: heading/paragraph = one each, then each list item, then each table
    // cell — matching WordHtml.ToHtml and the bridge's selector). Toggle semantics: if every
    // selected character already has the format it is cleared, otherwise it is set. Pure and
    // side-effect-free, so it is unit-testable without the editor.
    private static WordDocument ToggleInline(
        WordDocument document, int containerIndex, int start, int end, InlineFormat format)
    {
        int seen = -1;
        var blocks = new WordBlock[document.Blocks.Count];
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            blocks[i] = MapBlock(document.Blocks[i], ref seen, containerIndex, start, end, format);
        }

        return new WordDocument(blocks);
    }

    private static WordBlock MapBlock(
        WordBlock block, ref int seen, int target, int start, int end, InlineFormat format)
    {
        switch (block)
        {
            case WordHeading heading:
                seen++;
                return seen == target ? heading with { Runs = Toggle(heading.Runs, start, end, format) } : heading;

            case WordParagraph paragraph:
                seen++;
                return seen == target ? paragraph with { Runs = Toggle(paragraph.Runs, start, end, format) } : paragraph;

            case WordList list:
            {
                var items = new IReadOnlyList<WordRun>[list.Items.Count];
                for (int k = 0; k < list.Items.Count; k++)
                {
                    seen++;
                    items[k] = seen == target ? Toggle(list.Items[k], start, end, format) : list.Items[k];
                }

                return list with { Items = items };
            }

            case WordTable table:
            {
                var rows = new WordTableRow[table.Rows.Count];
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    var cells = new WordTableCell[table.Rows[r].Cells.Count];
                    for (int c = 0; c < table.Rows[r].Cells.Count; c++)
                    {
                        seen++;
                        WordTableCell cell = table.Rows[r].Cells[c];
                        cells[c] = seen == target ? new WordTableCell(Toggle(cell.Runs, start, end, format)) : cell;
                    }

                    rows[r] = new WordTableRow(cells);
                }

                return new WordTable(rows);
            }

            default:
                return block; // images and any future container-less block
        }
    }

    // Splits runs at the [start, end) boundaries and sets/clears the format on the slice.
    private static IReadOnlyList<WordRun> Toggle(
        IReadOnlyList<WordRun> runs, int start, int end, InlineFormat format)
    {
        int length = 0;
        foreach (WordRun run in runs)
        {
            length += run.Text.Length;
        }

        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);
        if (start >= end)
        {
            return runs;
        }

        bool enable = !AllSet(runs, start, end, format); // toggle: all-set -> clear, else set

        var result = new List<WordRun>(runs.Count + 2);
        int pos = 0;
        foreach (WordRun run in runs)
        {
            int runStart = pos;
            int runEnd = pos + run.Text.Length;
            pos = runEnd;

            int s = Math.Max(start, runStart);
            int e = Math.Min(end, runEnd);
            if (s >= e)
            {
                result.Add(run); // no overlap with the selection
                continue;
            }

            if (s > runStart)
            {
                result.Add(run with { Text = run.Text[..(s - runStart)] });
            }

            result.Add(WithFormat(run with { Text = run.Text[(s - runStart)..(e - runStart)] }, format, enable));

            if (e < runEnd)
            {
                result.Add(run with { Text = run.Text[(e - runStart)..] });
            }
        }

        return Coalesce(result);
    }

    // True when every character in [start, end) already carries the format.
    private static bool AllSet(IReadOnlyList<WordRun> runs, int start, int end, InlineFormat format)
    {
        int pos = 0;
        foreach (WordRun run in runs)
        {
            int runStart = pos;
            int runEnd = pos + run.Text.Length;
            pos = runEnd;

            if (Math.Max(start, runStart) < Math.Min(end, runEnd) && !Has(run, format))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Has(WordRun run, InlineFormat format) => format switch
    {
        InlineFormat.Bold => run.Bold,
        InlineFormat.Italic => run.Italic,
        InlineFormat.Underline => run.Underline,
        InlineFormat.Strike => run.Strike,
        _ => false,
    };

    private static WordRun WithFormat(WordRun run, InlineFormat format, bool on) => format switch
    {
        InlineFormat.Bold => run with { Bold = on },
        InlineFormat.Italic => run with { Italic = on },
        InlineFormat.Underline => run with { Underline = on },
        InlineFormat.Strike => run with { Strike = on },
        _ => run,
    };

    // Merges adjacent runs with identical formatting (and drops empty fragments left by a
    // split), so toggling never fragments the model more than necessary.
    private static IReadOnlyList<WordRun> Coalesce(List<WordRun> runs)
    {
        var merged = new List<WordRun>(runs.Count);
        foreach (WordRun run in runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }

            if (merged.Count > 0 && SameFormat(merged[^1], run))
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
            }
            else
            {
                merged.Add(run);
            }
        }

        return merged;
    }

    private static bool SameFormat(WordRun a, WordRun b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline && a.Strike == b.Strike
        && a.Href == b.Href && a.Color == b.Color && a.Highlight == b.Highlight;
}
