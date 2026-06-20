using System.Runtime.Versioning;
using BlazorDX.Interop;

namespace BlazorDX.Compute;

/// <summary>
/// Browser implementation of <see cref="IGridCompute"/> that delegates to the
/// Rust <c>dx_grid</c> wasm kernels through <see cref="IGridWasmInterop"/>. Only
/// resolved when running under WebAssembly; the server path uses
/// <see cref="ManagedGridCompute"/>.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class RustGridCompute(IGridWasmInterop interop) : IGridCompute
{
    public string Backend => "Rust (WebAssembly)";

    public ValueTask<int[]> SortAsync(double[] values, bool descending) =>
        interop.SortAsync(values, descending);

    public ValueTask<int[]> FilterGreaterOrEqualAsync(double[] values, double threshold) =>
        interop.FilterGreaterOrEqualAsync(values, threshold);

    public async ValueTask<GridAggregate> AggregateAsync(double[] values)
    {
        // [count, sum, min, max, mean] from the Rust kernel.
        double[] stats = await interop.AggregateAsync(values);
        return new GridAggregate(stats[0], stats[1], stats[2], stats[3], stats[4]);
    }

    public ValueTask<int[]> HistogramAsync(double[] values, int bins, double min, double max) =>
        interop.HistogramAsync(values, bins, min, max);

    public ValueTask<int[]> DownsampleAsync(double[] x, double[] y, int threshold) =>
        interop.DownsampleAsync(x, y, threshold);
}
