using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BlazorDX.Interop;

/// <summary>
/// Drives the Rust <c>dx_grid</c> wasm kernels from C# through the TypeScript
/// bridge, using compile-time <see cref="JSImportAttribute"/> bindings (never the
/// reflection-based IJSRuntime path). The grid kernels return row-index arrays,
/// which we narrow from the JS number representation back to <see cref="int"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed partial class GridWasmInterop : IGridWasmInterop
{
    // Logical module name (must match the [JSImport] module argument) and the
    // static-web-asset path the WebAssembly host fetches it from.
    private const string ModuleName = "dx/grid-interop.js";
    // Resolved relative to the .NET wasm runtime at /_framework/, so "../" steps
    // up to the app root where RCL static assets live under _content/.
    private const string ModulePath = "../_content/BlazorDX.Interop/dx/grid-interop.js";

    private bool isLoaded;

    public async ValueTask EnsureLoadedAsync()
    {
        if (isLoaded)
        {
            return;
        }

        await JSHost.ImportAsync(ModuleName, ModulePath);
        await EnsureWasmLoaded();
        isLoaded = true;
    }

    public async ValueTask<int[]> SortAsync(double[] values, bool descending)
    {
        await EnsureLoadedAsync();
        return Narrow(SortIndices(values, descending));
    }

    public async ValueTask<int[]> FilterGreaterOrEqualAsync(double[] values, double threshold)
    {
        await EnsureLoadedAsync();
        return Narrow(FilterIndicesGte(values, threshold));
    }

    public async ValueTask<double[]> AggregateAsync(double[] values)
    {
        await EnsureLoadedAsync();
        return Aggregate(values);
    }

    public async ValueTask<int[]> HistogramAsync(double[] values, int bins, double min, double max)
    {
        await EnsureLoadedAsync();
        return Narrow(Histogram(values, bins, min, max));
    }

    public async ValueTask<int[]> DownsampleAsync(double[] x, double[] y, int threshold)
    {
        await EnsureLoadedAsync();
        return Narrow(DownsampleLttb(x, y, threshold));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Row indices arrive as JS numbers (doubles); convert to the int[] the grid uses.
    private static int[] Narrow(double[] indices)
    {
        int[] result = new int[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            result[i] = (int)indices[i];
        }

        return result;
    }

    // Loading the wasm is the only asynchronous step (Promise -> Task is a
    // supported marshalling). The kernels below are synchronous array calls.
    [JSImport("ensureLoaded", ModuleName)]
    private static partial Task EnsureWasmLoaded();

    [JSImport("sortIndices", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] SortIndices(
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] values,
        bool descending);

    [JSImport("filterIndicesGte", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] FilterIndicesGte(
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] values,
        double threshold);

    [JSImport("aggregate", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] Aggregate(
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] values);

    [JSImport("histogram", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] Histogram(
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] values,
        int bins,
        double min,
        double max);

    [JSImport("downsampleLttb", ModuleName)]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial double[] DownsampleLttb(
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] x,
        [JSMarshalAs<JSType.Array<JSType.Number>>] double[] y,
        int threshold);
}
