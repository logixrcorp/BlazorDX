using BlazorDX.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorDX.Documents;

/// <summary>
/// Find &amp; replace for <see cref="DxWordEditor"/>. It operates on the document
/// <em>model</em> (run text), not the contentEditable selection, so it needs no DOM
/// plumbing: replacing rebuilds the <see cref="WordDocument"/> and re-seeds the editor.
/// </summary>
/// <remarks>
/// Matches are counted and replaced <b>within a single run</b> — a match split across a
/// formatting boundary (e.g. <c>wor</c>+<c>&lt;b&gt;d&lt;/b&gt;</c>) is not found. Replace
/// re-seeds the surface in place through the shared commit funnel (ADR-0015, Phase C); the
/// caret is not repositioned to the match.
/// </remarks>
public sealed partial class DxWordEditor
{
    private bool showFind;
    private string findText = string.Empty;
    private string replaceText = string.Empty;
    private bool caseSensitive;
    private int matchCount;
    private int currentMatch; // 1-based index of the match navigated to, or 0

    // Captured DxRichTextEditor instance, used to drive in-editor find-next selection.
    private DxRichTextEditor? _rte;

    private async Task FindNextAsync(bool forward)
    {
        if (_rte is null || findText.Length == 0)
        {
            return;
        }

        currentMatch = await _rte.FindNextAsync(findText, forward, caseSensitive);
        StateHasChanged();
    }

    private void ToggleFind()
    {
        showFind = !showFind;
        if (showFind)
        {
            matchCount = CountMatches(Current, findText, caseSensitive);
        }
    }

    private void BuildFindBar(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dx-word-findbar");
        builder.AddAttribute(2, "role", "search");

        builder.OpenElement(3, "input");
        builder.AddAttribute(4, "type", "text");
        builder.AddAttribute(5, "class", "dx-word-find-input");
        builder.AddAttribute(6, "placeholder", "Find");
        builder.AddAttribute(7, "aria-label", "Find");
        builder.AddAttribute(8, "value", findText);
        builder.AddAttribute(9, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnFindInput));
        builder.CloseElement();

        builder.OpenElement(10, "span");
        builder.AddAttribute(11, "class", "dx-word-find-count");
        builder.AddAttribute(12, "aria-live", "polite");
        builder.AddContent(13, findText.Length == 0
            ? string.Empty
            : currentMatch > 0 ? $"{currentMatch} of {matchCount}"
            : matchCount == 1 ? "1 match" : $"{matchCount} matches");
        builder.CloseElement();

