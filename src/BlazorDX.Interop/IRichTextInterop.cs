namespace BlazorDX.Interop;

/// <summary>
/// Browser bridge for the WYSIWYG editor: applies formatting commands to a
/// contentEditable region and reads its live HTML back out. Only functional under
/// WebAssembly; the server uses a no-op implementation.
/// </summary>
public interface IRichTextInterop
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Applies a formatting command (e.g. <c>bold</c>, <c>insertUnorderedList</c>).</summary>
    ValueTask ExecAsync(string command, string value);

    /// <summary>
    /// Prompts for a URL and links the current selection. Only http/https/mailto URLs are
    /// applied; unsafe schemes are rejected in the browser bridge.
    /// </summary>
    ValueTask CreateLinkAsync();

    /// <summary>
    /// Prompts for a URL and returns it without touching the DOM (http/https/mailto only;
    /// empty on cancel or an unsafe scheme). Lets the model-driven core set the link itself.
    /// </summary>
    ValueTask<string> PromptLinkAsync();

    /// <summary>
    /// Applies a color to the last in-editor selection. <paramref name="command"/> is
    /// <c>foreColor</c> (text) or <c>hiliteColor</c> (highlight); <paramref name="color"/>
    /// is a CSS color. The bridge restores the remembered selection first.
    /// </summary>
    ValueTask ApplyColorAsync(string command, string color);

    /// <summary>
    /// Selects (and scrolls to) the next/previous occurrence of <paramref name="query"/> in
    /// the editor element, wrapping at the ends. Returns the 1-based index of the selected
    /// match, or 0 if there are none.
    /// </summary>
    ValueTask<int> FindInEditorAsync(string elementId, string query, bool forward, bool caseSensitive);

    /// <summary>
    /// Reports the caret's table position as <c>"tableIndex,rowIndex,colIndex"</c> (0-based),
    /// or an empty string when the caret is not inside a table.
    /// </summary>
    ValueTask<string> GetTableCellAsync(string elementId);

    /// <summary>
    /// Reports the current selection as <c>"containerIndex,start,end"</c>: the run-container
    /// (heading/paragraph/list-item/table-cell, in document order) and the character offsets
    /// within it. Empty when there is no selection, it spans more than one container, or it
    /// lies outside the editor. This is the owned selection the model-driven editing core
    /// maps commands onto (ADR-0015).
    /// </summary>
    ValueTask<string> GetSelectionRangeAsync(string elementId);

    /// <summary>
    /// Restores a selection addressed as a run-container index plus character offsets (the
    /// inverse of <see cref="GetSelectionRangeAsync"/>) and refocuses the editor.
    /// </summary>
    ValueTask SetSelectionRangeAsync(string elementId, int containerIndex, int start, int end);

    /// <summary>Returns the current inner HTML of the editor element.</summary>
    ValueTask<string> GetHtmlAsync(string elementId);

    /// <summary>Replaces the editor element's inner HTML (used to seed initial content).</summary>
    ValueTask SetHtmlAsync(string elementId, string html);

    /// <summary>Moves focus into the editor element.</summary>
    ValueTask FocusAsync(string elementId);

    /// <summary>
    /// Wires Ctrl/Cmd keyboard shortcuts on the editor element to <paramref name="onShortcut"/>,
    /// which receives the mapped command (<c>bold</c>/<c>italic</c>/<c>underline</c>/
    /// <c>createLink</c>/<c>undo</c>/<c>redo</c>). The bridge calls <c>preventDefault</c> only for
    /// handled shortcuts, so the browser's own contentEditable commands never bypass the model.
    /// </summary>
    ValueTask SubscribeShortcutsAsync(string elementId, Action<string> onShortcut);
}
