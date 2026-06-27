using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the grid's TypeScript DOM helpers
/// (<c>grid-dom.js</c>). Only functional under WebAssembly; on the server the
/// grid renders its initial window without live scroll metrics.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class GridDomInterop : IGridDomInterop
{
    private const string ModuleName = "dx/grid-dom.js";
    // Relative to /_framework/; "../" reaches the app root's _content/ assets.
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/grid-dom.js";

    private bool isLoaded;

    public async ValueTask EnsureLoadedAsync()
    {
        if (isLoaded)
        {
            return;
        }

        await JSHost.ImportAsync(ModuleName, ModulePath);
        isLoaded = true;
    }

    public async ValueTask<(double ScrollTop, double ClientHeight, double ScrollHeight)> MeasureViewportAsync(
        string elementId)
    {
        await EnsureLoadedAsync();
        double[] metrics = MeasureViewport(elementId);
        return (metrics[0], metrics[1], metrics[2]);
    }

    public async ValueTask<(double ScrollTop, double ScrollLeft, double ClientHeight, double ClientWidth)>
        MeasureViewport2dAsync(string elementId)
    {
        await EnsureLoadedAsync();
        double[] m = MeasureViewport2d(elementId);
        return (m[0], m[1], m[2], m[3]);
    }

    public async ValueTask SubscribeScrollAsync(string elementId, Action onScroll)
    {
        await EnsureLoadedAsync();
        SubscribeScroll(elementId, onScroll);
    }

    public async ValueTask FocusFirstAsync(string elementId)
    {
        await EnsureLoadedAsync();
        FocusFirst(elementId);
    }

    public async ValueTask DownloadTextAsync(string filename, string mime, string content)
    {
        await EnsureLoadedAsync();
        DownloadText(filename, mime, content);
    }

    public async ValueTask DownloadBytesAsync(string filename, string mime, byte[] content)
    {
        await EnsureLoadedAsync();
        // Marshal as base64 text — a single string crosses the [JSImport] boundary
        // cheaply and AOT-safely; the JS side decodes it back to bytes for the Blob.
        DownloadBytes(filename, mime, Convert.ToBase64String(content));
    }

    public async ValueTask<bool> WriteClipboardAsync(string text)
    {
        await EnsureLoadedAsync();
        return await WriteClipboard(text);
    }

    public async ValueTask ScrollToAsync(string elementId, double top)
    {
        await EnsureLoadedAsync();
        ScrollTo(elementId, top);
    }

    public async ValueTask SuppressArrowKeysAsync(string elementId)
    {
        await EnsureLoadedAsync();
        SuppressArrowKeys(elementId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("downloadText", ModuleName)]
    private static partial void DownloadText(string filename, string mime, string content);

    [JSImport("downloadBytes", ModuleName)]
    private static partial void DownloadBytes(string filename, string mime, string base64);

    [JSImport("writeClipboard", ModuleName)]
    private static partial Task<bool> WriteClipboard(string text);

    [JSImport("scrollTo", ModuleName)]
    private static partial void ScrollTo(string elementId, double top);

    [JSImport("suppressArrowKeys", ModuleName)]
    private static partial void SuppressArrowKeys(string elementId);

    [JSImport("measureViewport", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] MeasureViewport(string elementId);

    [JSImport("measureViewport2d", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] MeasureViewport2d(string elementId);

    [JSImport("subscribeScroll", ModuleName)]
    private static partial void SubscribeScroll(
        string elementId,
        [JSMarshalAs<JSType.Function>] Action onScroll);

    [JSImport("focusFirst", ModuleName)]
    private static partial void FocusFirst(string elementId);
}
