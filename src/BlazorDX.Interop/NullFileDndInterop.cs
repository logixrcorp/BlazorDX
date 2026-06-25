namespace BlazorDX.Interop;

/// <summary>
/// Non-browser implementation of <see cref="IFileDndInterop"/>. There is no DOM to
/// attach native drag-and-drop listeners to off-browser, so every method is a no-op.
/// The file manager's keyboard "move" buttons and its <c>InputFile</c> upload path
/// remain fully functional without it (drag-and-drop is an enhancement only).
/// </summary>
public sealed class NullFileDndInterop : IFileDndInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask RegisterDraggableAsync(string elementId) => ValueTask.CompletedTask;

    public ValueTask RegisterDropTargetAsync(
        string elementId,
        Action<string, string> onMove,
        Action<IReadOnlyList<DroppedFile>> onFiles) => ValueTask.CompletedTask;

    public ValueTask UnregisterAsync(string elementId) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
