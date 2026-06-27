namespace BlazorDX.Documents;

/// <summary>
/// Model-driven list toggling for <see cref="DxWordEditor"/> (ADR-0015, Phase D): the
/// <c>insertUnorderedList</c> / <c>insertOrderedList</c> toolbar commands convert the caret's
/// block between a paragraph and a list item over the document <em>model</em>.
/// </summary>
/// <remarks>
/// The transform flattens the document into a line sequence (paragraphs and list <em>items</em>
/// become individual lines; headings/tables/images pass through), toggles the one targeted
/// line, then rebuilds — regrouping consecutive same-kind list lines into <see cref="WordList"/>
/// blocks. Merge (two adjacent items become one list) and split (un-listing a middle item
/// breaks one list into two) both fall out of the regroup for free. Headings, tables and
/// images never become list items, so the command is a no-op on them. Converting preserves the
/// run-container count and order, so the owned selection still maps after the edit.
/// </remarks>
public sealed partial class DxWordEditor
{
    private abstract record Line(int Container);

    private sealed record ParaLine(IReadOnlyList<WordRun> Runs, WordAlignment Align, int Container) : Line(Container);

    private sealed record ItemLine(IReadOnlyList<WordRun> Runs, bool Ordered, int Level, int Container) : Line(Container);

    private sealed record OtherLine(WordBlock Block, int Container) : Line(Container);

    // Returns a copy of the document with the list state toggled for the run-container at
    // containerIndex: paragraph -> list item, same-kind list item -> paragraph (un-list),
    // or other-kind list item -> the requested kind. A no-op when the target isn't a
    // paragraph or list item.
    private static WordDocument SetList(WordDocument document, int containerIndex, bool ordered)
    {
        List<Line> lines = Flatten(document);

        int index = lines.FindIndex(l => l.Container == containerIndex && l is ParaLine or ItemLine);
        if (index < 0)
        {
            return document; // caret is on a heading/table/image, or out of range
        }

        lines[index] = ToggleListLine(lines[index], ordered);
        return new WordDocument(Rebuild(lines));
    }

    private static Line ToggleListLine(Line target, bool ordered) => target switch
    {
        ParaLine p => new ItemLine(p.Runs, ordered, 0, p.Container),
        ItemLine i when i.Ordered == ordered => new ParaLine(i.Runs, WordAlignment.Start, i.Container),
        ItemLine i => new ItemLine(i.Runs, ordered, i.Level, i.Container), // switch list kind, keep level
        _ => target,
    };

    // Expands blocks into one line per run-container, tracking the running container index so
    // a line can be matched to the owned selection. A WordList expands to one line per item; a
    // table is one OtherLine but still advances the index by its cell count.
    private static List<Line> Flatten(WordDocument document)
    {
        var lines = new List<Line>(document.Blocks.Count);
        int container = -1;
        foreach (WordBlock block in document.Blocks)
        {
            switch (block)
            {
                case WordParagraph p:
                    lines.Add(new ParaLine(p.Runs, p.Alignment, ++container));
                    break;

                case WordList list:
                    for (int k = 0; k < list.Items.Count; k++)
                    {
                        lines.Add(new ItemLine(list.Items[k], list.Ordered, list.LevelOf(k), ++container));
                    }

                    break;

                case WordHeading h:
                    lines.Add(new OtherLine(h, ++container));
                    break;

                case WordTable t:
                    foreach (WordTableRow row in t.Rows)
                    {
                        container += row.Cells.Count;
                    }

                    lines.Add(new OtherLine(t, container));
                    break;

                default: // WordImage and any future container-less block
                    lines.Add(new OtherLine(block, container));
                    break;
            }
        }

        return lines;
    }

    // Rebuilds blocks from lines, coalescing consecutive list items of the same kind into one
    // WordList (the inverse of Flatten).
    private static WordBlock[] Rebuild(List<Line> lines)
    {
        var blocks = new List<WordBlock>(lines.Count);
        int i = 0;
        while (i < lines.Count)
        {
            if (lines[i] is ItemLine first)
            {
                var items = new List<IReadOnlyList<WordRun>>();
                var levels = new List<int>();
                while (i < lines.Count && lines[i] is ItemLine item && item.Ordered == first.Ordered)
                {
                    items.Add(item.Runs);
                    levels.Add(item.Level);
                    i++;
                }

                bool nested = levels.Exists(l => l > 0);
                blocks.Add(new WordList(first.Ordered, items, nested ? levels : null));
            }
            else if (lines[i] is ParaLine p)
            {
                blocks.Add(new WordParagraph(p.Runs, p.Align));
                i++;
            }
            else
            {
                blocks.Add(((OtherLine)lines[i]).Block);
                i++;
            }
        }

        return blocks.ToArray();
    }
}
