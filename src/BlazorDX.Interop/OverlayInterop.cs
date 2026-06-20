using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the overlay TypeScript module (<c>overlay.js</c>).
/// Only functional under WebAssembly; the server uses <see cref="NullOverlayInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class OverlayInterop : IOverlayInterop
{
    private const string ModuleName = "dx/overlay.js";

    // Relative to /_framework/; "../" reaches the app root's _content/ assets.
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/overlay.js";

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

    public async ValueTask OpenAsync(
        string elementId,
        string ignoreElementId,
        bool trapFocus,
        bool lockScroll,
        bool closeOnEscape,
        bool closeOnOutsideClick,
        Action onDismiss)
    {
        await EnsureLoadedAsync();
        Open(elementId, ignoreElementId, trapFocus, lockScroll, closeOnEscape, closeOnOutsideClick, onDismiss);
    }

    public async ValueTask CloseAsync(string elementId)
    {
        await EnsureLoadedAsync();
        Close(elementId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("open", ModuleName)]
    private static partial void Open(
        string elementId,
        string ignoreId,
        bool trapFocus,
        bool lockScroll,
        bool closeOnEscape,
        bool closeOnOutsideClick,
        [JSMarshalAs<JSType.Function>] Action onDismiss);

    [JSImport("close", ModuleName)]
    private static partial void Close(string elementId);
}
