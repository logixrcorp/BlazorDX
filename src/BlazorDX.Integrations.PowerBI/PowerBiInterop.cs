using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Integrations.PowerBI;

/// <summary>
/// Compile-time-bound bridge to the Power BI ESM wrapper (<c>dx-powerbi.js</c>)
/// using <see cref="JSImportAttribute"/> — never <c>IJSRuntime</c> (ADR 0013). Only
/// functional under WebAssembly; the server uses <see cref="NullPowerBiInterop"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class PowerBiInterop : IPowerBiInterop
{
    private const string ModuleName = "BlazorDX.Integrations.PowerBI/dx-powerbi.js";
    private const string ModulePath = "../_content/BlazorDX.Integrations.PowerBI/dx-powerbi.js";

    private bool isLoaded;

    /// <inheritdoc />
    public async ValueTask EnsureLoadedAsync()
    {
        if (isLoaded)
        {
            return;
        }

        await JSHost.ImportAsync(ModuleName, ModulePath);
        isLoaded = true;
    }

    /// <inheritdoc />
    public async ValueTask EmbedAsync(string elementId, string configJson)
    {
        await EnsureLoadedAsync();
        // The wrapper's embed() returns a Promise (it may await the CDN load), so it
        // is marshalled as Task and awaited here.
        await Embed(elementId, configJson);
    }

    /// <inheritdoc />
    public async ValueTask UnmountAsync(string elementId)
    {
        if (isLoaded)
        {
            Unmount(elementId);
        }

        await ValueTask.CompletedTask;
    }

    [JSImport("embed", ModuleName)]
    private static partial Task Embed(string elementId, string configJson);

    [JSImport("unmount", ModuleName)]
    private static partial void Unmount(string elementId);
}
