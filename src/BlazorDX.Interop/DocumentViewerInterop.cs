using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the document-viewer TypeScript module
/// (<c>docviewer.js</c>). Only functional under WebAssembly; the server uses
/// <see cref="NullDocumentViewerInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class DocumentViewerInterop : IDocumentViewerInterop
{
    private const string ModuleName = "dx/docviewer.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/docviewer.js";

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

    public async ValueTask PrintAsync(string frameId)
    {
        await EnsureLoadedAsync();
        Print(frameId);
    }

    [JSImport("print", ModuleName)]
    private static partial void Print(string frameId);
}
