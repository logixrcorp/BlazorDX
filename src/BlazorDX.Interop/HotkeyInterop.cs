using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the hotkeys TypeScript module (<c>hotkeys.js</c>).
/// Only functional under WebAssembly; the server uses <see cref="NullHotkeyInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class HotkeyInterop : IHotkeyInterop
{
    private const string ModuleName = "dx/hotkeys.js";
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/hotkeys.js";

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

    public async ValueTask SubscribeAsync(Action<string> onMatch)
    {
        await EnsureLoadedAsync();
        Subscribe(onMatch);
    }

    public async ValueTask SetBindingsAsync(string[] combos)
    {
        await EnsureLoadedAsync();
        SetBindings(combos);
    }

    public async ValueTask UnsubscribeAsync()
    {
        if (isLoaded)
        {
            Unsubscribe();
        }

        await ValueTask.CompletedTask;
    }

    [JSImport("subscribe", ModuleName)]
    private static partial void Subscribe(
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onMatch);

    [JSImport("setBindings", ModuleName)]
    private static partial void SetBindings(
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] combos);

    [JSImport("unsubscribe", ModuleName)]
    private static partial void Unsubscribe();
}
