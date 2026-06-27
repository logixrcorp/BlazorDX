namespace BlazorDX.Documents;

/// <summary>
/// Undo/redo for <see cref="DxWordEditor"/> as a stack of <em>model</em> states (ADR-0015,
/// Phase C). Each committed change pushes the prior <see cref="WordDocument"/>; undo/redo pop
/// a state and re-seed the editing surface <b>in place</b> (no re-mount), restoring the owned
/// selection when one was captured with the edit. The model — not editor HTML — is the unit
/// of history, so a restore is exact (no HTML re-parse) and the caret survives.
/// </summary>
/// <remarks>
/// History granularity is per change-event (one per oninput / one per model command), not per
/// character. Each entry also carries the state's serialized HTML (for the in-place re-seed
/// and cheap change-detection) and an optional <c>"container,start,end"</c> selection.
/// </remarks>
public sealed partial class DxWordEditor
{
    private const int MaxHistory = 200;

    // One committed editor state: the authoritative model, its serialized HTML (for the
    // in-place re-seed and dedup), and the owned selection to restore (null when unknown).
    private readonly record struct HistoryEntry(WordDocument Model, string Html, string? Selection);

    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private HistoryEntry? _baseline; // the current committed state
    private bool _restoring;          // suppresses capture while we re-seed from a snapshot

    /// <summary>Whether there is a prior state to undo to.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether an undone state can be redone.</summary>
    public bool CanRedo => _redo.Count > 0;

    // Records that the editor moved to a new committed state, pushing the prior one so it can
    // be undone. A no-op during restore or when the serialized state is unchanged.
    private void CaptureHistory(WordDocument model, string html, string? selection)
    {
        if (_restoring)
        {
            return;
        }

        if (_baseline is null)
        {
            _baseline = new HistoryEntry(model, html, selection);
            return;
        }

        if (html == _baseline.Value.Html)
        {
            return;
        }

        _undo.Push(_baseline.Value);
        TrimHistory(_undo);
        _redo.Clear();
        _baseline = new HistoryEntry(model, html, selection);
    }

    // Resets history to a freshly loaded document (a new external Document/DocxBytes).
    private void ResetHistory(WordDocument model, string html)
    {
        _undo.Clear();
        _redo.Clear();
        _baseline = new HistoryEntry(model, html, null);
    }

    private async Task UndoAsync()
    {
        if (_undo.Count == 0 || _baseline is null)
        {
            return;
        }

        _redo.Push(_baseline.Value);
        _baseline = _undo.Pop();
        await RestoreAsync(_baseline.Value);
    }

    private async Task RedoAsync()
    {
        if (_redo.Count == 0 || _baseline is null)
        {
            return;
        }

        _undo.Push(_baseline.Value);
        _baseline = _redo.Pop();
        await RestoreAsync(_baseline.Value);
    }

    // Re-seeds the editor from a model state in place (no re-mount) and surfaces it. Restores
    // the captured selection so the caret survives an undo/redo.
    private async Task RestoreAsync(HistoryEntry entry)
    {
        _restoring = true;
        try
        {
            editorHtml = entry.Html;
            lastSeededHtml = entry.Html;
            dirty = true;

            if (_rte is not null)
            {
                await _rte.ReseedAsync(entry.Html);
                await RestoreSelectionAsync(entry.Selection);
            }

            if (DocumentChanged.HasDelegate)
            {
                await DocumentChanged.InvokeAsync(entry.Model);
            }

            if (OnSave.HasDelegate)
            {
                await OnSave.InvokeAsync(DocxWriter.Write(entry.Model));
            }

            StateHasChanged();
        }
        finally
        {
            _restoring = false;
        }
    }

    // Adopts a model edited in C# (find/replace, table ops, model-driven formatting): records
    // history, re-seeds the surface in place, restores the selection, and surfaces the new
    // document. The single funnel for model edits — no re-mount, so the caret is preserved.
    private async Task CommitModelEditAsync(WordDocument updated, string? selection = null)
    {
        string html = WordHtml.ToHtml(updated);
        CaptureHistory(updated, html, selection);
        editorHtml = html;
        lastSeededHtml = html;
        dirty = true;

        if (_rte is not null)
        {
            await _rte.ReseedAsync(html);
            await RestoreSelectionAsync(selection);
        }

        if (DocumentChanged.HasDelegate)
        {
            await DocumentChanged.InvokeAsync(updated);
        }

        if (OnSave.HasDelegate)
        {
            await OnSave.InvokeAsync(DocxWriter.Write(updated));
        }

        StateHasChanged();
    }

    // Puts the caret back after an in-place re-seed, given a "container,start,end" selection.
    private async Task RestoreSelectionAsync(string? selection)
    {
        if (_rte is not null && selection is not null
            && TryParseRange(selection, out int container, out int start, out int end))
        {
            await _rte.SetSelectionRangeAsync(container, start, end);
        }
    }

    private static void TrimHistory(Stack<HistoryEntry> stack)
    {
        if (stack.Count <= MaxHistory)
        {
            return;
        }

        // Drop the oldest (bottom) entry by rebuilding without it.
        HistoryEntry[] items = stack.ToArray(); // top..bottom
        stack.Clear();
        for (int i = items.Length - 2; i >= 0; i--) // skip items[^1] (oldest), re-push bottom..top
        {
            stack.Push(items[i]);
        }
    }
}
