using BlazorDX.Interop;

namespace BlazorDX.Components.Tests;

/// <summary>
/// Off-DOM test double for <see cref="IRichTextInterop"/>: returns canned selection/table/image
/// data so the model-driven <c>DxWordEditor</c> commands can be exercised under bUnit (no JS),
/// and captures the restored selection and the keyboard-shortcut callback for assertions.
/// </summary>
internal sealed class FakeRichTextInterop : IRichTextInterop
{
    public string TableCell { get; init; } = string.Empty;
    public string SelectionRange { get; init; } = string.Empty;
    public string LinkUrl { get; init; } = string.Empty;
    public string ImageData { get; init; } = string.Empty; // "mime|base64" returned by the picker

    // Captures the last selection restore, so a test can assert the caret was put back.
    public (int Container, int Start, int End)? RestoredSelection { get; private set; }

    public void ClearRestored() => RestoredSelection = null;

    // Captures the keyboard-shortcut callback so a test can fire a shortcut without JS.
    public Action<string>? Shortcut { get; private set; }

    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;
    public ValueTask ExecAsync(string command, string value) => ValueTask.CompletedTask;
    public ValueTask CreateLinkAsync() => ValueTask.CompletedTask;
    public ValueTask<string> PromptLinkAsync() => ValueTask.FromResult(LinkUrl);
    public ValueTask<string> PickImageAsync() => ValueTask.FromResult(ImageData);
    public ValueTask ApplyColorAsync(string command, string color) => ValueTask.CompletedTask;
    public ValueTask<int> FindInEditorAsync(string e, string q, bool f, bool c) => ValueTask.FromResult(0);
    public ValueTask<string> GetTableCellAsync(string e) => ValueTask.FromResult(TableCell);
    public ValueTask<string> GetSelectionRangeAsync(string e) => ValueTask.FromResult(SelectionRange);

    public ValueTask SetSelectionRangeAsync(string e, int container, int start, int end)
    {
        RestoredSelection = (container, start, end);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetHtmlAsync(string e) => ValueTask.FromResult(string.Empty);
    public ValueTask SetHtmlAsync(string e, string h) => ValueTask.CompletedTask;
    public ValueTask FocusAsync(string e) => ValueTask.CompletedTask;

    public ValueTask SubscribeShortcutsAsync(string e, Action<string> onShortcut)
    {
        Shortcut = onShortcut;
        return ValueTask.CompletedTask;
    }
}
