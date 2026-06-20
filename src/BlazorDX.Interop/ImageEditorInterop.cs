using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the image-editor TypeScript module
/// (<c>image-editor.js</c>) using <see cref="JSImportAttribute"/>. Only functional
/// under WebAssembly; the server uses <see cref="NullImageEditorInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class ImageEditorInterop : IImageEditorInterop
{
    private const string ModuleName = "dx/image-editor.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/image-editor.js";

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

    public async ValueTask LoadImageAsync(string canvasId, string dataUrl)
    {
        await EnsureLoadedAsync();
        await LoadImage(canvasId, dataUrl);
    }

    public async ValueTask<string> RenderAsync(string canvasId, string editsJson)
    {
        await EnsureLoadedAsync();
        return Render(canvasId, editsJson);
    }

    public async ValueTask DownloadAsync(string canvasId, string filename, string mime)
    {
        await EnsureLoadedAsync();
        Download(canvasId, filename, mime);
    }

    public async ValueTask DisposeCanvasAsync(string canvasId)
    {
        if (isLoaded)
        {
            DisposeCanvas(canvasId);
        }

        await ValueTask.CompletedTask;
    }

    // The JS loadImage returns a Promise; marshalled as Task so the decode is awaited.
    [JSImport("loadImage", ModuleName)]
    private static partial Task LoadImage(string canvasId, string dataUrl);

    [JSImport("render", ModuleName)]
    private static partial string Render(string canvasId, string editsJson);

    [JSImport("download", ModuleName)]
    private static partial void Download(string canvasId, string filename, string mime);

    [JSImport("dispose", ModuleName)]
    private static partial void DisposeCanvas(string canvasId);
}
