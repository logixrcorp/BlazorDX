using BlazorDX.Components;
using Microsoft.AspNetCore.Components;

namespace BlazorDX.Documents;

/// <summary>Which editing engine a <see cref="DxWordEditor"/> uses for formatting.</summary>
public enum EditingCore
{
    /// <summary>
    /// The default: formatting goes through the browser's <c>contentEditable</c> +
    /// <c>document.execCommand</c>, and the model is re-derived from the edited HTML.
    /// </summary>
    ContentEditable,

    /// <summary>
    /// The model-driven core (ADR-0015): formatting commands mutate the authoritative
    /// <see cref="WordDocument"/> over an owned selection, the surface is re-rendered from
    /// the model, and the selection is restored. No <c>execCommand</c>.
    /// </summary>
    ModelDriven,
}

/// <summary>
/// The model-driven formatting commands for <see cref="DxWordEditor"/> (ADR-0015, Phases B
/// and D). When <see cref="EditingCore"/> is <see cref="EditingCore.ModelDriven"/>, the
/// toolbar's character commands (bold/italic/underline/strikethrough, clear formatting) and
/// paragraph commands (alignment) are intercepted here: the caret's owned selection is read
/// as a run-container + character range, the edit is applied to the immutable model as a pure
/// function, and the surface is re-seeded in place with the selection restored — so the
/// model, not the DOM, is the source of truth.
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
    /// The editing engine for formatting. Defaults to <see cref="EditingCore.ContentEditable"/>;
    /// opt into <see cref="EditingCore.ModelDriven"/> to drive the supported commands through
    /// the model (ADR-0015): bold/italic/underline/strikethrough, clear formatting, alignment.
    /// </summary>
    [Parameter] public EditingCore EditingCore { get; set; } = EditingCore.ContentEditable;

    // Handles an intercepted formatting command from the editor toolbar: read the owned
    // selection, apply the matching pure model transform, then commit (re-seed in place +
    // restore selection + history). A no-op when the selection can't be addressed.
    private async Task HandleModelCommandAsync(string command)
    {
        if (_rte is null)
        {
            return;
        }

        string range = await _rte.GetSelectionRangeAsync();
        if (!TryParseRange(range, out int container, out int start, out int end))
        {
            return;
        }

        // createLink needs an async URL prompt before the edit, so it is handled out of band.
        if (command == "createLink")
        {
            if (start >= end)
            {
                return;
            }

            string url = await _rte.PromptLinkAsync();
            if (!string.IsNullOrEmpty(url))
            {
                await CommitModelEditAsync(SetLink(Current, container, start, end, url), range);
            }

            return;
        }

        // Character commands need a non-empty selection; block commands apply at the caret.
        WordDocument? updated = command switch
        {
            "bold" or "italic" or "underline" or "strikeThrough" when start < end =>
                ToggleInline(Current, container, start, end, MapFormat(command)!.Value),
            "removeFormat" when start < end =>
                ClearInline(Current, container, start, end),
            "justifyLeft" or "justifyCenter" or "justifyRight" or "justifyFull" =>
                SetAlignment(Current, container, MapAlignment(command)),
            "formatBlock" =>
                ToggleHeading(Current, container),
            "insertUnorderedList" =>
                SetList(Current, container, ordered: false),
            "insertOrderedList" =>
                SetList(Current, container, ordered: true),
            _ => null,
        };

        if (updated is not null)
        {
            await CommitModelEditAsync(updated, range);
        }
    }

    // Handles the color inputs (text / highlight) in the model-driven core: set the chosen
    // color on the selected run range. A no-op for an empty selection or a malformed color.
    private async Task HandleModelColorAsync(ColorCommandArgs args)
    {
        if (_rte is null || !IsHexColor(args.Color))
        {
            return;
        }

        string range = await _rte.GetSelectionRangeAsync();
        if (!TryParseRange(range, out int container, out int start, out int end) || start >= end)
        {
            return;
        }

        bool highlight = args.Command == "hiliteColor";
        await CommitModelEditAsync(SetColor(Current, container, start, end, args.Color, highlight), range);
    }

    // A #RRGGBB color, the only shape the native color input produces and the model stores.
    private static bool IsHexColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (int i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static InlineFormat? MapFormat(string command) => command switch
    {
        "bold" => InlineFormat.Bold,
        "italic" => InlineFormat.Italic,
        "underline" => InlineFormat.Underline,
        "strikeThrough" => InlineFormat.Strike,
        _ => null,
    };

    private static WordAlignment MapAlignment(string command) => command switch
    {
        "justifyCenter" => WordAlignment.Center,
        "justifyRight" => WordAlignment.End,
        "justifyFull" => WordAlignment.Justify,
        _ => WordAlignment.Start, // justifyLeft / unknown
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

    // ---- Pure model transforms ---------------------------------------------------
    // Run-containers are numbered in document order: heading/paragraph = one each, then each
    // list item, then each table cell — matching WordHtml.ToHtml and the selection bridge.

    private static WordDocument ToggleInline(
        WordDocument document, int containerIndex, int start, int end, InlineFormat format) =>
        EditContainer(document, containerIndex, runs => Toggle(runs, start, end, format));

    private static WordDocument ClearInline(WordDocument document, int containerIndex, int start, int end) =>
        EditContainer(document, containerIndex, runs => ClearRange(runs, start, end));

    private static WordDocument SetColor(
        WordDocument document, int containerIndex, int start, int end, string color, bool highlight) =>
        EditContainer(document, containerIndex, runs => ApplyToRange(runs, start, end,
            run => highlight ? run with { Highlight = color } : run with { Color = color }));

    private static WordDocument SetLink(
        WordDocument document, int containerIndex, int start, int end, string href) =>
        EditContainer(document, containerIndex, runs => ApplyToRange(runs, start, end,
            run => run with { Href = href }));

    // Toggles the block owning the target run-container between a paragraph and a level-2
    // heading, preserving its runs and alignment. The toolbar's only formatBlock value is
    // <h2>, so this models it as a heading toggle. List items and table cells can't be
    // headings, so those are a no-op (the container index still advances).
    private static WordDocument ToggleHeading(WordDocument document, int containerIndex)
    {
        int seen = -1;
        var blocks = new WordBlock[document.Blocks.Count];
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            blocks[i] = HeadingBlock(document.Blocks[i], ref seen, containerIndex);
        }

        return new WordDocument(blocks);
    }

    private static WordBlock HeadingBlock(WordBlock block, ref int seen, int target)
    {
        switch (block)
        {
            case WordHeading h:
                seen++;
                return seen == target ? new WordParagraph(h.Runs, h.Alignment) : h;
            case WordParagraph p:
                seen++;
                return seen == target ? new WordHeading(2, p.Runs, p.Alignment) : p;
            case WordList l:
                seen += l.Items.Count;
                return l;
            case WordTable t:
                foreach (WordTableRow row in t.Rows)
                {
                    seen += row.Cells.Count;
                }

                return t;
            default:
                return block;
        }
    }

    // Sets paragraph alignment on the block owning the target run-container. Alignment lives
    // on headings and paragraphs only; list items and table cells carry none, so those are a
    // no-op (the container index still advances so the addressing stays consistent).
    private static WordDocument SetAlignment(WordDocument document, int containerIndex, WordAlignment alignment)
    {
        int seen = -1;
        var blocks = new WordBlock[document.Blocks.Count];
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            blocks[i] = AlignBlock(document.Blocks[i], ref seen, containerIndex, alignment);
        }

        return new WordDocument(blocks);
    }

    private static WordBlock AlignBlock(WordBlock block, ref int seen, int target, WordAlignment alignment)
    {
        switch (block)
        {
            case WordHeading h:
                seen++;
                return seen == target ? h with { Alignment = alignment } : h;
            case WordParagraph p:
                seen++;
                return seen == target ? p with { Alignment = alignment } : p;
            case WordList l:
                seen += l.Items.Count; // items occupy containers but carry no alignment
                return l;
            case WordTable t:
                foreach (WordTableRow row in t.Rows)
                {
                    seen += row.Cells.Count;
                }

                return t;
            default:
                return block;
        }
    }

    // Applies a run-list transform to the run-container at containerIndex (in document order),
    // rebuilding the owning block immutably. Container-less blocks (images) are passed through.
    private static WordDocument EditContainer(
        WordDocument document, int containerIndex, Func<IReadOnlyList<WordRun>, IReadOnlyList<WordRun>> map)
    {
        int seen = -1;
        var blocks = new WordBlock[document.Blocks.Count];
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            blocks[i] = MapBlock(document.Blocks[i], ref seen, containerIndex, map);
        }

        return new WordDocument(blocks);
    }

    private static WordBlock MapBlock(
        WordBlock block, ref int seen, int target, Func<IReadOnlyList<WordRun>, IReadOnlyList<WordRun>> map)
    {
        switch (block)
        {
            case WordHeading heading:
                seen++;
                return seen == target ? heading with { Runs = map(heading.Runs) } : heading;

            case WordParagraph paragraph:
                seen++;
                return seen == target ? paragraph with { Runs = map(paragraph.Runs) } : paragraph;

            case WordList list:
            {
                var items = new IReadOnlyList<WordRun>[list.Items.Count];
                for (int k = 0; k < list.Items.Count; k++)
                {
                    seen++;
                    items[k] = seen == target ? map(list.Items[k]) : list.Items[k];
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
                        cells[c] = seen == target ? new WordTableCell(map(cell.Runs)) : cell;
                    }

                    rows[r] = new WordTableRow(cells);
                }

                return new WordTable(rows);
            }

            default:
                return block; // images and any future container-less block
        }
    }

    // Toggles an inline format over [start, end): if every selected character already has it,
    // it is cleared, otherwise set.
    private static IReadOnlyList<WordRun> Toggle(
        IReadOnlyList<WordRun> runs, int start, int end, InlineFormat format)
    {
        bool enable = !AllSet(runs, start, end, format);
        return ApplyToRange(runs, start, end, run => WithFormat(run, format, enable));
    }

    // Strips all character formatting (emphasis + color) from [start, end), keeping links.
    private static IReadOnlyList<WordRun> ClearRange(IReadOnlyList<WordRun> runs, int start, int end) =>
        ApplyToRange(runs, start, end, run => run with
        {
            Bold = false,
            Italic = false,
            Underline = false,
            Strike = false,
            Color = null,
            Highlight = null,
        });

    // Splits runs at the [start, end) boundaries and applies a transform to the slice, then
    // coalesces. The shared core of every character-level model command.
    private static IReadOnlyList<WordRun> ApplyToRange(
        IReadOnlyList<WordRun> runs, int start, int end, Func<WordRun, WordRun> transform)
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

            result.Add(transform(run with { Text = run.Text[(s - runStart)..(e - runStart)] }));

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
    // split), so an edit never fragments the model more than necessary.
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
