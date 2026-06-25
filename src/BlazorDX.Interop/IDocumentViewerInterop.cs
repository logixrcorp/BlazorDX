namespace BlazorDX.Interop;

/// <summary>
/// Browser bridge for the document viewer's print action. The native
/// <c>&lt;embed&gt;</c>/<c>&lt;iframe&gt;</c> PDF viewer has no scriptable print API,
/// so printing falls back to the host window's print dialog (the user's browser then
/// prints the focused document). Only functional under WebAssembly; off-browser the
/// no-op <see cref="NullDocumentViewerInterop"/> is used.
/// </summary>
public interface IDocumentViewerInterop
{
    /// <summary>Ensures the underlying JavaScript module has been imported.</summary>
    ValueTask EnsureLoadedAsync();

    /// <summary>
    /// Prints the embedded document. When <paramref name="frameId"/> names a same-origin
    /// <c>&lt;iframe&gt;</c>/<c>&lt;embed&gt;</c> the bridge prints that frame's own
    /// content window; otherwise (cross-origin, or a plain <c>&lt;embed&gt;</c> with no
    /// reachable content window) it falls back to printing the host window.
    /// </summary>
    ValueTask PrintAsync(string frameId);
}
