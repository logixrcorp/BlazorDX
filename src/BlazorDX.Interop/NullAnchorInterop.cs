namespace BlazorDX.Interop;

/// <summary>
/// Non-browser implementation of <see cref="IAnchorInterop"/>. There is no layout
/// to measure off-browser, so positioning is a no-op; the floating element renders
/// in normal document flow.
/// </summary>
public sealed class NullAnchorInterop : IAnchorInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask AttachAsync(string floatingId, string anchorId, string side, string align, int offset) =>
        ValueTask.CompletedTask;

    public ValueTask DetachAsync(string floatingId) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
