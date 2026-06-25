namespace BlazorDX.Interop;

/// <summary>
/// Off-browser no-op document-viewer bridge (static SSR / Interactive Server
/// prerender), where there is no window to print from.
/// </summary>
public sealed class NullDocumentViewerInterop : IDocumentViewerInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask PrintAsync(string frameId) => ValueTask.CompletedTask;
}
