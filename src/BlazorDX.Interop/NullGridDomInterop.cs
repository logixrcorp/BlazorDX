namespace BlazorDX.Interop;

/// <summary>
/// Server-side / non-browser implementation of <see cref="IGridDomInterop"/>.
/// There is no DOM to measure outside WebAssembly, so it reports an empty
/// viewport and ignores subscriptions. The grid falls back to rendering its
/// initial window, which is exactly what static SSR should produce.
/// </summary>
public sealed class NullGridDomInterop : IGridDomInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask<(double ScrollTop, double ClientHeight, double ScrollHeight)> MeasureViewportAsync(
        string elementId) => ValueTask.FromResult<(double, double, double)>((0, 0, 0));

    public ValueTask<(double ScrollTop, double ScrollLeft, double ClientHeight, double ClientWidth)>
        MeasureViewport2dAsync(string elementId) =>
        ValueTask.FromResult<(double, double, double, double)>((0, 0, 0, 0));

    public ValueTask SubscribeScrollAsync(string elementId, Action onScroll) => ValueTask.CompletedTask;

    public ValueTask FocusFirstAsync(string elementId) => ValueTask.CompletedTask;

    public ValueTask DownloadTextAsync(string filename, string mime, string content) => ValueTask.CompletedTask;

    public ValueTask DownloadBytesAsync(string filename, string mime, byte[] content) => ValueTask.CompletedTask;

    public ValueTask<bool> WriteClipboardAsync(string text) => ValueTask.FromResult(true);

    public ValueTask ScrollToAsync(string elementId, double top) => ValueTask.CompletedTask;

    public ValueTask SuppressArrowKeysAsync(string elementId) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
