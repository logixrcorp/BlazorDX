using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the anchored-positioning TypeScript module
/// (<c>positioning.js</c>). Only functional under WebAssembly; the server uses
/// <see cref="NullAnchorInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class AnchorInterop : IAnchorInterop
{
    private const string ModuleName = "dx/positioning.js";

    // Relative to /_framework/; "../" reaches the app root's _content/ assets.
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/positioning.js";

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

    public async ValueTask AttachAsync(string floatingId, string anchorId, string side, string align, int offset)
    {
        await EnsureLoadedAsync();
        Attach(floatingId, anchorId, side, align, offset);
    }

    public async ValueTask DetachAsync(string floatingId)
    {
        await EnsureLoadedAsync();
        Detach(floatingId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("attach", ModuleName)]
    private static partial void Attach(string floatingId, string anchorId, string side, string align, int offset);

    [JSImport("detach", ModuleName)]
    private static partial void Detach(string floatingId);
}
