namespace BlazorDX.Interop;

/// <summary>
/// Off-browser no-op rich-text bridge (static SSR / Interactive Server prerender),
/// where there is no DOM to format or read.
/// </summary>
public sealed class NullRichTextInterop : IRichTextInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask ExecAsync(string command, string value) => ValueTask.CompletedTask;

    public ValueTask CreateLinkAsync() => ValueTask.CompletedTask;

    public ValueTask<string> PromptLinkAsync() => ValueTask.FromResult(string.Empty);

    public ValueTask ApplyColorAsync(string command, string color) => ValueTask.CompletedTask;

    public ValueTask<int> FindInEditorAsync(string elementId, string query, bool forward, bool caseSensitive) =>
        ValueTask.FromResult(0);

    public ValueTask<string> GetTableCellAsync(string elementId) => ValueTask.FromResult(string.Empty);

    public ValueTask<string> GetSelectionRangeAsync(string elementId) => ValueTask.FromResult(string.Empty);

    public ValueTask SetSelectionRangeAsync(string elementId, int containerIndex, int start, int end) =>
        ValueTask.CompletedTask;

    public ValueTask<string> GetHtmlAsync(string elementId) => ValueTask.FromResult(string.Empty);

    public ValueTask SetHtmlAsync(string elementId, string html) => ValueTask.CompletedTask;

    public ValueTask FocusAsync(string elementId) => ValueTask.CompletedTask;
}
