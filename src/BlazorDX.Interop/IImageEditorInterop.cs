namespace BlazorDX.Interop;

/// <summary>
/// The canvas operations <c>DxImageEditor</c> needs that WebAssembly cannot do
/// itself: decoding an image, repainting a canvas from it with a set of edits, and
/// downloading the result. The canvas is addressed by id so no ElementReference
/// crosses the interop boundary, and the edits travel as a JSON string.
/// </summary>
public interface IImageEditorInterop
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>Decodes a data-URL image and caches it as the source for the canvas.</summary>
    ValueTask LoadImageAsync(string canvasId, string dataUrl);

    /// <summary>Repaints the canvas from its source with <paramref name="editsJson"/>; returns the PNG data URL.</summary>
    ValueTask<string> RenderAsync(string canvasId, string editsJson);

    /// <summary>Downloads the current canvas contents as <paramref name="filename"/>.</summary>
    ValueTask DownloadAsync(string canvasId, string filename, string mime);

    /// <summary>Releases the cached source bitmap for the canvas.</summary>
    ValueTask DisposeCanvasAsync(string canvasId);
}
