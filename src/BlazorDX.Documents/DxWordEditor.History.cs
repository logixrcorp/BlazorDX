namespace BlazorDX.Documents;

/// <summary>
/// Undo/redo for <see cref="DxWordEditor"/> as a stack of editor-HTML snapshots. Each
/// committed change (a typing edit or a find/replace) pushes the prior state; undo/redo
/// re-seed the editor from a snapshot via the same re-mount path find/replace uses. This
/// fixes find/replace previously discarding the editor's history.
/// </summary>
/// <remarks>
/// History granularity is per change-event (one per oninput), not per character. Snapshots
/// are editor HTML (cheap to store and to re-seed); the model is re-derived on restore.
/// </remarks>
public sealed partial class DxWordEditor
{
    private const int MaxHistory = 200;

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string? _baselineHtml; // the HTML of the current committed state
    private bool _restoring;        // suppresses capture while we re-seed from a snapshot

    /// <summary>Whether there is a prior state to undo to.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether an undone state can be redone.</summary>
    public bool CanRedo => _redo.Count > 0;

    // Records that the editor content changed to newHtml, pushing the prior state so it can
    // be undone. A no-op during restore or when nothing actually changed.
    private void CaptureHistory(string newHtml)
    {
        if (_restoring)
        {
            return;
        }

        if (_baselineHtml is null)
        {
            _baselineHtml = newHtml;
            return;
        }

        if (newHtml == _baselineHtml)
        {
            return;
        }

        _undo.Push(_baselineHtml);
        TrimHistory(_undo);
        _redo.Clear();
        _baselineHtml = newHtml;
    }

    // Resets history to a freshly loaded document (a new external Document/DocxBytes).
    private void ResetHistory(string html)
    {
        _undo.Clear();
        _redo.Clear();
        _baselineHtml = html;
    }

    private async Task UndoAsync()
    {
        if (_undo.Count == 0 || _baselineHtml is null)
        {
            return;
        }

        _redo.Push(_baselineHtml);
        _baselineHtml = _undo.Pop();
        await RestoreAsync(_baselineHtml);
    }

    private async Task RedoAsync()
    {
        if (_redo.Count == 0 || _baselineHtml is null)
        {
            return;
        }

        _undo.Push(_baselineHtml);
        _baselineHtml = _redo.Pop();
        await RestoreAsync(_baselineHtml);
    }

    // Re-seeds the editor from a snapshot and surfaces the re-derived model. Mirrors the
    // find/replace re-mount: bump the key so DxRichTextEditor re-mounts with the new HTML.
    private async Task RestoreAsync(string html)
    {
        _restoring = true;
        try
        {
            WordDocument model = WordHtml.FromHtml(html);
            editorHtml = html;
            // Normalized form, so OnParametersSet (fired by DocumentChanged) won't re-seed.
            lastSeededHtml = WordHtml.ToHtml(model);
            dirty = true;
            editorEpoch++;

            if (DocumentChanged.HasDelegate)
            {
                await DocumentChanged.InvokeAsync(model);
            }

            if (OnSave.HasDelegate)
            {
                await OnSave.InvokeAsync(DocxWriter.Write(model));
            }

            StateHasChanged();
        }
        finally
        {
            _restoring = false;
        }
    }

    // Adopts a model edited in C# (find/replace, table ops): records history, re-seeds the
    // editor (re-mount), and surfaces the new document. The single funnel for model edits.
    private async Task CommitModelEditAsync(WordDocument updated)
    {
        string html = WordHtml.ToHtml(updated);
        CaptureHistory(html);
        editorHtml = html;
        lastSeededHtml = html;
        dirty = true;
        editorEpoch++; // re-mount so the editor shows the edited model

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

    // Adopts a model edited in C# WITHOUT re-mounting the editor: the surface HTML is
    // replaced in place (DxRichTextEditor.ReseedAsync) so the caller can immediately restore
    // the owned selection on the same instance. Used by the model-driven inline-format
    // commands (ADR-0015), where the re-mount path would discard the selection we want to
    // keep. Still records history and surfaces the new document, like CommitModelEditAsync.
    private async Task CommitModelEditInPlaceAsync(WordDocument updated)
    {
        string html = WordHtml.ToHtml(updated);
        CaptureHistory(html);
        editorHtml = html;
        lastSeededHtml = html;
        dirty = true;

        if (_rte is not null)
        {
            await _rte.ReseedAsync(html);
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

    private static void TrimHistory(Stack<string> stack)
    {
        if (stack.Count <= MaxHistory)
        {
            return;
        }

        // Drop the oldest (bottom) entry by rebuilding without it.
        string[] items = stack.ToArray(); // top..bottom
        stack.Clear();
        for (int i = items.Length - 2; i >= 0; i--) // skip items[^1] (oldest), re-push bottom..top
        {
            stack.Push(items[i]);
        }
    }
}
