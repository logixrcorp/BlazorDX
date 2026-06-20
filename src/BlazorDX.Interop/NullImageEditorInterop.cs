namespace BlazorDX.Interop;

/// <summary>
/// Server-side / non-browser implementation of <see cref="IImageEditorInterop"/>.
/// There is no canvas to paint outside WebAssembly, so loads/renders are no-ops and
/// a render yields an empty data URL. The editor renders its chrome during SSR and
/// becomes functional once interactive in the browser.
/// </summary>
public sealed class NullImageEditorInterop : IImageEditorInterop
{
    public ValueTask EnsureLoadedAsync() => ValueTask.CompletedTask;

    public ValueTask LoadImageAsync(string canvasId, string dataUrl) => ValueTask.CompletedTask;

    public ValueTask<string> RenderAsync(string canvasId, string editsJson) =>
        ValueTask.FromResult(string.Empty);

    public ValueTask DownloadAsync(string canvasId, string filename, string mime) => ValueTask.CompletedTask;

    public ValueTask DisposeCanvasAsync(string canvasId) => ValueTask.CompletedTask;
}