        builder.OpenElement(14, "input");
        builder.AddAttribute(15, "type", "text");
        builder.AddAttribute(16, "class", "dx-word-replace-input");
        builder.AddAttribute(17, "placeholder", "Replace with");
        builder.AddAttribute(18, "aria-label", "Replace with");
        builder.AddAttribute(19, "value", replaceText);
        builder.AddAttribute(20, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, OnReplaceInput));
        builder.CloseElement();

        builder.OpenElement(21, "label");
        builder.AddAttribute(22, "class", "dx-word-find-case");
        builder.OpenElement(23, "input");
        builder.AddAttribute(24, "type", "checkbox");
        builder.AddAttribute(25, "checked", caseSensitive);
        builder.AddAttribute(26, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, OnCaseToggle));
        builder.CloseElement();
        builder.AddContent(27, "Match case");
        builder.CloseElement();

        FindButton(builder, 30, "Replace", ReplaceOneAsync);
        FindButton(builder, 40, "Replace all", ReplaceAllAsync);

        // Previous / next match navigation (selects + scrolls to the match in the editor).
        FindButton(builder, 100, "‹", PreviousMatchAsync, "Previous match");
        FindButton(builder, 110, "›", NextMatchAsync, "Next match");

        builder.CloseElement();
    }

    private void FindButton(RenderTreeBuilder builder, int seq, string text, Func<Task> handler, string? label = null)
    {
        builder.OpenElement(seq, "button");
        builder.AddAttribute(seq + 1, "type", "button");
        builder.AddAttribute(seq + 2, "class", "dx-word-find-btn");
        builder.AddAttribute(seq + 3, "onclick", EventCallback.Factory.Create(this, handler));
        if (label is not null)
        {
            builder.AddAttribute(seq + 4, "aria-label", label);
            builder.AddAttribute(seq + 5, "title", label);
        }

        builder.AddContent(seq + 6, text);
        builder.CloseElement();
    }

    private Task NextMatchAsync() => FindNextAsync(forward: true);

    private Task PreviousMatchAsync() => FindNextAsync(forward: false);

    private void OnFindInput(ChangeEventArgs args)
    {
        findText = args.Value?.ToString() ?? string.Empty;
        currentMatch = 0; // navigation position is no longer valid for a changed query
        matchCount = CountMatches(Current, findText, caseSensitive);
    }

    private void OnReplaceInput(ChangeEventArgs args) =>
        replaceText = args.Value?.ToString() ?? string.Empty;

    private void OnCaseToggle(ChangeEventArgs args)
    {
        caseSensitive = args.Value is bool b && b;
        currentMatch = 0;
        matchCount = CountMatches(Current, findText, caseSensitive);
    }

    private Task ReplaceOneAsync() => ReplaceAsync(all: false);

    private Task ReplaceAllAsync() => ReplaceAsync(all: true);

    private async Task ReplaceAsync(bool all)
    {
        if (findText.Length == 0)
        {
            return;
        }

        WordDocument updated = ReplaceInDocument(Current, findText, replaceText, caseSensitive, all);
        matchCount = CountMatches(updated, findText, caseSensitive);
        await CommitModelEditAsync(updated); // records history + re-seeds + raises DocumentChanged
    }

    // ---- Model transforms (within-run) -------------------------------------------

    private static int CountMatches(WordDocument document, string find, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(find))
        {
            return 0;
        }

        StringComparison cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int count = 0;
        ForEachRunList(document, runs =>
        {
            foreach (WordRun run in runs)
            {
                int i = 0;
                while ((i = run.Text.IndexOf(find, i, cmp)) >= 0)
                {
                    count++;
                    i += find.Length;
                }
            }
        });

        return count;
    }

    private static WordDocument ReplaceInDocument(
        WordDocument document, string find, string replace, bool caseSensitive, bool all)
    {
        if (string.IsNullOrEmpty(find))
        {
            return document;
        }

        StringComparison cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        bool firstDone = false;

        string MapText(string text)
        {
            if (all)
            {
                return text.Replace(find, replace, cmp);
            }

            if (firstDone)
            {
                return text;
            }

            int idx = text.IndexOf(find, cmp);
            if (idx < 0)
            {
                return text;
            }

            firstDone = true;
            return string.Concat(text.AsSpan(0, idx), replace, text.AsSpan(idx + find.Length));
        }

        WordRun MapRun(WordRun run) => run with { Text = MapText(run.Text) };

        WordRun[] MapRuns(IReadOnlyList<WordRun> runs)
        {
            var mapped = new WordRun[runs.Count];
            for (int i = 0; i < runs.Count; i++)
            {
                mapped[i] = MapRun(runs[i]);
            }

            return mapped;
        }

        var blocks = new WordBlock[document.Blocks.Count];
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            blocks[i] = document.Blocks[i] switch
            {
                WordHeading h => new WordHeading(h.Level, MapRuns(h.Runs)),
                WordParagraph p => new WordParagraph(MapRuns(p.Runs)),
                WordList l => new WordList(l.Ordered, MapItems(l.Items, MapRuns), l.Levels),
                WordTable t => new WordTable(MapRows(t.Rows, MapRuns)),
                WordBlock other => other,
            };
        }

        return new WordDocument(blocks);
    }

    private static IReadOnlyList<IReadOnlyList<WordRun>> MapItems(
        IReadOnlyList<IReadOnlyList<WordRun>> items, Func<IReadOnlyList<WordRun>, WordRun[]> mapRuns)
    {
        var mapped = new IReadOnlyList<WordRun>[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            mapped[i] = mapRuns(items[i]);
        }

        return mapped;
    }

    private static IReadOnlyList<WordTableRow> MapRows(
        IReadOnlyList<WordTableRow> rows, Func<IReadOnlyList<WordRun>, WordRun[]> mapRuns)
    {
        var mapped = new WordTableRow[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            var cells = new WordTableCell[rows[r].Cells.Count];
            for (int c = 0; c < rows[r].Cells.Count; c++)
            {
                cells[c] = new WordTableCell(mapRuns(rows[r].Cells[c].Runs));
            }

            mapped[r] = new WordTableRow(cells);
        }

        return mapped;
    }

    private static void ForEachRunList(WordDocument document, Action<IReadOnlyList<WordRun>> action)
    {
        foreach (WordBlock block in document.Blocks)
        {
            switch (block)
            {
                case WordHeading h:
                    action(h.Runs);
                    break;
                case WordParagraph p:
                    action(p.Runs);
                    break;
                case WordList l:
                    foreach (IReadOnlyList<WordRun> item in l.Items)
                    {
                        action(item);
                    }

                    break;
                case WordTable t:
                    foreach (WordTableRow row in t.Rows)
                    {
                        foreach (WordTableCell cell in row.Cells)
                        {
                            action(cell.Runs);
                        }
                    }

                    break;
            }
        }
    }
}
