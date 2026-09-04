using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Compile-time-bound bridge to the chart zoom/pan DOM helper (<c>chart-zoom.js</c>). Only
/// functional under WebAssembly; on the server a zoomable chart's drag-pan gesture simply
/// doesn't start (see <see cref="NullChartZoomInterop"/>) since there's no live layout to measure.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class ChartZoomInterop : IChartZoomInterop
{
    private const string ModuleName = "dx/chart-zoom.js";
    // Relative to /_framework/; "../" reaches the app root's _content/ assets.
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/chart-zoom.js";

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

    public async ValueTask<double> MeasureWidthAsync(string elementId)
    {
        await EnsureLoadedAsync();
        return MeasureWidth(elementId);
    }

    public async ValueTask<(double Left, double Top)> MeasureOffsetAsync(string elementId)
    {
        await EnsureLoadedAsync();
        double[] offset = MeasureOffset(elementId);
        return (offset[0], offset[1]);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [JSImport("measureWidth", ModuleName)]
    private static partial double MeasureWidth(string elementId);

    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    [JSImport("measureOffset", ModuleName)]
    private static partial double[] MeasureOffset(string elementId);
}
